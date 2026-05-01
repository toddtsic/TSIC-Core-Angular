using TSIC.Contracts.Dtos.RegSaverUpload;

namespace TSIC.Contracts.Services;

public interface IRegSaverUploadService
{
    /// <summary>
    /// Parse an uploaded RegSaver monthly payouts spreadsheet (.xlsx) and insert any
    /// new policies into Vertical-Insure-Payouts. Existing PolicyNumbers are skipped.
    /// Partial success: valid rows are applied, invalid rows are reported individually.
    /// </summary>
    Task<RegSaverUploadResultDto> ProcessUploadAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default);
}
