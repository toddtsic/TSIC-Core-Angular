using Microsoft.AspNetCore.Mvc;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController(ILogger<TestController> logger) : ControllerBase
{
    private readonly ILogger<TestController> _logger = logger;

    [HttpGet]
    public ActionResult<string> Get()
    {
        _logger.LogInformation("Test endpoint called successfully");
        return Ok("api returning successfully from test");
    }

    [HttpGet("health")]
    public ActionResult<object> GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            message = "TSIC API is running",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }
}
