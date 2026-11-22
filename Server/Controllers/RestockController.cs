using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN,STAFF")]
public class RestockController : ControllerBase
{
    private readonly RestockService _service;

    public RestockController(RestockService service) => _service = service;

    // JWT lưu id người dùng ở claim "sub", vai trò ở claim "role"
    private bool TryGetUser(out int userId, out string role)
    {
        role = User.FindFirst("role")?.Value ?? "";
        return int.TryParse(User.FindFirst("sub")?.Value, out userId);
    }

    private IActionResult BadToken() =>
        Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Token không hợp lệ" } });

    [HttpGet("/api/restock/suggestions")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Suggestions() => Ok(await _service.GetSuggestionsAsync());

    [HttpGet("/api/staff")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Staff() => Ok(await _service.GetStaffAsync());

    [HttpPost("/api/restock-tickets")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest req) =>
        this.ToActionResult(await _service.CreateAsync(req), 201);

    [HttpGet("/api/restock-tickets")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? machineId)
    {
        if (!TryGetUser(out var userId, out var role)) return BadToken();
        return this.ToActionResult(await _service.ListAsync(userId, role, status, machineId));
    }

    [HttpGet("/api/restock-tickets/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetUser(out var userId, out var role)) return BadToken();
        return this.ToActionResult(await _service.GetDetailAsync(id, userId, role));
    }

    [HttpPost("/api/restock-tickets/{id:int}/start")]
    public async Task<IActionResult> Start(int id)
    {
        if (!TryGetUser(out var userId, out var role)) return BadToken();
        return this.ToActionResult(await _service.StartAsync(id, userId, role));
    }

    [HttpPost("/api/restock-tickets/{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmTicketRequest req)
    {
        if (!TryGetUser(out var userId, out var role)) return BadToken();
        return this.ToActionResult(await _service.ConfirmAsync(id, req, userId, role));
    }

    [HttpPost("/api/restock-tickets/{id:int}/cancel")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Cancel(int id) =>
        this.ToActionResult(await _service.CancelAsync(id));
}