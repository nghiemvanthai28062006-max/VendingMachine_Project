using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class ReportService
{
    private readonly ReportRepository _repo;
    private readonly OrderService _orders;
    private readonly IClock _clock;

    public ReportService(ReportRepository repo, OrderService orders, IClock clock)
    {
        _repo = repo;
        _orders = orders;
        _clock = clock;
    }

    public async Task<Result<RevenueReport>> RevenueAsync(DateTime? from, DateTime? to, string? groupBy)
    {
        groupBy = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.Trim().ToLowerInvariant();
        if (groupBy is not ("day" or "machine" or "product"))
            return Result<RevenueReport>.Fail("VALIDATION_ERROR", "groupBy chỉ nhận day, machine hoặc product");

        // Mặc định: 7 ngày gần nhất, tính cả hôm nay
        var toDate = (to ?? _clock.Now.Date).Date;
        var fromDate = (from ?? toDate.AddDays(-6)).Date;
        if (fromDate > toDate)
            return Result<RevenueReport>.Fail("VALIDATION_ERROR", "Ngày bắt đầu lớn hơn ngày kết thúc");
        if ((toDate - fromDate).TotalDays > 366)
            return Result<RevenueReport>.Fail("VALIDATION_ERROR", "Khoảng ngày tối đa 366 ngày");

        await _orders.SweepStaleOrdersAsync();   // BR-20: dọn đơn quá hạn trước khi tính

        var toExclusive = toDate.AddDays(1);
        var rows = await _repo.RevenueAsync(groupBy, fromDate, toExclusive);
        var refunded = await _repo.RefundedAsync(fromDate, toExclusive);

        return Result<RevenueReport>.Ok(new RevenueReport
        {
            From = fromDate.ToString("yyyy-MM-dd"),
            To = toDate.ToString("yyyy-MM-dd"),
            GroupBy = groupBy,
            TotalRevenue = rows.Sum(r => r.Revenue),
            TotalOrders = rows.Sum(r => r.Orders),
            RefundedAmount = refunded,
            Rows = rows
        });
    }
}