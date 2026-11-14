using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request.Username, request.Password);
        if (result.Success) return Ok(result.Value);

        var status = result.ErrorCode switch
        {
            "INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "ACCOUNT_DISABLED" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, new { error = new { code = result.ErrorCode, message = result.ErrorMessage } });
    }

    
}