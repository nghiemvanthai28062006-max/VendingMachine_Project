using Dapper;
using MySqlConnector;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public interface ITrayRepository
{
    Task<IEnumerable<Tray>> GetByMachineAsync(int machineId);
    Task<Tray?> GetByIdAsync(int id);
    Task<int?> InsertAsync(int machineId, CreateTrayRequest req);   // null = trùng mã khay trong máy
    Task UpdateAsync(int id, UpdateTrayRequest req);
}

public class TrayRepository : ITrayRepository
{
    private const string Select = @"
        SELECT t.id, t.machine_id, t.code, t.product_id, p.name AS product_name,
               t.quantity, t.capacity, t.low_stock_threshold
          FROM trays t
          LEFT JOIN products p ON p.id = t.product_id";

    private readonly IDbConnectionFactory _db;

    public TrayRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Tray>> GetByMachineAsync(int machineId)
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<Tray>(
            $"{Select} WHERE t.machine_id = @machineId ORDER BY t.code", new { machineId });
    }

    public async Task<Tray?> GetByIdAsync(int id)
    {
        await using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<Tray>(
            $"{Select} WHERE t.id = @id", new { id });
    }

    public async Task<int?> InsertAsync(int machineId, CreateTrayRequest req)
    {
        await using var conn = _db.Create();
        try
        {
            return await conn.ExecuteScalarAsync<int>(
                @"INSERT INTO trays (machine_id, code, product_id, quantity, capacity, low_stock_threshold)
                  VALUES (@machineId, @Code, @ProductId, @Quantity, @Capacity, @LowStockThreshold);
                  SELECT LAST_INSERT_ID();",
                new { machineId, req.Code, req.ProductId, req.Quantity, req.Capacity, req.LowStockThreshold });
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return null;
        }
    }

    public async Task UpdateAsync(int id, UpdateTrayRequest req)
    {
        await using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"UPDATE trays SET product_id = @ProductId, capacity = @Capacity,
                               low_stock_threshold = @LowStockThreshold
              WHERE id = @id",
            new { id, req.ProductId, req.Capacity, req.LowStockThreshold });
    }
}