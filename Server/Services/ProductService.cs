using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class ProductService
{
    private readonly IProductRepository _repo;
    private readonly IDeviceNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<ProductService> _log;

    public ProductService(IProductRepository repo, IDeviceNotifier notifier, IClock clock, ILogger<ProductService> log)
    {
        _repo = repo;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    public Task<IEnumerable<Product>> GetAllAsync() => _repo.GetAllAsync();

    public async Task<Result<Product>> CreateAsync(ProductRequest req)
    {
        var id = await _repo.InsertAsync(req, _clock.Now);
        _log.LogInformation("Thêm sản phẩm {Id}: {Name}", id, req.Name);
        return Result<Product>.Ok((await _repo.GetByIdAsync(id))!);
    }

    public async Task<Result<Product>> UpdateAsync(int id, ProductRequest req)
    {
        var old = await _repo.GetByIdAsync(id);
        if (old is null) return NotFound();

        await _repo.UpdateAsync(id, req);   // đổi giá không ảnh hưởng đơn cũ (BR-02): đơn đã chốt giá riêng
        if (old.Price != req.Price)
            _log.LogInformation("Đổi giá sản phẩm {Id}: {Old} -> {New}", id, old.Price, req.Price);

        await NotifyMachinesAsync(id);
        return Result<Product>.Ok((await _repo.GetByIdAsync(id))!);
    }

    public async Task<Result<Product>> SetActiveAsync(int id, bool isActive)
    {
        if (await _repo.GetByIdAsync(id) is null) return NotFound();

        await _repo.SetActiveAsync(id, isActive);
        _log.LogInformation("Sản phẩm {Id} {State}", id, isActive ? "mở bán" : "ngừng bán");

        await NotifyMachinesAsync(id);
        return Result<Product>.Ok((await _repo.GetByIdAsync(id))!);
    }

    // Ghi DB trước, rồi báo các máy đang dùng sản phẩm này cập nhật danh sách khay.
    // Publish lỗi thì chỉ ghi log, không làm hỏng thao tác của Admin.
    private async Task NotifyMachinesAsync(int productId)
    {
        foreach (var machineId in await _repo.GetMachineIdsUsingAsync(productId))
        {
            try { await _notifier.PublishTraysAsync(machineId); }
            catch (Exception ex) { _log.LogWarning(ex, "Không publish được trays của máy {MachineId}", machineId); }
        }
    }

    private static Result<Product> NotFound() =>
        Result<Product>.Fail("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm");
}