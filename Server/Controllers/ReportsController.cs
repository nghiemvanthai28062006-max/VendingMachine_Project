using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "ADMIN")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _service;

    public ReportsController(ReportService service) => _service = service;

    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? groupBy) =>
        this.ToActionResult(await _service.RevenueAsync(from, to, groupBy));
}