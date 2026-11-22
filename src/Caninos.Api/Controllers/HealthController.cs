using Caninos.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var canConnect = await _db.Database.CanConnectAsync();
        if (!canConnect)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "degraded",
                database = "sqlite",
                timestamp = DateTime.UtcNow
            });
        }

        return Ok(new
        {
            status = "ok",
            database = "sqlite",
            timestamp = DateTime.UtcNow
        });
    }
}
