using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class LoginRequest
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    public string Password { get; set; } = "";
}

public record UserInfo(int Id, string Username, string FullName, string Role);

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserInfo User);