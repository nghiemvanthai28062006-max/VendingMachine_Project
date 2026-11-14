using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthService> _log;

    public AuthService(IUserRepository users, IJwtTokenService jwt, ILogger<AuthService> log)
    {
        _users = users;
        _jwt = jwt;
        _log = log;
    }

    public async Task<Result<LoginResponse>> LoginAsync(string username, string password)
    {
        username = username.Trim();
        var user = await _users.GetByUsernameAsync(username);

        if (user is null || !PasswordMatches(password, user.PasswordHash))
        {
            // Không phân biệt "sai tên" hay "sai mật khẩu" để không lộ tài khoản nào tồn tại
            _log.LogInformation("Đăng nhập thất bại: {Username}", username);
            return Result<LoginResponse>.Fail("INVALID_CREDENTIALS", "Sai tên đăng nhập hoặc mật khẩu");
        }

        if (!user.IsActive)
        {
            _log.LogInformation("Tài khoản bị khóa cố đăng nhập: {Username}", username);
            return Result<LoginResponse>.Fail("ACCOUNT_DISABLED", "Tài khoản đã bị khóa");
        }

        var (token, expiresAt) = _jwt.Create(user);
        _log.LogInformation("Đăng nhập thành công: {Username} ({Role})", user.Username, user.Role);

        return Result<LoginResponse>.Ok(new LoginResponse(
            token, expiresAt, new UserInfo(user.Id, user.Username, user.FullName, user.Role)));
    }

    private bool PasswordMatches(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception ex)   // hash trong DB sai định dạng
        {
            _log.LogWarning(ex, "Không đọc được password_hash");
            return false;
        }
    }
}