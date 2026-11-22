namespace Server.Models;

// Cảnh báo ảo MACHINE_OFFLINE: Id và CreatedAt là null (vắng mặt trong JSON), Virtual = true
public class AlertView
{
    public int? Id { get; set; }
    public int MachineId { get; set; }
    public int? TrayId { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsResolved { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
    public bool? Virtual { get; set; }
}

public class AlertFilter
{
    public bool? Resolved { get; set; }
    public int? MachineId { get; set; }
    public string? Type { get; set; }
}
