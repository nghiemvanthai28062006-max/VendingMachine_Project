using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/trays")]
[Authorize(Roles = "ADMIN")]
public class TraysController : ControllerBase
{
    private readonly MachineService _service;

    public TraysController(MachineService service) => _service = service;

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTrayRequest req) =>
        this.ToActionResult(await _service.UpdateTrayAsync(id, req));
}