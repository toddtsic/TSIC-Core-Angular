using Microsoft.AspNetCore.Mvc;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;
    private readonly SqlDbContext _dbContext;

    public TestController(ILogger<TestController> logger, SqlDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpGet]
    public ActionResult<string> Get()
    {
        _logger.LogInformation("Test endpoint called successfully");
        return Ok("api returning successfully from test");
    }

    [HttpGet("health")]
    public ActionResult<object> GetHealth()
    {
        try
        {
            var roles = _dbContext.AspNetRoles
                .Select(r => new { r.Id, r.Name })
                .ToList();
            return Ok(new
            {
                status = "healthy",
                message = "TSIC API is running",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                roles
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                status = "warning",
                message = "TSIC API is running but database connection failed",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                error = ex.Message
            });
        }
    }
}
