using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Email;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/health/email")]
public class EmailHealthController : ControllerBase
{
    private readonly IEmailHealthService _health;

    public EmailHealthController(IEmailHealthService health)
    {
        _health = health;
    }

    [HttpGet]
    public async Task<ActionResult<EmailHealthStatus>> Get(CancellationToken ct)
    {
        var status = await _health.CheckAsync(ct);
        return Ok(status);
    }
}
