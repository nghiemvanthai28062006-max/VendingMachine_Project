namespace Server.Models;

// Payload Device gửi lên (trường thiếu thì null để kiểm tra)
public class CreateOrderMessage { public string? RequestId { get; set; } public string? MachineCode { get; set; } public string? TrayCode { get; set; } }
public class PaymentCallbackMessage { public string? RequestId { get; set; } public int? OrderId { get; set; } public string? TransactionCode { get; set; } public int? Amount { get; set; } public string? Result { get; set; } }
public class DispenseStartedMessage { public string? RequestId { get; set; } public int? OrderId { get; set; } }
public class DispenseResultMessage { public string? RequestId { get; set; } public int? OrderId { get; set; } public string? TrayCode { get; set; } public string? Result { get; set; } }

// Payload Server gửi xuống Device
public record ErrorInfo(string Code, string Message);

public class OrderStatusMessage
{
    public string? RequestId { get; set; }
    public int? OrderId { get; set; }
    public string? Status { get; set; }
    public int? Amount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? QrContent { get; set; }
    public string? Reason { get; set; }
    public int? RefundAmount { get; set; }
    public ErrorInfo? Error { get; set; }
}

public class DispenseCommandMessage { public int OrderId { get; set; } public string TrayCode { get; set; } = ""; }
