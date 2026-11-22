using Dapper;
using MySqlConnector;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public interface IMachineRepository
{
    Task<IEnumerable<Machine>> GetAllAsync();
    Task<Machine?> GetByIdAsync(int id);
    Task<int?> InsertAsync(CreateMachineRequest req, DateTimeOffset now);   // null = trùng mã máy
    Task UpdateAsync(int id, UpdateMachineRequest req);
}

public class MachineRepository : IMachineRepository
{
    private const string Columns = "id, code, name, location, status, last_heartbeat, created_at";
    private readonly IDbConnectionFactory _db;

    public MachineRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Machine>> GetAllAsync()
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<Machine>($"SELECT {Columns} FROM machines ORDER BY id");
    }

    public async Task<Machine?> GetByIdAsync(int id)
    {
        await using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<Machine>(
            $"SELECT {Columns} FROM machines WHERE id = @id", new { id });
    }

    public async Task<int?> InsertAsync(CreateMachineRequest req, DateTimeOffset now)
    {
        await using var conn = _db.Create();
        try
        {
            return await conn.ExecuteScalarAsync<int>(
                @"INSERT INTO machines (code, name, location, status, created_at)
                  VALUES (@Code, @Name, @Location, 'ACTIVE', @now);
                  SELECT LAST_INSERT_ID();",
                new { req.Code, req.Name, req.Location, now });
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return null;
        }
    }

    public async Task UpdateAsync(int id, UpdateMachineRequest req)
    {
        await using var conn = _db.Create();
        await conn.ExecuteAsync(
            "UPDATE machines SET name = @Name, location = @Location, status = @Status WHERE id = @id",
            new { id, req.Name, req.Location, req.Status });
    }
}