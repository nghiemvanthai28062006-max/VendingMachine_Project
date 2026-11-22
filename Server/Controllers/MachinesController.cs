using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/machines")]
[Authorize(Roles = "ADMIN,STAFF")]
public class MachinesController : ControllerBase
{
    private readonly MachineService _service;

    public MachinesController(MachineService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Create([FromBody] CreateMachineRequest req) =>
        this.ToActionResult(await _service.CreateAsync(req), 201);

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMachineRequest req) =>
        this.ToActionResult(await _service.UpdateAsync(id, req));

    [HttpGet("{id:int}/trays")]
    public async Task<IActionResult> GetTrays(int id) =>
        this.ToActionResult(await _service.GetTraysAsync(id));

    [HttpPost("{id:int}/trays")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CreateTray(int id, [FromBody] CreateTrayRequest req) =>
        this.ToActionResult(await _service.CreateTrayAsync(id, req), 201);
}