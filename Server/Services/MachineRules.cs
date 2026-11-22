namespace Server.Services;

public static class MachineRules
{
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(60);

    // ONLINE/OFFLINE là giá trị tính ra, không lưu DB (BR-12)
    public static string ConnectionOf(DateTimeOffset? lastHeartbeat, DateTimeOffset now) =>
        lastHeartbeat is null || now - lastHeartbeat.Value > OfflineAfter ? "OFFLINE" : "ONLINE";
}
