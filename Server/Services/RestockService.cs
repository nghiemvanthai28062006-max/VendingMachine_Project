using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class RestockService
{
    private readonly ITransactionRunner _tx;
    private readonly IDbConnectionFactory _db;
    private readonly RestockRepository _repo;
    private readonly IMachineRepository _machines;
    private readonly IDeviceNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<RestockService> _log;

    public RestockService(ITransactionRunner tx, IDbConnectionFactory db, RestockRepository repo,
        IMachineRepository machines, IDeviceNotifier notifier, IClock clock, ILogger<RestockService> log)
    {
        _tx = tx;
        _db = db;
        _repo = repo;
        _machines = machines;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    // Dừng transaction giữa chừng vì lỗi nghiệp vụ: rollback rồi báo lỗi
    private sealed class AbortException : Exception
    {
        public string Code { get; }
        public AbortException(string code, string message) : base(message) => Code = code;
    }

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Fail(code, message);
    private static Result<RestockTicketDetail> NotFound() =>
        Fail<RestockTicketDetail>("TICKET_NOT_FOUND", "Không tìm thấy phiếu tiếp hàng");
    private static Result<RestockTicketDetail> Forbidden() =>
        Fail<RestockTicketDetail>("FORBIDDEN", "Phiếu này không được giao cho bạn");

    // Nhân viên chỉ làm việc với phiếu của mình; Admin làm với mọi phiếu (BR-17)
    private static bool CanAccess(RestockTicketListItem t, int userId, string role) =>
        role != "STAFF" || t.StaffId == userId;

    // =====================================================================
    // Gợi ý và danh sách nhân viên (Admin)
    // =====================================================================
    public async Task<List<RestockSuggestion>> GetSuggestionsAsync()
    {
        await using var conn = _db.Create();
        var rows = await _repo.GetCandidatesAsync(conn, null, null);
        return RestockRules.BuildSuggestions(rows);
    }

    public async Task<List<StaffInfo>> GetStaffAsync()
    {
        await using var conn = _db.Create();
        return (await _repo.ListStaffAsync(conn)).ToList();
    }

    // =====================================================================
    // Tạo phiếu (Admin, BR-13)
    // =====================================================================
    public async Task<Result<RestockTicketDetail>> CreateAsync(CreateTicketRequest req)
    {
        var machine = await _machines.GetByIdAsync(req.MachineId!.Value);
        if (machine is null) return Fail<RestockTicketDetail>("MACHINE_NOT_FOUND", "Không tìm thấy máy");

        if (req.StaffId != null)
        {
            await using var conn = _db.Create();
            var user = await _repo.GetUserAsync(conn, req.StaffId.Value);
            if (user is null || user.Role != "STAFF" || !user.IsActive)
                return Fail<RestockTicketDetail>("STAFF_NOT_FOUND", "Không tìm thấy nhân viên");
        }

        var now = _clock.Now;
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        var ticketId = await _tx.RunAsync<int?>(async (c, t) =>
        {
            var trays = (await _repo.GetCandidatesAsync(c, t, machine.Id)).ToList();
            if (trays.Count == 0) return null;

            var id = await _repo.InsertTicketAsync(c, t, machine.Id, req.StaffId, note, now);
            foreach (var tray in trays)
                await _repo.InsertItemAsync(c, t, id, tray.TrayId, RestockRules.PlannedQty(tray.Capacity, tray.Quantity));
            return id;
        });

        if (ticketId is null)
            return Fail<RestockTicketDetail>("NOTHING_TO_RESTOCK", $"Máy {machine.Code} không có khay nào cần châm");

        _log.LogInformation("Tạo phiếu tiếp hàng {Id} cho máy {Code}, giao cho nhân viên {StaffId}",
            ticketId, machine.Code, req.StaffId);

        await using var readConn = _db.Create();
        return Result<RestockTicketDetail>.Ok(await LoadDetailAsync(readConn, ticketId.Value));
    }

    // =====================================================================
    // Danh sách và chi tiết (Admin, Nhân viên)
    // =====================================================================
    public async Task<Result<List<RestockTicketListItem>>> ListAsync(int userId, string role, string? status, int? machineId)
    {
        if (!string.IsNullOrEmpty(status) && !TicketStatus.All.Contains(status))
            return Fail<List<RestockTicketListItem>>("VALIDATION_ERROR", "Trạng thái phiếu không hợp lệ");

        var filter = new TicketFilter
        {
            Status = status,
            MachineId = machineId,
            StaffId = role == "STAFF" ? userId : null
        };
        await using var conn = _db.Create();
        return Result<List<RestockTicketListItem>>.Ok(await _repo.ListTicketsAsync(conn, filter));
    }

    public async Task<Result<RestockTicketDetail>> GetDetailAsync(int id, int userId, string role)
    {
        await using var conn = _db.Create();
        var ticket = await _repo.GetTicketAsync(conn, null, id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket, userId, role)) return Forbidden();

        ticket.Items = (await _repo.GetItemsAsync(conn, null, id)).ToList();
        return Result<RestockTicketDetail>.Ok(ticket);
    }

    // =====================================================================
    // Nhận phiếu: OPEN -> IN_PROGRESS
    // =====================================================================
    public async Task<Result<RestockTicketDetail>> StartAsync(int id, int userId, string role)
    {
        await using var conn = _db.Create();
        var ticket = await _repo.GetTicketAsync(conn, null, id);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket, userId, role)) return Forbidden();
        if (!RestockRules.CanStart(ticket.Status))
            return Fail<RestockTicketDetail>("INVALID_STATE", $"Phiếu đang ở trạng thái {ticket.Status}");

        if (await _repo.StartAsync(conn, id) != 1)
            return Fail<RestockTicketDetail>("INVALID_STATE", "Phiếu đã đổi trạng thái");

        _log.LogInformation("Phiếu {Id} OPEN -> IN_PROGRESS (người dùng {UserId})", id, userId);
        return Result<RestockTicketDetail>.Ok(await LoadDetailAsync(conn, id));
    }

    // =====================================================================
    // Xác nhận đã châm hàng (BR-14), một transaction cho cả phiếu
    // =====================================================================
    public async Task<Result<RestockTicketDetail>> ConfirmAsync(int id, ConfirmTicketRequest req, int userId, string role)
    {
        RestockTicketDetail ticket;
        List<RestockItemView> items;
        await using (var conn = _db.Create())
        {
            var found = await _repo.GetTicketAsync(conn, null, id);
            if (found is null) return NotFound();
            if (!CanAccess(found, userId, role)) return Forbidden();
            if (!RestockRules.CanConfirm(found.Status))
                return Fail<RestockTicketDetail>("INVALID_STATE", $"Phiếu đang ở trạng thái {found.Status}");

            ticket = found;
            items = (await _repo.GetItemsAsync(conn, null, id)).ToList();
        }

        // Danh sách gửi lên phải khớp đúng các khay của phiếu, mỗi khay một lần
        var sent = req.Items!;
        if (sent.Select(i => i.TrayId!.Value).Distinct().Count() != sent.Count)
            return Fail<RestockTicketDetail>("VALIDATION_ERROR", "Có khay bị lặp trong danh sách");
        if (!sent.Select(i => i.TrayId!.Value).ToHashSet().SetEquals(items.Select(i => i.TrayId)))
            return Fail<RestockTicketDetail>("VALIDATION_ERROR", "Danh sách khay phải khớp đúng các khay trong phiếu");

        var now = _clock.Now;
        try
        {
            await _tx.RunAsync(async (c, t) =>
            {
                // Chuyển phiếu sang DONE trước: người khác đã xử lý thì dừng ngay
                if (await _repo.CompleteAsync(c, t, id, now) != 1)
                    throw new AbortException("INVALID_STATE", "Phiếu đã hoàn tất hoặc đã bị hủy");

                // Khóa khay theo thứ tự id tăng dần (tránh khóa chéo)
                foreach (var item in sent.OrderBy(i => i.TrayId))
                {
                    var tray = await _repo.LockTrayAsync(c, t, item.TrayId!.Value)
                        ?? throw new AbortException("TRAY_NOT_FOUND", "Không tìm thấy khay");
                    var add = item.ActualQty!.Value;

                    if (add > 0)
                    {
                        if (await _repo.AddQuantityAsync(c, t, tray.Id, add) != 1)
                            throw new AbortException("EXCEEDS_CAPACITY",
                                $"Khay {tray.Code}: châm {add} chai sẽ vượt sức chứa {tray.Capacity} (đang có {tray.Quantity})");
                        await _repo.ResolveStockAlertsAsync(c, t, tray.Id, userId, now);
                    }
                    await _repo.SetActualAsync(c, t, id, tray.Id, add);
                }
                return true;
            });
        }
        catch (AbortException ex)
        {
            return Fail<RestockTicketDetail>(ex.Code, ex.Message);
        }

        _log.LogInformation("Phiếu {Id} -> DONE (người dùng {UserId}), tồn kho máy {Code} đã cập nhật",
            id, userId, ticket.MachineCode);

        // Tồn kho đổi nên báo Device cập nhật danh sách khay (ghi DB trước, publish sau)
        try { await _notifier.PublishTraysAsync(ticket.MachineId); }
        catch (Exception ex) { _log.LogWarning(ex, "Không publish được trays của máy {MachineId}", ticket.MachineId); }

        await using var readConn = _db.Create();
        return Result<RestockTicketDetail>.Ok(await LoadDetailAsync(readConn, id));
    }

    // =====================================================================
    // Hủy phiếu (Admin)
    // =====================================================================
    public async Task<Result<RestockTicketDetail>> CancelAsync(int id)
    {
        await using var conn = _db.Create();
        var ticket = await _repo.GetTicketAsync(conn, null, id);
        if (ticket is null) return NotFound();
        if (!RestockRules.CanCancel(ticket.Status))
            return Fail<RestockTicketDetail>("INVALID_STATE", $"Phiếu đang ở trạng thái {ticket.Status}");

        if (await _repo.CancelAsync(conn, id) != 1)
            return Fail<RestockTicketDetail>("INVALID_STATE", "Phiếu đã đổi trạng thái");

        _log.LogInformation("Phiếu {Id} -> CANCELLED", id);
        return Result<RestockTicketDetail>.Ok(await LoadDetailAsync(conn, id));
    }

    private async Task<RestockTicketDetail> LoadDetailAsync(MySqlConnector.MySqlConnection conn, int id)
    {
        var ticket = (await _repo.GetTicketAsync(conn, null, id))!;
        ticket.Items = (await _repo.GetItemsAsync(conn, null, id)).ToList();
        return ticket;
    }
}