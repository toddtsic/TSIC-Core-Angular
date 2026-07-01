using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Admin diagnostics for email-delivery complaints. Two capabilities:
///  - suppression list lookup/removal (SES v2)
///  - "investigate" = check suppression + forced test send + which-side conclusion.
/// Cross-job admin tool: no :jobPath in the route (mirrors AdministratorsController/MenuAdminController).
/// </summary>
[ApiController]
[Route("api/email-troubleshooter")]
[Authorize(Policy = "AdminOnly")]
public class EmailTroubleshooterController : ControllerBase
{
    private readonly IEmailTroubleshooterService _service;

    public EmailTroubleshooterController(IEmailTroubleshooterService service)
    {
        _service = service;
    }

    [HttpPost("suppression/check")]
    [ProducesResponseType(typeof(IReadOnlyList<SuppressionEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SuppressionEntryDto>>> CheckSuppression(
        [FromBody] EmailListRequest request,
        CancellationToken cancellationToken)
    {
        var results = await _service.CheckSuppressionAsync(request.Emails, cancellationToken);
        return Ok(results);
    }

    [HttpPost("suppression/remove")]
    [ProducesResponseType(typeof(IReadOnlyList<SuppressionRemoveResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SuppressionRemoveResultDto>>> RemoveSuppression(
        [FromBody] EmailListRequest request,
        CancellationToken cancellationToken)
    {
        var results = await _service.RemoveSuppressionAsync(request.Emails, cancellationToken);
        return Ok(results);
    }

    [HttpPost("investigate")]
    [ProducesResponseType(typeof(IReadOnlyList<EmailInvestigateResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmailInvestigateResultDto>>> Investigate(
        [FromBody] EmailListRequest request,
        CancellationToken cancellationToken)
    {
        var results = await _service.InvestigateAsync(request.Emails, cancellationToken);
        return Ok(results);
    }
}
