using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "ADMIN,STAFF")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _service;

    public OrdersController(OrderService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? status, [FromQuery] int? machineId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await _service.SearchAsync(new OrderFilter
        {
            From = from,
            To = to,
            Status = status,
            MachineId = machineId,
            Page = page,
            PageSize = pageSize
        }));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id) =>
        this.ToActionResult(await _service.GetDetailAsync(id));
}