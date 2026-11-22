using Dapper;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public class AlertRepository
{
    private const string Columns =
        "id, machine_id, tray_id, type, message, is_resolved, created_at, resolved_at, resolved_by";

    private readonly IDbConnectionFactory _db;

    public AlertRepository(IDbConnectionFactory db) => _db = db;

    public async Task<List<AlertView>> SearchAsync(AlertFilter f)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (f.Resolved != null) { where.Add("is_resolved = @resolved"); p.Add("resolved", f.Resolved.Value); }
        if (f.MachineId != null) { where.Add("machine_id = @machineId"); p.Add("machineId", f.MachineId); }
        if (!string.IsNullOrEmpty(f.Type)) { where.Add("type = @type"); p.Add("type", f.Type); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        await using var conn = _db.Create();
        var rows = await conn.QueryAsync<AlertView>(
            $"SELECT {Columns} FROM alerts {whereSql} ORDER BY created_at DESC, id DESC", p);
        return rows.ToList();
    }

    public async Task<AlertView?> GetByIdAsync(int id)
    {
        await using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<AlertView>(
            $"SELECT {Columns} FROM alerts WHERE id = @id", new { id });
    }

    // Trả số dòng bị tác động: 0 nghĩa là cảnh báo đã được xử lý trước đó
    public async Task<int> ResolveAsync(int id, int userId, DateTimeOffset now)
    {
        await using var conn = _db.Create();
        return await conn.ExecuteAsync(
            @"UPDATE alerts SET is_resolved = 1, resolved_at = @now, resolved_by = @userId
               WHERE id = @id AND is_resolved = 0",
            new { id, userId, now });
    }
}