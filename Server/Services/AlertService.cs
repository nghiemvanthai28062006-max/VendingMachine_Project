using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class AlertService
{
    private static readonly HashSet<string> ValidTypes = new()
        { "LOW_STOCK", "OUT_OF_STOCK", "JAMMED", "MACHINE_OFFLINE" };

    private readonly AlertRepository _repo;
    private readonly IMachineRepository _machines;
    private readonly IDeviceNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<AlertService> _log;

    public AlertService(AlertRepository repo, IMachineRepository machines, IDeviceNotifier notifier,
        IClock clock, ILogger<AlertService> log)
    {
        _repo = repo;
        _machines = machines;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    public async Task<Result<List<AlertView>>> GetAsync(AlertFilter f)
    {
        if (!string.IsNullOrEmpty(f.Type) && !ValidTypes.Contains(f.Type))
            return Result<List<AlertView>>.Fail("VALIDATION_ERROR", "Loại cảnh báo không hợp lệ");

        var list = await _repo.SearchAsync(f);

        // BR-12: MACHINE_OFFLINE không lưu bảng, tính ra lúc đọc từ last_heartbeat.
        // Chỉ hiện khi lọc chưa xử lý (hoặc không lọc) và loại là MACHINE_OFFLINE (hoặc không lọc loại).
        var wantsVirtual = f.Resolved != true && (string.IsNullOrEmpty(f.Type) || f.Type == "MACHINE_OFFLINE");
        if (wantsVirtual)
        {
            var now = _clock.Now;
            foreach (var m in await _machines.GetAllAsync())
            {
                if (f.MachineId != null && f.MachineId != m.Id) continue;
                if (MachineRules.ConnectionOf(m.LastHeartbeat, now) != "OFFLINE") continue;

                list.Add(new AlertView
                {
                    MachineId = m.Id,
                    Type = "MACHINE_OFFLINE",
                    Message = $"Máy {m.Code} mất kết nối",
                    IsResolved = false,
                    Virtual = true
                });
            }
        }
        return Result<List<AlertView>>.Ok(list);
    }

    public async Task<Result<AlertView>> ResolveAsync(int id, int userId)
    {
        var alert = await _repo.GetByIdAsync(id);
        if (alert is null) return Result<AlertView>.Fail("ALERT_NOT_FOUND", "Không tìm thấy cảnh báo");
        if (alert.IsResolved) return Result<AlertView>.Fail("INVALID_STATE", "Cảnh báo đã được xử lý");

        if (await _repo.ResolveAsync(id, userId, _clock.Now) != 1)
            return Result<AlertView>.Fail("INVALID_STATE", "Cảnh báo đã được xử lý");

        _log.LogInformation("Cảnh báo {Id} ({Type}) được đóng bởi người dùng {UserId}", id, alert.Type, userId);

        // Đóng cảnh báo kẹt thì khay bán lại được (BR-04): báo Device cập nhật danh sách khay
        if (alert.Type == "JAMMED")
        {
            try { await _notifier.PublishTraysAsync(alert.MachineId); }
            catch (Exception ex) { _log.LogWarning(ex, "Không publish được trays của máy {MachineId}", alert.MachineId); }
        }

        return Result<AlertView>.Ok((await _repo.GetByIdAsync(id))!);
    }
}
