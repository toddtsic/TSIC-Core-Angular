using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.UniformUpload;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/uniform-upload")]
[Authorize(Policy = "AdminOnly")]
public class UniformUploadController : ControllerBase
{
    private readonly IUniformUploadService _uniformUploadService;
    private readonly IJobLookupService _jobLookupService;

    public UniformUploadController(
        IUniformUploadService uniformUploadService,
        IJobLookupService jobLookupService)
    {
        _uniformUploadService = uniformUploadService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/uniform-upload/template — Download Excel template pre-populated with the job's player roster.
    /// </summary>
    [HttpGet("template")]
    public async Task<ActionResult> DownloadTemplate(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var bytes = await _uniformUploadService.GenerateTemplateAsync(jobId.Value, ct);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "uniform-numbers-template.xlsx");
    }

    /// <summary>
    /// POST /api/uniform-upload/upload — Upload an Excel file to bulk-update uniform numbers and day groups.
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<UniformUploadResultDto>> Upload(IFormFile file, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        if (file.Length == 0)
            return BadRequest(new { message = "File is empty." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are accepted." });

        using var stream = file.OpenReadStream();
        var result = await _uniformUploadService.ProcessUploadAsync(jobId.Value, stream, ct);

        return Ok(result);
    }
}
