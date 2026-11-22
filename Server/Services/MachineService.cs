using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class MachineService
{
    private readonly IMachineRepository _machines;
    private readonly ITrayRepository _trays;
    private readonly IProductRepository _products;
    private readonly IDeviceNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<MachineService> _log;

    public MachineService(IMachineRepository machines, ITrayRepository trays, IProductRepository products,
        IDeviceNotifier notifier, IClock clock, ILogger<MachineService> log)
    {
        _machines = machines;
        _trays = trays;
        _products = products;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    public async Task<IEnumerable<MachineView>> GetAllAsync()
    {
        var now = _clock.Now;
        return (await _machines.GetAllAsync()).Select(m => ToView(m, now)).ToList();
    }

    public async Task<Result<MachineView>> CreateAsync(CreateMachineRequest req)
    {
        req.Code = req.Code.Trim();
        req.Name = req.Name.Trim();

        var id = await _machines.InsertAsync(req, _clock.Now);
        if (id is null)
            return Result<MachineView>.Fail("DUPLICATE_CODE", $"Mã máy {req.Code} đã tồn tại");

        _log.LogInformation("Thêm máy {Id}: {Code}", id, req.Code);
        var machine = (await _machines.GetByIdAsync(id.Value))!;
        return Result<MachineView>.Ok(ToView(machine, _clock.Now));
    }

    public async Task<Result<MachineView>> UpdateAsync(int id, UpdateMachineRequest req)
    {
        if (await _machines.GetByIdAsync(id) is null)
            return Result<MachineView>.Fail("MACHINE_NOT_FOUND", "Không tìm thấy máy");

        req.Name = req.Name.Trim();
        await _machines.UpdateAsync(id, req);
        _log.LogInformation("Sửa máy {Id}, trạng thái {Status}", id, req.Status);

        await NotifyAsync(id);   // đổi trạng thái máy làm đổi "available" của các khay
        var machine = (await _machines.GetByIdAsync(id))!;
        return Result<MachineView>.Ok(ToView(machine, _clock.Now));
    }

    public async Task<Result<IEnumerable<Tray>>> GetTraysAsync(int machineId)
    {
        if (await _machines.GetByIdAsync(machineId) is null)
            return Result<IEnumerable<Tray>>.Fail("MACHINE_NOT_FOUND", "Không tìm thấy máy");

        return Result<IEnumerable<Tray>>.Ok(await _trays.GetByMachineAsync(machineId));
    }

    public async Task<Result<Tray>> CreateTrayAsync(int machineId, CreateTrayRequest req)
    {
        if (await _machines.GetByIdAsync(machineId) is null)
            return Result<Tray>.Fail("MACHINE_NOT_FOUND", "Không tìm thấy máy");
        if (!await ProductExistsAsync(req.ProductId))
            return Result<Tray>.Fail("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm");
        if (req.Quantity > req.Capacity)
            return Result<Tray>.Fail("VALIDATION_ERROR", "Số chai ban đầu không được lớn hơn sức chứa");

        req.Code = req.Code.Trim();
        var id = await _trays.InsertAsync(machineId, req);
        if (id is null)
            return Result<Tray>.Fail("DUPLICATE_CODE", $"Máy này đã có khay {req.Code}");

        _log.LogInformation("Thêm khay {Code} cho máy {MachineId}", req.Code, machineId);
        await NotifyAsync(machineId);
        return Result<Tray>.Ok((await _trays.GetByIdAsync(id.Value))!);
    }

    public async Task<Result<Tray>> UpdateTrayAsync(int trayId, UpdateTrayRequest req)
    {
        var tray = await _trays.GetByIdAsync(trayId);
        if (tray is null)
            return Result<Tray>.Fail("TRAY_NOT_FOUND", "Không tìm thấy khay");
        if (!await ProductExistsAsync(req.ProductId))
            return Result<Tray>.Fail("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm");
        if (req.Capacity < tray.Quantity)
            return Result<Tray>.Fail("VALIDATION_ERROR",
                $"Sức chứa không được nhỏ hơn số chai hiện có ({tray.Quantity})");

        await _trays.UpdateAsync(trayId, req);
        _log.LogInformation("Sửa khay {TrayId} của máy {MachineId}", trayId, tray.MachineId);

        await NotifyAsync(tray.MachineId);
        return Result<Tray>.Ok((await _trays.GetByIdAsync(trayId))!);
    }

    private async Task<bool> ProductExistsAsync(int? productId) =>
        productId is null || await _products.GetByIdAsync(productId.Value) is not null;

    private static MachineView ToView(Machine m, DateTimeOffset now) => new()
    {
        Id = m.Id,
        Code = m.Code,
        Name = m.Name,
        Location = m.Location,
        Status = m.Status,
        LastHeartbeat = m.LastHeartbeat,
        Connection = MachineRules.ConnectionOf(m.LastHeartbeat, now)
    };

    // Ghi DB trước, publish sau; publish lỗi chỉ ghi log, không làm hỏng thao tác của Admin
    private async Task NotifyAsync(int machineId)
    {
        try { await _notifier.PublishTraysAsync(machineId); }
        catch (Exception ex) { _log.LogWarning(ex, "Không publish được trays của máy {MachineId}", machineId); }
    }
}