using Server.Models;

namespace Server.Services;

public static class TicketStatus
{
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Done = "DONE";
    public const string Cancelled = "CANCELLED";

    public static readonly HashSet<string> All = new() { Open, InProgress, Done, Cancelled };
}

public static class RestockRules
{
    // BR-13: khay có sản phẩm, còn ít (bằng hoặc dưới ngưỡng) và còn chỗ để châm
    public static bool NeedsRestock(bool hasProduct, int quantity, int capacity, int threshold) =>
        hasProduct && quantity <= threshold && capacity - quantity > 0;

    public static int PlannedQty(int capacity, int quantity) => capacity - quantity;

    public static bool CanStart(string status) => status == TicketStatus.Open;
    public static bool CanConfirm(string status) => status is TicketStatus.Open or TicketStatus.InProgress;
    public static bool CanCancel(string status) => status is TicketStatus.Open or TicketStatus.InProgress;

    public static bool WouldExceedCapacity(int quantity, int add, int capacity) => quantity + add > capacity;

    // BR-15: nhiều khay hết hàng trước, rồi đến nhiều khay sắp hết, rồi theo mã máy
    public static List<RestockSuggestion> BuildSuggestions(IEnumerable<RestockCandidate> rows) =>
        rows.GroupBy(r => new { r.MachineId, r.MachineCode })
            .Select(g => new RestockSuggestion
            {
                MachineId = g.Key.MachineId,
                MachineCode = g.Key.MachineCode,
                OutOfStockTrays = g.Count(r => r.Quantity == 0),
                LowStockTrays = g.Count(r => r.Quantity > 0),
                Trays = g.OrderBy(r => r.TrayCode, StringComparer.Ordinal)
                    .Select(r => new RestockSuggestionTray
                    {
                        TrayId = r.TrayId,
                        TrayCode = r.TrayCode,
                        ProductName = r.ProductName,
                        Quantity = r.Quantity,
                        Capacity = r.Capacity,
                        PlannedQty = PlannedQty(r.Capacity, r.Quantity)
                    }).ToList()
            })
            .OrderByDescending(s => s.OutOfStockTrays)
            .ThenByDescending(s => s.LowStockTrays)
            .ThenBy(s => s.MachineId)
            .ToList();
}