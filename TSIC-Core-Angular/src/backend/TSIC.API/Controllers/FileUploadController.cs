using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// General-purpose file upload (headshots, documents, etc.).
/// </summary>
[ApiController]
[Authorize]
[Route("api/files")]
public class FileUploadController : ControllerBase
{
    private readonly IFileUploadService _fileUploadService;

    public FileUploadController(IFileUploadService fileUploadService)
    {
        _fileUploadService = fileUploadService;
    }

    /// <summary>Upload a file (multipart form data).</summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(FileUploadResponseDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { Error = "No file provided" });

        await using var stream = file.OpenReadStream();
        var fileUrl = await _fileUploadService.UploadFileAsync(stream, file.FileName, file.ContentType, ct);

        return Ok(new FileUploadResponseDto { FileUrl = fileUrl });
    }

    /// <summary>Delete a previously uploaded file.</summary>
    [HttpPost("delete")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete([FromBody] DeleteFileRequest request, CancellationToken ct)
    {
        var deleted = await _fileUploadService.DeleteFileAsync(request.FileUrl, ct);
        return deleted ? Ok() : NotFound();
    }
}

public record DeleteFileRequest
{
    public required string FileUrl { get; init; }
}
