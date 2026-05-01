using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.RegSaverUpload;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/regsaver-upload")]
[Authorize(Roles = "Superuser")]
public class RegSaverUploadController : ControllerBase
{
    private readonly IRegSaverUploadService _service;

    public RegSaverUploadController(IRegSaverUploadService service)
    {
        _service = service;
    }

    /// <summary>
    /// POST /api/regsaver-upload/upload — Upload a RegSaver monthly payouts .xlsx
    /// export. Inserts new policies into Vertical-Insure-Payouts; existing
    /// PolicyNumbers are skipped (counted as duplicates).
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<RegSaverUploadResultDto>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest(new { message = "File is empty." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are accepted." });

        using var stream = file.OpenReadStream();
        var result = await _service.ProcessUploadAsync(stream, cancellationToken);

        return Ok(result);
    }
}
