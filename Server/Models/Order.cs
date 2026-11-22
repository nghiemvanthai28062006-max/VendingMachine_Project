namespace Server.Models;

public class Order
{
    public int Id { get; set; }
    public string RequestId { get; set; } = "";
    public int MachineId { get; set; }
    public int TrayId { get; set; }
    public int ProductId { get; set; }
    public int Amount { get; set; }
    public string Status { get; set; } = "";
    public string? DispenseRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? DispenseStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string MachineCode { get; set; } = "";   // lấy từ JOIN
    public string TrayCode { get; set; } = "";      // lấy từ JOIN
}

// Các dòng truy vấn nhỏ dùng trong transaction
public class MachineLite { public int Id { get; set; } public string Code { get; set; } = ""; public string Status { get; set; } = ""; }
public class TrayLock
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string Code { get; set; } = "";
    public int? ProductId { get; set; }
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; }
}
public class ProductLite { public int Id { get; set; } public string Name { get; set; } = ""; public int Price { get; set; } public bool IsActive { get; set; } }
public class PaymentRow { public int Id { get; set; } public int OrderId { get; set; } public string? TransactionCode { get; set; } public string Status { get; set; } = ""; }
public class StaleOrder
{
    public int OrderId { get; set; }
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public string Status { get; set; } = "";
    public int Amount { get; set; }
}

// Dạng trả về cho REST
public class OrderListItem
{
    public int Id { get; set; }
    public string MachineCode { get; set; } = "";
    public string TrayCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Amount { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
public class PaymentInfo { public string Status { get; set; } = ""; public string Method { get; set; } = ""; public string? TransactionCode { get; set; } public int Amount { get; set; } }
public class RefundInfo { public string Status { get; set; } = ""; public int Amount { get; set; } public string Reason { get; set; } = ""; }
public class OrderDetail : OrderListItem
{
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DispenseStartedAt { get; set; }
    public PaymentInfo? Payment { get; set; }
    public RefundInfo? Refund { get; set; }
}
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
}
public class OrderFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Status { get; set; }
    public int? MachineId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}