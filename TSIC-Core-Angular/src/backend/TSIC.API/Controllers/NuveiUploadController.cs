using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.NuveiUpload;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/nuvei-upload")]
[Authorize(Roles = "Superuser")]
public class NuveiUploadController : ControllerBase
{
    private readonly INuveiUploadService _service;

    public NuveiUploadController(INuveiUploadService service)
    {
        _service = service;
    }

    /// <summary>
    /// POST /api/nuvei-upload/upload-funding — Upload a Nuvei monthly Funding CSV.
    /// Inserts new rows into adn.NuveiFunding; rows whose 5-column composite already
    /// exists are skipped (duplicates).
    /// </summary>
    [HttpPost("upload-funding")]
    public async Task<ActionResult<NuveiUploadResultDto>> UploadFunding(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCsv(file);
        if (validation != null) return validation;

        using var stream = file.OpenReadStream();
        var result = await _service.ProcessFundingAsync(stream, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/nuvei-upload/upload-batches — Upload a Nuvei monthly Batches CSV.
    /// Inserts new rows into adn.NuveiBatches; rows whose 5-column composite already
    /// exists are skipped (duplicates).
    /// </summary>
    [HttpPost("upload-batches")]
    public async Task<ActionResult<NuveiUploadResultDto>> UploadBatches(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCsv(file);
        if (validation != null) return validation;

        using var stream = file.OpenReadStream();
        var result = await _service.ProcessBatchesAsync(stream, cancellationToken);
        return Ok(result);
    }

    private static BadRequestObjectResult? ValidateCsv(IFormFile file)
    {
        if (file.Length == 0)
            return new BadRequestObjectResult(new { message = "File is empty." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".csv")
            return new BadRequestObjectResult(new { message = "Only .csv files are accepted." });

        return null;
    }
}
