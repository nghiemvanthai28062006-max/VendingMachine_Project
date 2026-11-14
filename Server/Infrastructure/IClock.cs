namespace Server.Infrastructure;

public interface IClock { DateTimeOffset Now { get; } }

public class SystemClock : IClock
{
    // Giờ Việt Nam (UTC+7), thống nhất với toàn hệ thống
    public DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
}