using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using TSIC.Contracts.Dtos.NuveiUpload;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Imports Nuvei monthly Funding and Batches CSV exports.
/// Mirrors legacy <c>UploadNuveiMonthlyExportsController</c>: full-row equality dedup,
/// CsvHelper with InvariantCulture, no schema-side change required.
/// </summary>
public class NuveiUploadService : INuveiUploadService
{
    private readonly INuveiFundingRepository _fundingRepo;
    private readonly INuveiBatchesRepository _batchesRepo;

    public NuveiUploadService(
        INuveiFundingRepository fundingRepo,
        INuveiBatchesRepository batchesRepo)
    {
        _fundingRepo = fundingRepo;
        _batchesRepo = batchesRepo;
    }

    public async Task<NuveiUploadResultDto> ProcessFundingAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<NuveiUploadRowError>();
        var parsed = new List<(int Row, NuveiFundingCsvRow Data, string Fingerprint)>();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
        });

        var rowNum = 1; // header
        try
        {
            await csv.ReadAsync();
            csv.ReadHeader();
        }
        catch (Exception ex)
        {
            return EmptyResult(new NuveiUploadRowError { Row = 0, Reason = $"Could not read CSV header: {ex.Message}" });
        }

        while (await csv.ReadAsync())
        {
            rowNum++;
            try
            {
                var record = csv.GetRecord<NuveiFundingCsvRow>();
                if (record == null) continue;

                var fingerprint = string.Join('|',
                    record.RefNumber,
                    record.FundingEvent,
                    record.FundingType ?? string.Empty,
                    record.FundingAmount,
                    record.FundingDate.ToString("o"));

                parsed.Add((rowNum, record, fingerprint));
            }
            catch (Exception ex)
            {
                errors.Add(new NuveiUploadRowError { Row = rowNum, Reason = ex.Message });
            }
        }

        if (parsed.Count == 0)
        {
            return new NuveiUploadResultDto
            {
                TotalRows = 0,
                ImportedCount = 0,
                DuplicateCount = 0,
                ErrorCount = errors.Count,
                Errors = errors,
            };
        }

        var existing = await _fundingRepo.GetExistingFingerprintsAsync(
            parsed.Select(p => p.Fingerprint),
            cancellationToken);

        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        var toInsert = new List<NuveiFunding>();
        var duplicateCount = 0;

        foreach (var (_, data, fingerprint) in parsed)
        {
            if (existing.Contains(fingerprint) || !seenInFile.Add(fingerprint))
            {
                duplicateCount++;
                continue;
            }

            toInsert.Add(new NuveiFunding
            {
                FundingEvent = data.FundingEvent,
                FundingType = data.FundingType,
                RefNumber = data.RefNumber,
                FundingAmount = data.FundingAmount,
                FundingDate = data.FundingDate,
            });
        }

        if (toInsert.Count > 0)
        {
            await _fundingRepo.AddRangeAsync(toInsert, cancellationToken);
            await _fundingRepo.SaveChangesAsync(cancellationToken);
        }

        return new NuveiUploadResultDto
        {
            TotalRows = parsed.Count,
            ImportedCount = toInsert.Count,
            DuplicateCount = duplicateCount,
            ErrorCount = errors.Count,
            Errors = errors,
        };
    }

    public async Task<NuveiUploadResultDto> ProcessBatchesAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<NuveiUploadRowError>();
        var parsed = new List<(int Row, NuveiBatchCsvRow Data, string Fingerprint)>();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
        });

        var rowNum = 1;
        try
        {
            await csv.ReadAsync();
            csv.ReadHeader();
        }
        catch (Exception ex)
        {
            return EmptyResult(new NuveiUploadRowError { Row = 0, Reason = $"Could not read CSV header: {ex.Message}" });
        }

        while (await csv.ReadAsync())
        {
            rowNum++;
            try
            {
                var record = csv.GetRecord<NuveiBatchCsvRow>();
                if (record == null) continue;

                var fingerprint = string.Join('|',
                    record.BatchId,
                    record.BatchCloseDate.ToString("o"),
                    record.BatchNet,
                    record.SaleAmt,
                    record.ReturnAmt.ToString());

                parsed.Add((rowNum, record, fingerprint));
            }
            catch (Exception ex)
            {
                errors.Add(new NuveiUploadRowError { Row = rowNum, Reason = ex.Message });
            }
        }

        if (parsed.Count == 0)
        {
            return new NuveiUploadResultDto
            {
                TotalRows = 0,
                ImportedCount = 0,
                DuplicateCount = 0,
                ErrorCount = errors.Count,
                Errors = errors,
            };
        }

        var existing = await _batchesRepo.GetExistingFingerprintsAsync(
            parsed.Select(p => p.Fingerprint),
            cancellationToken);

        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        var toInsert = new List<NuveiBatches>();
        var duplicateCount = 0;

        foreach (var (_, data, fingerprint) in parsed)
        {
            if (existing.Contains(fingerprint) || !seenInFile.Add(fingerprint))
            {
                duplicateCount++;
                continue;
            }

            toInsert.Add(new NuveiBatches
            {
                BatchCloseDate = data.BatchCloseDate,
                BatchId = data.BatchId,
                BatchNet = data.BatchNet,
                SaleAmt = data.SaleAmt,
                ReturnAmt = data.ReturnAmt,
            });
        }

        if (toInsert.Count > 0)
        {
            await _batchesRepo.AddRangeAsync(toInsert, cancellationToken);
            await _batchesRepo.SaveChangesAsync(cancellationToken);
        }

        return new NuveiUploadResultDto
        {
            TotalRows = parsed.Count,
            ImportedCount = toInsert.Count,
            DuplicateCount = duplicateCount,
            ErrorCount = errors.Count,
            Errors = errors,
        };
    }

    private static NuveiUploadResultDto EmptyResult(NuveiUploadRowError error) => new()
    {
        TotalRows = 0,
        ImportedCount = 0,
        DuplicateCount = 0,
        ErrorCount = 1,
        Errors = [error],
    };

    private sealed class NuveiFundingCsvRow
    {
        public string FundingEvent { get; set; } = string.Empty;
        public string? FundingType { get; set; }
        public string RefNumber { get; set; } = string.Empty;
        public decimal FundingAmount { get; set; }
        public DateTime FundingDate { get; set; }
    }

    private sealed class NuveiBatchCsvRow
    {
        public DateTime BatchCloseDate { get; set; }
        public int BatchId { get; set; }
        public decimal BatchNet { get; set; }
        public decimal SaleAmt { get; set; }
        public decimal ReturnAmt { get; set; }
    }
}
