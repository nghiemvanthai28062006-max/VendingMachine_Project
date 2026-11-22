using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class DeviceService
{
    private readonly IDeviceRepository _repo;
    private readonly IMachineRepository _machines;
    private readonly IClock _clock;
    private readonly ILogger<DeviceService> _log;

    public DeviceService(IDeviceRepository repo, IMachineRepository machines, IClock clock, ILogger<DeviceService> log)
    {
        _repo = repo;
        _machines = machines;
        _clock = clock;
        _log = log;
    }

    public async Task RecordHeartbeatAsync(string machineCode)
    {
        var rows = await _repo.SetHeartbeatAsync(machineCode, _clock.Now.DateTime);
        if (rows == 0) _log.LogWarning("Heartbeat từ máy không tồn tại: {Code}", machineCode);
        else _log.LogDebug("Heartbeat {Code}", machineCode);
    }

    // Nhận LWT: last_heartbeat = NULL thì máy hiện OFFLINE ngay (BR-12)
    public async Task MarkDisconnectedAsync(string machineCode)
    {
        var rows = await _repo.SetHeartbeatAsync(machineCode, null);
        if (rows == 0) _log.LogWarning("LWT từ máy không tồn tại: {Code}", machineCode);
        else _log.LogInformation("Máy {Code} ngắt kết nối (LWT)", machineCode);
    }

    public async Task<IReadOnlyList<int>> GetAllMachineIdsAsync() =>
        (await _repo.GetAllMachineIdsAsync()).ToList();

    public async Task<TraysMessage?> BuildTraysMessageAsync(int machineId)
    {
        var machine = await _machines.GetByIdAsync(machineId);
        if (machine is null) return null;

        var now = _clock.Now;
        var states = await _repo.GetTrayStatesAsync(machineId, now.DateTime);

        return new TraysMessage
        {
            MachineCode = machine.Code,
            MachineStatus = machine.Status,
            UpdatedAt = now,
            Trays = states.Select(s => new TrayItem
            {
                TrayCode = s.TrayCode,
                Quantity = s.Quantity,
                Product = s.ProductName is null ? null : new TrayProductInfo
                {
                    Name = s.ProductName,
                    VolumeMl = s.VolumeMl ?? 0,
                    Price = s.Price ?? 0,
                    ImageUrl = s.ImageUrl
                },
                Available = TrayRules.IsAvailable(machine.Status, s.ProductActive, s.Quantity, s.Holds, s.JamCount)
            }).ToList()
        };
    }
}