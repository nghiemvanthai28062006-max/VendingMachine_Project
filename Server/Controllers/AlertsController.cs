using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize(Roles = "ADMIN,STAFF")]
public class AlertsController : ControllerBase
{
    private readonly AlertService _service;

    public AlertsController(AlertService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool? resolved, [FromQuery] int? machineId,
        [FromQuery] string? type) =>
        this.ToActionResult(await _service.GetAsync(
            new AlertFilter { Resolved = resolved, MachineId = machineId, Type = type }));

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id)
    {
        // JWT lưu id người dùng ở claim "sub"
        if (!int.TryParse(User.FindFirst("sub")?.Value, out var userId))
            return Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Token không hợp lệ" } });

        return this.ToActionResult(await _service.ResolveAsync(id, userId));
    }
}
