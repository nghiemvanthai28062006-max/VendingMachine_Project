using Dapper;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public class ReportRepository
{
    private readonly IDbConnectionFactory _db;

    public ReportRepository(IDbConnectionFactory db) => _db = db;

    // Doanh thu: chỉ đơn COMPLETED, theo ngày hoàn tất (completed_at)
    public async Task<List<RevenueRow>> RevenueAsync(string groupBy, DateTime fromDate, DateTime toExclusive)
    {
        // keyExpr lấy từ danh sách cố định (groupBy đã được kiểm tra), không chèn dữ liệu người dùng vào SQL
        var keyExpr = groupBy switch
        {
            "machine" => "m.code",
            "product" => "p.name",
            _ => "DATE_FORMAT(o.completed_at, '%Y-%m-%d')"
        };

        await using var conn = _db.Create();
        var rows = await conn.QueryAsync<RevenueRow>(
            $@"SELECT {keyExpr} AS `key`, COUNT(*) AS orders, CAST(SUM(o.amount) AS SIGNED) AS revenue
                 FROM orders o
                 JOIN machines m ON m.id = o.machine_id
                 JOIN products p ON p.id = o.product_id
                WHERE o.status = 'COMPLETED'
                  AND o.completed_at >= @fromDate AND o.completed_at < @toExclusive
                GROUP BY {keyExpr}
                ORDER BY {keyExpr}",
            new { fromDate, toExclusive });
        return rows.ToList();
    }

    public async Task<long> RefundedAsync(DateTime fromDate, DateTime toExclusive)
    {
        await using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<long>(
            @"SELECT COALESCE(CAST(SUM(amount) AS SIGNED), 0) FROM refunds
               WHERE status = 'COMPLETED'
                 AND completed_at >= @fromDate AND completed_at < @toExclusive",
            new { fromDate, toExclusive });
    }
}