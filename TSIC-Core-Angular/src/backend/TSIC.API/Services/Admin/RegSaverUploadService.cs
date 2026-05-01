using OfficeOpenXml;
using TSIC.Contracts.Dtos.RegSaverUpload;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Imports RegSaver monthly payouts (Vertical Insure) from an Excel export.
/// Mirrors legacy <c>FileUploadService.UploadVerticalInsureMonthlyPayout</c>:
/// dedupe on PolicyNumber, only insert new policies, surface per-row errors.
/// </summary>
public class RegSaverUploadService : IRegSaverUploadService
{
    private static readonly string[] RequiredHeaders =
        ["PolicyNumber", "PurchaseDate", "EffectiveDate", "Premium", "Payout"];

    private readonly IVerticalInsurePayoutsRepository _repo;

    public RegSaverUploadService(IVerticalInsurePayoutsRepository repo)
    {
        _repo = repo;
    }

    public async Task<RegSaverUploadResultDto> ProcessUploadAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        ExcelPackage.License.SetNonCommercialPersonal("Todd Greenwald");
        using var package = new ExcelPackage(fileStream);

        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws == null)
        {
            return Empty([new RegSaverUploadRowError
            {
                Row = 0,
                PolicyNumber = string.Empty,
                Reason = "No worksheet found in the uploaded file."
            }]);
        }

        var colMap = ResolveColumnMap(ws);
        var missing = RequiredHeaders.Where(h => colMap[h] == 0).ToList();
        if (missing.Count > 0)
        {
            return Empty([new RegSaverUploadRowError
            {
                Row = 1,
                PolicyNumber = string.Empty,
                Reason = $"Missing required column(s): {string.Join(", ", missing)}."
            }]);
        }

        var errors = new List<RegSaverUploadRowError>();
        var parsed = new List<ParsedRow>();
        var rowCount = ws.Dimension?.Rows ?? 0;

        for (var row = 2; row <= rowCount; row++)
        {
            var policyNumber = ws.Cells[row, colMap["PolicyNumber"]].Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(policyNumber))
                continue; // skip blank rows entirely

            var purchaseDateRaw = ws.Cells[row, colMap["PurchaseDate"]].Text?.Trim() ?? string.Empty;
            var effectiveDateRaw = ws.Cells[row, colMap["EffectiveDate"]].Text?.Trim() ?? string.Empty;
            var premiumRaw = ws.Cells[row, colMap["Premium"]].Text?.Trim() ?? string.Empty;
            var payoutRaw = ws.Cells[row, colMap["Payout"]].Text?.Trim() ?? string.Empty;

            if (!DateTime.TryParse(purchaseDateRaw, out var purchaseDate))
            {
                errors.Add(new RegSaverUploadRowError
                {
                    Row = row,
                    PolicyNumber = policyNumber,
                    Reason = $"PurchaseDate '{purchaseDateRaw}' could not be parsed."
                });
                continue;
            }

            if (!DateTime.TryParse(effectiveDateRaw, out var effectiveDate))
            {
                errors.Add(new RegSaverUploadRowError
                {
                    Row = row,
                    PolicyNumber = policyNumber,
                    Reason = $"EffectiveDate '{effectiveDateRaw}' could not be parsed."
                });
                continue;
            }

            if (!TryParseMoney(premiumRaw, out var premium))
            {
                errors.Add(new RegSaverUploadRowError
                {
                    Row = row,
                    PolicyNumber = policyNumber,
                    Reason = $"Premium '{premiumRaw}' could not be parsed as a money value."
                });
                continue;
            }

            if (!TryParseMoney(payoutRaw, out var payout))
            {
                errors.Add(new RegSaverUploadRowError
                {
                    Row = row,
                    PolicyNumber = policyNumber,
                    Reason = $"Payout '{payoutRaw}' could not be parsed as a money value."
                });
                continue;
            }

            parsed.Add(new ParsedRow(row, policyNumber, purchaseDate, effectiveDate, premium, payout));
        }

        if (parsed.Count == 0)
        {
            return new RegSaverUploadResultDto
            {
                TotalRows = 0,
                ImportedCount = 0,
                DuplicateCount = 0,
                ErrorCount = errors.Count,
                Errors = errors
            };
        }

        // Dedupe on PolicyNumber against existing rows AND against duplicates within the file itself.
        var existing = await _repo.GetExistingPolicyNumbersAsync(
            parsed.Select(p => p.PolicyNumber),
            cancellationToken);

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toInsert = new List<VerticalInsurePayouts>();
        var duplicateCount = 0;

        foreach (var row in parsed)
        {
            if (existing.Contains(row.PolicyNumber) || !seenInFile.Add(row.PolicyNumber))
            {
                duplicateCount++;
                continue;
            }

            var purchaseUtc = row.PurchaseDate.ToUniversalTime();
            toInsert.Add(new VerticalInsurePayouts
            {
                PolicyNumber = row.PolicyNumber,
                PurchaseDate = purchaseUtc,
                PolicyEffectiveDate = row.EffectiveDate,
                NetWrittenPremium = row.Premium,
                Payout = row.Payout,
                Year = purchaseUtc.Year,
                Month = purchaseUtc.Month,
            });
        }

        if (toInsert.Count > 0)
        {
            await _repo.AddRangeAsync(toInsert, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
        }

        return new RegSaverUploadResultDto
        {
            TotalRows = parsed.Count,
            ImportedCount = toInsert.Count,
            DuplicateCount = duplicateCount,
            ErrorCount = errors.Count,
            Errors = errors
        };
    }

    private static RegSaverUploadResultDto Empty(List<RegSaverUploadRowError> errors) => new()
    {
        TotalRows = 0,
        ImportedCount = 0,
        DuplicateCount = 0,
        ErrorCount = errors.Count,
        Errors = errors
    };

    private static Dictionary<string, int> ResolveColumnMap(ExcelWorksheet ws)
    {
        var map = RequiredHeaders.ToDictionary(h => h, _ => 0, StringComparer.OrdinalIgnoreCase);
        var colCount = ws.Dimension?.Columns ?? 0;

        for (var col = 1; col <= colCount; col++)
        {
            var header = ws.Cells[1, col].Text?.Trim() ?? string.Empty;
            if (map.ContainsKey(header)) map[header] = col;
        }
        return map;
    }

    private static bool TryParseMoney(string raw, out decimal value)
    {
        // Strip currency symbol + thousands separator before parsing — RegSaver
        // exports sometimes contain "$1,234.56".
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, out value);
    }

    private sealed record ParsedRow(
        int Row,
        string PolicyNumber,
        DateTime PurchaseDate,
        DateTime EffectiveDate,
        decimal Premium,
        decimal Payout);
}
