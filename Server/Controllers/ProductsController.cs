using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Roles = "ADMIN,STAFF")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _service;

    public ProductsController(ProductService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Create([FromBody] ProductRequest req)
    {
        var result = await _service.CreateAsync(req);
        return result.Success ? StatusCode(201, result.Value) : Fail(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductRequest req)
    {
        var result = await _service.UpdateAsync(id, req);
        return result.Success ? Ok(result.Value) : Fail(result);
    }

    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> SetActive(int id, [FromBody] ActiveRequest req)
    {
        var result = await _service.SetActiveAsync(id, req.IsActive!.Value);
        return result.Success ? Ok(result.Value) : Fail(result);
    }

    private IActionResult Fail(Result<Product> r) => StatusCode(
        r.ErrorCode == "PRODUCT_NOT_FOUND" ? 404 : 400,
        new { error = new { code = r.ErrorCode, message = r.ErrorMessage } });
}