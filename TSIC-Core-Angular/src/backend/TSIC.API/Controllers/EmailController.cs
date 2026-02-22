using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/email")]
[AllowAnonymous]
public class EmailController : ControllerBase
{
    private readonly IRegistrationRepository _registrationRepo;

    public EmailController(IRegistrationRepository registrationRepo)
    {
        _registrationRepo = registrationRepo;
    }

    [HttpGet("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] Guid regId, CancellationToken ct)
    {
        if (regId == Guid.Empty)
            return Content(BuildHtml("Invalid Request", "The unsubscribe link is invalid."), "text/html");

        await _registrationRepo.SetEmailOptOutAsync(regId, true, ct);

        return Content(BuildHtml(
            "Unsubscribed",
            "You have been successfully unsubscribed from emails for this league. You will no longer receive batch emails from this organization."
        ), "text/html");
    }

    private static string BuildHtml(string title, string message) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}} - TeamSportsInfo</title>
            <style>
                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; background: #f5f5f4; color: #1c1917; }
                .card { background: #fff; border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.07); padding: 48px; max-width: 480px; text-align: center; }
                h1 { font-size: 24px; margin: 0 0 16px; }
                p { font-size: 16px; line-height: 1.6; color: #57534e; margin: 0; }
            </style>
        </head>
        <body>
            <div class="card">
                <h1>{{title}}</h1>
                <p>{{message}}</p>
            </div>
        </body>
        </html>
        """;
}
