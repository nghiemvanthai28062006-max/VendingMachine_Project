using Dapper;
using Microsoft.AspNetCore.Mvc;
using Server.Infrastructure;

namespace Server.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<HealthController> _log;

    public HealthController(IDbConnectionFactory db, ILogger<HealthController> log)
    {
        _db = db;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            await using var conn = _db.Create();
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            return Ok(new { status = "ok", database = "ok" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Health check: không kết nối được MySQL");
            return StatusCode(503, new { status = "error", database = "error" });
        }
    }
}