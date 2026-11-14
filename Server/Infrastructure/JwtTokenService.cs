using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Server.Models;

namespace Server.Infrastructure;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) Create(User user);
}

public class JwtTokenService : IJwtTokenService
{
    public const string Issuer = "VendingServer";
    public const string Audience = "VendingApp";

    private readonly IConfiguration _config;
    private readonly IClock _clock;

    public JwtTokenService(IConfiguration config, IClock clock)
    {
        _config = config;
        _clock = clock;
    }

    public (string Token, DateTimeOffset ExpiresAt) Create(User user)
    {
        var secret = _config["Jwt:Secret"]!;
        var hours = _config.GetValue("Jwt:ExpiryHours", 8);

        var now = _clock.Now;
        var expires = now.AddHours(hours);

        // Dùng tên claim ngắn ("sub", "name", "role") và đọc lại đúng tên đó ở Program.cs
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("name", user.Username),
            new Claim("fullName", user.FullName),
            new Claim("role", user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.UtcDateTime,       // JWT bắt buộc dùng giờ UTC
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}