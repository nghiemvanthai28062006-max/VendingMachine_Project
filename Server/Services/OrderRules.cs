namespace Server.Services;

public static class OrderStatus
{
    public const string PendingPayment = "PENDING_PAYMENT";
    public const string Paid = "PAID";
    public const string Dispensing = "DISPENSING";
    public const string Completed = "COMPLETED";
    public const string Expired = "EXPIRED";
    public const string PaymentFailed = "PAYMENT_FAILED";
    public const string DispenseFailed = "DISPENSE_FAILED";
    public const string Refunded = "REFUNDED";

    public static readonly HashSet<string> All = new()
    {
        PendingPayment, Paid, Dispensing, Completed, Expired, PaymentFailed, DispenseFailed, Refunded
    };
}

public static class OrderRules
{
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromSeconds(120);   // BR-05
    public static readonly TimeSpan DeviceAckWindow = TimeSpan.FromSeconds(60);   // BR-07
    public static readonly TimeSpan DispenseWindow = TimeSpan.FromSeconds(30);    // BR-08

    // Lý do hoàn tiền khi đơn quá hạn; null nếu trạng thái đó không bị hoàn tiền
    public static string? StaleRefundReason(string status) => status switch
    {
        OrderStatus.Paid => "DEVICE_NO_ACK",
        OrderStatus.Dispensing => "DISPENSE_TIMEOUT",
        _ => null
    };
    public static DateTimeOffset ExpiresAtFor(DateTimeOffset createdAt) => createdAt + PaymentWindow;

    public static bool IsPaymentOverdue(string status, DateTimeOffset expiresAt, DateTimeOffset now) =>
        status == OrderStatus.PendingPayment && now >= expiresAt;

    // BR-03, BR-04: trả mã lỗi, null nếu được tạo đơn
    public static string? CheckCanCreate(string machineStatus, bool? productActive, int quantity, int holds, int jamCount)
    {
        if (machineStatus != "ACTIVE") return "MACHINE_NOT_ACTIVE";
        if (productActive != true) return "PRODUCT_INACTIVE";
        if (jamCount > 0) return "TRAY_BLOCKED";
        if (quantity - holds <= 0) return "TRAY_EMPTY";
        return null;
    }

    public static string QrContent(string machineCode, int orderId, int amount) =>
        $"VENDING|{machineCode}|{orderId}|{amount}";
    private static readonly HashSet<(string From, string To)> AllowedTransitions = new()
{
    (OrderStatus.PendingPayment, OrderStatus.Paid),
    (OrderStatus.PendingPayment, OrderStatus.PaymentFailed),
    (OrderStatus.PendingPayment, OrderStatus.Expired),
    (OrderStatus.Paid, OrderStatus.Dispensing),
    (OrderStatus.Paid, OrderStatus.DispenseFailed),
    (OrderStatus.Dispensing, OrderStatus.Completed),
    (OrderStatus.Dispensing, OrderStatus.DispenseFailed),
    (OrderStatus.DispenseFailed, OrderStatus.Refunded)
};

    public static bool CanTransition(string from, string to) => AllowedTransitions.Contains((from, to));
}

public static class AlertRules
{
    // BR-11: sau khi trừ kho
    public static string? StockAlertFor(int quantityAfter, int threshold) =>
        quantityAfter == 0 ? "OUT_OF_STOCK" : quantityAfter <= threshold ? "LOW_STOCK" : null;
}