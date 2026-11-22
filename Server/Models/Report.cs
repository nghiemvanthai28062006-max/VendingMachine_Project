namespace Server.Models;

public class RevenueRow
{
    public string Key { get; set; } = "";
    public long Orders { get; set; }
    public long Revenue { get; set; }
}

public class RevenueReport
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string GroupBy { get; set; } = "";
    public long TotalRevenue { get; set; }
    public long TotalOrders { get; set; }
    public long RefundedAmount { get; set; }
    public List<RevenueRow> Rows { get; set; } = new();
}