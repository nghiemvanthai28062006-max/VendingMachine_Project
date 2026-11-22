namespace Server.Services;

public interface IDeviceNotifier
{
    Task PublishTraysAsync(int machineId);
}

public class NullDeviceNotifier : IDeviceNotifier
{
    // Tạm thời không làm gì cả
    public Task PublishTraysAsync(int machineId) => Task.CompletedTask;
}