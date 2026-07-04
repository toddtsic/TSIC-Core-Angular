using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Context-sensitive help content. Fragments live as HTML under
/// <c>App_Data/Help/{component}/{topic}.html</c> — git-tracked, deployed with the app.
///
/// Reads are anonymous: the "?" appears app-wide, including public / pre-login pages like the
/// registration wizard. Writes are SuperUser AND sandbox only (Development + Staging): the working-tree
/// file is the single source of truth — authored on staging, committed, deployed — never written on
/// live production (Model A). Both routes validate the key segments against path traversal.
/// </summary>
[ApiController]
[Route("api/help")]
public class HelpController : ControllerBase
{
    private readonly IHelpContentService _service;

    public HelpController(IHelpContentService service) => _service = service;

    /// <summary>
    /// GET /api/help/manifest — anonymous. The set of keys that actually have content, so the "?"
    /// launcher can hide itself where there's nothing to show.
    /// </summary>
    [HttpGet("manifest")]
    [AllowAnonymous]
    public ActionResult<HelpManifestDto> Manifest() => Ok(_service.GetManifest());

    /// <summary>
    /// GET /api/help/{component}/{topic} — anonymous. Returns the fragment, or Exists=false
    /// ("Under Development") when nothing is authored for the key yet.
    /// </summary>
    [HttpGet("{component}/{topic}")]
    [AllowAnonymous]
    public async Task<ActionResult<HelpContentDto>> Get(string component, string topic, CancellationToken ct)
    {
        if (!_service.IsValidSegment(component) || !_service.IsValidSegment(topic))
            return BadRequest(new { message = "Invalid help key." });

        return Ok(await _service.GetAsync(component, topic, ct));
    }

    /// <summary>
    /// PUT /api/help/{component}/{topic} — SuperUser + sandbox only. Writes the working-tree HTML
    /// fragment. Returns 404 on production, where help is read-only.
    /// </summary>
    [HttpPut("{component}/{topic}")]
    [Authorize(Roles = "Superuser")]
    public async Task<ActionResult<HelpContentDto>> Save(
        string component,
        string topic,
        [FromBody] SaveHelpContentRequest request,
        CancellationToken ct)
    {
        if (!_service.CanEdit)
            return NotFound();

        if (!_service.IsValidSegment(component) || !_service.IsValidSegment(topic))
            return BadRequest(new { message = "Invalid help key." });

        return Ok(await _service.SaveAsync(component, topic, request.Html, ct));
    }
}
