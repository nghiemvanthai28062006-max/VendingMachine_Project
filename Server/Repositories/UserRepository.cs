using Dapper;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
}

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<User>(
            @"SELECT id, username, password_hash, full_name, role, is_active
                FROM users WHERE username = @username",
            new { username });
    }
}
