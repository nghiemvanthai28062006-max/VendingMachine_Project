using Dapper;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public interface IDeviceRepository
{
    Task<int> SetHeartbeatAsync(string machineCode, DateTime? at);
    Task<IEnumerable<int>> GetAllMachineIdsAsync();
    Task<IEnumerable<TrayState>> GetTrayStatesAsync(int machineId, DateTime now);
}

public class DeviceRepository : IDeviceRepository
{
    private readonly IDbConnectionFactory _db;

    public DeviceRepository(IDbConnectionFactory db) => _db = db;

    // Trả về số dòng khớp: 0 nghĩa là không có máy mang mã đó
    public async Task<int> SetHeartbeatAsync(string machineCode, DateTime? at)
    {
        await using var conn = _db.Create();
        return await conn.ExecuteAsync(
            "UPDATE machines SET last_heartbeat = @at WHERE code = @machineCode",
            new { machineCode, at });
    }

    public async Task<IEnumerable<int>> GetAllMachineIdsAsync()
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<int>("SELECT id FROM machines ORDER BY id");
    }

    public async Task<IEnumerable<TrayState>> GetTrayStatesAsync(int machineId, DateTime now)
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<TrayState>(
            @"SELECT t.code AS tray_code, t.quantity,
                     p.name AS product_name, p.volume_ml, p.price, p.image_url,
                     p.is_active AS product_active,
                     (SELECT COUNT(*) FROM orders o
                       WHERE o.tray_id = t.id
                         AND (o.status IN ('PAID','DISPENSING')
                              OR (o.status = 'PENDING_PAYMENT' AND o.expires_at > @now))) AS holds,
                     (SELECT COUNT(*) FROM alerts a
                       WHERE a.tray_id = t.id AND a.type = 'JAMMED' AND a.is_resolved = 0) AS jam_count
                FROM trays t
                LEFT JOIN products p ON p.id = t.product_id
               WHERE t.machine_id = @machineId
               ORDER BY t.code",
            new { machineId, now });
    }
}