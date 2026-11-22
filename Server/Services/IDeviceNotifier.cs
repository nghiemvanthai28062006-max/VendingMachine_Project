using Server.Models;

namespace Server.Services;

public interface IDeviceNotifier
{
    Task PublishTraysAsync(int machineId);
    Task PublishOrderStatusAsync(string machineCode, OrderStatusMessage message);
    Task PublishDispenseCommandAsync(string machineCode, DispenseCommandMessage message);
}