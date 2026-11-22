using MySqlConnector;
using Server.Infrastructure;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public class OrderService
{
    private readonly ITransactionRunner _tx;
    private readonly IDbConnectionFactory _db;
    private readonly OrderRepository _repo;
    private readonly IDeviceNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<OrderService> _log;

    public OrderService(ITransactionRunner tx, IDbConnectionFactory db, OrderRepository repo,
        IDeviceNotifier notifier, IClock clock, ILogger<OrderService> log)
    {
        _tx = tx;
        _db = db;
        _repo = repo;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    // Dừng transaction giữa chừng vì lỗi nghiệp vụ: rollback rồi báo lỗi cho Device
    private sealed class AbortException : Exception
    {
        public string Code { get; }
        public AbortException(string code, string message) : base(message) => Code = code;
    }

    private sealed class Outcome
    {
        public Order? Order { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public bool Duplicate { get; init; }
        public string? RefundReason { get; init; }
        public static Outcome Fail(string code, string message) => new() { ErrorCode = code, ErrorMessage = message };
        public static Outcome Dup(Order o) => new() { Order = o, Duplicate = true };
        public static Outcome Changed(Order o) => new() { Order = o };
        public static Outcome Refunded(Order o, string reason) => new() { Order = o, RefundReason = reason };
    }

    private sealed record SweptOrder(StaleOrder Order, string? RefundReason);

    // =====================================================================
    // order/create
    // =====================================================================
    public async Task CreateOrderAsync(string machineCode, CreateOrderMessage msg)
    {
        if (!IsUuid(msg.RequestId))
        { await RejectAsync(machineCode, null, null, "VALIDATION_ERROR", "Thiếu hoặc sai requestId"); return; }
        if (string.IsNullOrWhiteSpace(msg.TrayCode))
        { await RejectAsync(machineCode, msg.RequestId, null, "VALIDATION_ERROR", "Thiếu mã khay"); return; }

        await SweepStaleOrdersAsync();

        var now = _clock.Now;
        var trayCode = msg.TrayCode.Trim();

        var outcome = await _tx.RunAsync(async (c, t) =>
        {
            var existing = await _repo.GetByRequestIdAsync(c, t, msg.RequestId!);
            if (existing != null && existing.MachineCode == machineCode) return Outcome.Dup(existing);

            var machine = await _repo.GetMachineByCodeAsync(c, t, machineCode);
            if (machine is null) return Outcome.Fail("MACHINE_NOT_FOUND", "Mã máy không tồn tại");

            var tray = await _repo.LockTrayByCodeAsync(c, t, machine.Id, trayCode);
            if (tray is null) return Outcome.Fail("TRAY_NOT_FOUND", $"Không có khay {trayCode}");

            var product = tray.ProductId is null ? null : await _repo.GetProductAsync(c, t, tray.ProductId.Value);
            var holds = await _repo.CountHoldsAsync(c, t, tray.Id, now);
            var jam = await _repo.CountJamAsync(c, t, tray.Id);

            var error = OrderRules.CheckCanCreate(machine.Status, product?.IsActive, tray.Quantity, holds, jam);
            if (error != null) return Outcome.Fail(error, ErrorText(error, trayCode));

            var orderId = await _repo.InsertOrderAsync(c, t, msg.RequestId!, machine.Id, tray.Id,
                product!.Id, product.Price, now, OrderRules.ExpiresAtFor(now));
            await _repo.InsertPaymentAsync(c, t, orderId, product.Price, now);

            return Outcome.Changed((await _repo.GetByIdAsync(c, t, orderId))!);
        });

        if (outcome.ErrorCode != null)
        { await RejectAsync(machineCode, msg.RequestId, null, outcome.ErrorCode, outcome.ErrorMessage!); return; }

        var order = outcome.Order!;
        await PublishStatusAsync(machineCode, StatusOf(order, msg.RequestId));
        if (outcome.Duplicate) return;

        _log.LogInformation("Order {Id} tạo mới: máy {Code} khay {Tray}, {Amount}đ", order.Id, machineCode, trayCode, order.Amount);
        await PublishTraysAsync(order.MachineId);   // số chai khả dụng đã đổi
    }

    // =====================================================================
    // payment/callback
    // =====================================================================
    public async Task PaymentCallbackAsync(string machineCode, PaymentCallbackMessage msg)
    {
        if (!IsUuid(msg.RequestId) || msg.OrderId is null || msg.Amount is null
            || string.IsNullOrWhiteSpace(msg.TransactionCode) || msg.TransactionCode.Length > 50
            || msg.Result is not ("SUCCESS" or "FAILED"))
        { await RejectAsync(machineCode, msg.RequestId, msg.OrderId, "VALIDATION_ERROR", "Dữ liệu thanh toán không hợp lệ"); return; }

        var now = _clock.Now;
        var orderId = msg.OrderId.Value;
        var txCode = msg.TransactionCode.Trim();

        await SweepStaleOrdersAsync(orderId);   // CheckAndExpireOrder (BR-20)

        Outcome outcome;
        try
        {
            outcome = await _tx.RunAsync(async (c, t) =>
            {
                var order = await _repo.GetByIdAsync(c, t, orderId);
                if (order is null || order.MachineCode != machineCode)
                    return Outcome.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn");

                var payment = await _repo.GetPaymentByOrderAsync(c, t, orderId);

                // Gửi trùng: cùng transactionCode đã ghi cho chính đơn này
                if (payment?.TransactionCode == txCode) return Outcome.Dup(order);

                // Đến sau hạn (BR-05): đơn đã bị dọn thành EXPIRED, không nhận tiền, trả trạng thái EXPIRED
                if (order.Status == OrderStatus.Expired) return Outcome.Changed(order);

                if (order.Status != OrderStatus.PendingPayment)
                    return Outcome.Fail("INVALID_STATE", $"Đơn đang ở trạng thái {order.Status}");

                if (await _repo.CountTransactionUsedByOtherAsync(c, t, txCode, orderId) > 0)
                    return Outcome.Fail("DUPLICATE_TRANSACTION", "Mã giao dịch đã được dùng cho đơn khác");

                // BR-06: báo lỗi hoặc sai số tiền
                if (msg.Result == "FAILED" || msg.Amount != order.Amount)
                {
                    await _repo.UpdatePaymentAsync(c, t, orderId, "FAILED", txCode, null);
                    if (await _repo.MarkPaymentFailedAsync(c, t, orderId) != 1)
                        throw new AbortException("INVALID_STATE", "Đơn đã đổi trạng thái");
                    order.Status = OrderStatus.PaymentFailed;
                    return Outcome.Changed(order);
                }

                await _repo.UpdatePaymentAsync(c, t, orderId, "SUCCESS", txCode, now);
                if (await _repo.MarkPaidAsync(c, t, orderId, now) != 1)
                    throw new AbortException("INVALID_STATE", "Đơn đã đổi trạng thái");
                order.Status = OrderStatus.Paid;
                order.PaidAt = now;
                return Outcome.Changed(order);
            });
        }
        catch (AbortException ex)
        { await RejectAsync(machineCode, msg.RequestId, orderId, ex.Code, ex.Message); return; }

        if (outcome.ErrorCode != null)
        { await RejectAsync(machineCode, msg.RequestId, orderId, outcome.ErrorCode, outcome.ErrorMessage!); return; }

        var o = outcome.Order!;
        await PublishStatusAsync(machineCode, StatusOf(o, msg.RequestId));
        if (outcome.Duplicate) return;

        _log.LogInformation("Order {Id} -> {Status} (payment/callback, máy {Code})", o.Id, o.Status, machineCode);

        if (o.Status == OrderStatus.Paid)
            await PublishCommandAsync(machineCode, new DispenseCommandMessage { OrderId = o.Id, TrayCode = o.TrayCode });
        else
            await PublishTraysAsync(o.MachineId);   // hết hạn hoặc thanh toán lỗi: nhả chỗ giữ
    }

    // =====================================================================
    // dispense/started
    // =====================================================================
    public async Task DispenseStartedAsync(string machineCode, DispenseStartedMessage msg)
    {
        if (!IsUuid(msg.RequestId) || msg.OrderId is null)
        { await RejectAsync(machineCode, msg.RequestId, msg.OrderId, "VALIDATION_ERROR", "Dữ liệu không hợp lệ"); return; }

        var now = _clock.Now;
        var orderId = msg.OrderId.Value;

        await SweepStaleOrdersAsync(orderId);   // quá 60s kể từ paid_at thì đơn đã bị hoàn tiền (BR-07)

        Outcome outcome;
        try
        {
            outcome = await _tx.RunAsync(async (c, t) =>
            {
                var order = await _repo.GetByIdAsync(c, t, orderId);
                if (order is null || order.MachineCode != machineCode)
                    return Outcome.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn");

                if (order.Status == OrderStatus.Dispensing) return Outcome.Dup(order);   // gửi trùng
                if (order.Status != OrderStatus.Paid)
                    return Outcome.Fail("INVALID_STATE", $"Đơn đang ở trạng thái {order.Status}");

                if (await _repo.MarkDispensingAsync(c, t, orderId, now) != 1)
                    throw new AbortException("INVALID_STATE", "Đơn đã đổi trạng thái");
                order.Status = OrderStatus.Dispensing;
                order.DispenseStartedAt = now;
                return Outcome.Changed(order);
            });
        }
        catch (AbortException ex)
        { await RejectAsync(machineCode, msg.RequestId, orderId, ex.Code, ex.Message); return; }

        if (outcome.ErrorCode != null)
        { await RejectAsync(machineCode, msg.RequestId, orderId, outcome.ErrorCode, outcome.ErrorMessage!); return; }

        await PublishStatusAsync(machineCode, StatusOf(outcome.Order!, msg.RequestId));
        if (!outcome.Duplicate)
            _log.LogInformation("Order {Id} PAID -> DISPENSING (máy {Code})", orderId, machineCode);
    }

    // =====================================================================
    // dispense/result (OK hoặc JAMMED)
    // =====================================================================
    public async Task DispenseResultAsync(string machineCode, DispenseResultMessage msg)
    {
        if (!IsUuid(msg.RequestId) || msg.OrderId is null || string.IsNullOrWhiteSpace(msg.TrayCode)
            || msg.Result is not ("OK" or "JAMMED"))
        { await RejectAsync(machineCode, msg.RequestId, msg.OrderId, "VALIDATION_ERROR", "Dữ liệu không hợp lệ"); return; }

        var now = _clock.Now;
        var orderId = msg.OrderId.Value;
        var trayCode = msg.TrayCode.Trim();

        await SweepStaleOrdersAsync(orderId);   // quá 30s kể từ dispense_started_at thì đơn đã bị hoàn tiền (BR-08)

        Outcome outcome;
        try
        {
            outcome = await _tx.RunAsync(async (c, t) =>
            {
                var order = await _repo.GetByIdAsync(c, t, orderId);
                if (order is null || order.MachineCode != machineCode)
                    return Outcome.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn");

                if (order.DispenseRequestId == msg.RequestId) return Outcome.Dup(order);   // gửi trùng
                if (order.TrayCode != trayCode) return Outcome.Fail("TRAY_MISMATCH", "Khay không khớp đơn");
                if (order.Status != OrderStatus.Dispensing)
                    return Outcome.Fail("INVALID_STATE", $"Đơn đang ở trạng thái {order.Status}");

                // Khóa khay TRƯỚC, sửa đơn SAU (thứ tự cố định, tránh khóa chéo)
                var tray = (await _repo.LockTrayByIdAsync(c, t, order.TrayId))!;
                var product = tray.ProductId is null ? null : await _repo.GetProductAsync(c, t, tray.ProductId.Value);
                var name = product?.Name ?? "";

                if (msg.Result == "JAMMED")
                {
                    // BR-10: kẹt hàng thì hoàn tiền, tạo cảnh báo kẹt, không trừ kho
                    if (!await RefundAsync(c, t, orderId, OrderStatus.Dispensing, msg.RequestId, order.Amount, "JAMMED", now))
                        throw new AbortException("INVALID_STATE", "Đơn đã đổi trạng thái");

                    await _repo.InsertAlertIfAbsentAsync(c, t, tray.MachineId, tray.Id, "JAMMED",
                        $"Máy {machineCode} khay {tray.Code} ({name}) bị kẹt hàng", now);

                    order.Status = OrderStatus.Refunded;
                    return Outcome.Refunded(order, "JAMMED");
                }

                // OK (BR-09): hoàn tất đơn và trừ kho trong cùng một transaction
                if (await _repo.MarkCompletedAsync(c, t, orderId, msg.RequestId!, now) != 1)
                    throw new AbortException("INVALID_STATE", "Đơn đã đổi trạng thái");
                if (await _repo.DecrementTrayAsync(c, t, order.TrayId) != 1)
                    throw new AbortException("TRAY_EMPTY", "Tồn kho không đủ");

                // BR-11: cảnh báo hết/sắp hết
                var quantityAfter = tray.Quantity - 1;
                var alertType = AlertRules.StockAlertFor(quantityAfter, tray.LowStockThreshold);
                if (alertType != null)
                {
                    var text = alertType == "OUT_OF_STOCK"
                        ? $"Máy {machineCode} khay {tray.Code} ({name}) đã hết hàng"
                        : $"Máy {machineCode} khay {tray.Code} ({name}) sắp hết hàng: còn {quantityAfter}";
                    await _repo.InsertAlertIfAbsentAsync(c, t, tray.MachineId, tray.Id, alertType, text, now);
                }

                order.Status = OrderStatus.Completed;
                order.CompletedAt = now;
                return Outcome.Changed(order);
            });
        }
        catch (AbortException ex)
        { await RejectAsync(machineCode, msg.RequestId, orderId, ex.Code, ex.Message); return; }

        if (outcome.ErrorCode != null)
        { await RejectAsync(machineCode, msg.RequestId, orderId, outcome.ErrorCode, outcome.ErrorMessage!); return; }

        var o = outcome.Order!;
        if (outcome.RefundReason != null)
        {
            // Nhả lỗi: publish hai tin liên tiếp DISPENSE_FAILED rồi REFUNDED
            await PublishStatusAsync(machineCode, new OrderStatusMessage
            { RequestId = msg.RequestId, OrderId = o.Id, Status = OrderStatus.DispenseFailed });
            await PublishStatusAsync(machineCode, new OrderStatusMessage
            {
                RequestId = msg.RequestId,
                OrderId = o.Id,
                Status = OrderStatus.Refunded,
                Reason = outcome.RefundReason,
                RefundAmount = o.Amount
            });
        }
        else
        {
            await PublishStatusAsync(machineCode, StatusOf(o, msg.RequestId));
        }
        if (outcome.Duplicate) return;

        _log.LogInformation("Order {Id} {Result} (máy {Code})", orderId,
            outcome.RefundReason != null ? "DISPENSING -> REFUNDED (kẹt hàng, đã hoàn tiền)" : "DISPENSING -> COMPLETED, tồn kho khay giảm 1",
            machineCode);
        await PublishTraysAsync(o.MachineId);   // tồn kho đổi, hoặc khay vừa bị chặn vì cảnh báo kẹt
    }

    // =====================================================================
    // Dọn đơn quá hạn (BR-20). orderId = null: quét tất cả; có giá trị: chỉ kiểm tra đơn đó.
    //   PENDING_PAYMENT quá expires_at           -> EXPIRED, payments -> FAILED
    //   PAID quá 60s kể từ paid_at               -> DISPENSE_FAILED -> hoàn tiền (DEVICE_NO_ACK)
    //   DISPENSING quá 30s kể từ dispense_started_at -> DISPENSE_FAILED -> hoàn tiền (DISPENSE_TIMEOUT)
    // =====================================================================
    public async Task SweepStaleOrdersAsync(int? orderId = null)
    {
        var now = _clock.Now;

        var swept = await _tx.RunAsync(async (c, t) =>
        {
            var done = new List<SweptOrder>();
            var stale = (await _repo.GetStaleOrdersAsync(c, t, now,
                now - OrderRules.DeviceAckWindow, now - OrderRules.DispenseWindow, orderId)).ToList();

            foreach (var o in stale)
            {
                if (o.Status == OrderStatus.PendingPayment)
                {
                    await _repo.FailPendingPaymentAsync(c, t, o.OrderId);
                    if (await _repo.MarkExpiredAsync(c, t, o.OrderId) == 1) done.Add(new SweptOrder(o, null));
                }
                else
                {
                    var reason = OrderRules.StaleRefundReason(o.Status)!;
                    if (await RefundAsync(c, t, o.OrderId, o.Status, null, o.Amount, reason, now))
                        done.Add(new SweptOrder(o, reason));
                }
            }
            return done;
        });

        var machineIds = new HashSet<int>();
        foreach (var s in swept)
        {
            var o = s.Order;
            machineIds.Add(o.MachineId);

            if (s.RefundReason is null)
            {
                _log.LogInformation("Order {Id} PENDING_PAYMENT -> EXPIRED (hết hạn thanh toán)", o.OrderId);
                await PublishStatusAsync(o.MachineCode,
                    new OrderStatusMessage { OrderId = o.OrderId, Status = OrderStatus.Expired });
            }
            else
            {
                _log.LogInformation("Order {Id} {From} -> REFUNDED ({Reason}), hoàn {Amount}đ",
                    o.OrderId, o.Status, s.RefundReason, o.Amount);
                await PublishStatusAsync(o.MachineCode,
                    new OrderStatusMessage { OrderId = o.OrderId, Status = OrderStatus.DispenseFailed });
                await PublishStatusAsync(o.MachineCode, new OrderStatusMessage
                { OrderId = o.OrderId, Status = OrderStatus.Refunded, Reason = s.RefundReason, RefundAmount = o.Amount });
            }
        }
        foreach (var id in machineIds) await PublishTraysAsync(id);   // chỗ giữ khay đã được nhả
    }

    // Hoàn tiền trong transaction đang mở (BR-10): DISPENSE_FAILED -> ghi hoàn tiền -> REFUNDED.
    // Trả false nếu đơn đã đổi trạng thái (có người khác xử lý trước).
    private async Task<bool> RefundAsync(MySqlConnection c, MySqlTransaction t, int orderId, string fromStatus,
        string? dispenseRequestId, int amount, string reason, DateTimeOffset now)
    {
        if (await _repo.MarkDispenseFailedAsync(c, t, orderId, fromStatus, dispenseRequestId) != 1) return false;

        var payment = await _repo.GetPaymentByOrderAsync(c, t, orderId)
            ?? throw new InvalidOperationException($"Đơn {orderId} không có bản ghi thanh toán");

        await _repo.InsertRefundAsync(c, t, orderId, payment.Id, amount, reason, now);
        if (await _repo.MarkRefundedAsync(c, t, orderId) != 1)
            throw new InvalidOperationException($"Không chuyển được đơn {orderId} sang REFUNDED");
        return true;
    }

    // =====================================================================
    // REST tra cứu (UC06)
    // =====================================================================
    public async Task<Result<PagedResult<OrderListItem>>> SearchAsync(OrderFilter f)
    {
        if (!string.IsNullOrEmpty(f.Status) && !OrderStatus.All.Contains(f.Status))
            return Result<PagedResult<OrderListItem>>.Fail("VALIDATION_ERROR", "Trạng thái không hợp lệ");
        if (f.From != null && f.To != null && f.From > f.To)
            return Result<PagedResult<OrderListItem>>.Fail("VALIDATION_ERROR", "Ngày bắt đầu lớn hơn ngày kết thúc");

        f.Page = Math.Max(1, f.Page);
        f.PageSize = Math.Clamp(f.PageSize, 1, 100);

        await SweepStaleOrdersAsync();
        await using var conn = _db.Create();
        var (items, total) = await _repo.SearchAsync(conn, f);
        return Result<PagedResult<OrderListItem>>.Ok(new PagedResult<OrderListItem>
        {
            Items = items,
            Page = f.Page,
            PageSize = f.PageSize,
            Total = total
        });
    }

    public async Task<Result<OrderDetail>> GetDetailAsync(int id)
    {
        await SweepStaleOrdersAsync(id);   // CheckAndExpireOrder
        await using var conn = _db.Create();
        var detail = await _repo.GetDetailAsync(conn, id);
        return detail is null
            ? Result<OrderDetail>.Fail("ORDER_NOT_FOUND", "Không tìm thấy đơn")
            : Result<OrderDetail>.Ok(detail);
    }

    // =====================================================================
    // Hàm phụ
    // =====================================================================
    private static bool IsUuid(string? s) => s is { Length: 36 } && Guid.TryParse(s, out _);

    private static string ErrorText(string code, string trayCode) => code switch
    {
        "MACHINE_NOT_ACTIVE" => "Máy đang bảo trì",
        "PRODUCT_INACTIVE" => $"Khay {trayCode} chưa có sản phẩm hoặc sản phẩm đã ngừng bán",
        "TRAY_BLOCKED" => $"Khay {trayCode} đang bị kẹt, chờ nhân viên xử lý",
        "TRAY_EMPTY" => $"Khay {trayCode} đã hết hàng",
        _ => "Không thể tạo đơn"
    };

    // Trạng thái hiện tại của đơn. Còn chờ thanh toán thì kèm số tiền, hạn và mã QR.
    private static OrderStatusMessage StatusOf(Order o, string? requestId)
    {
        var m = new OrderStatusMessage { RequestId = requestId, OrderId = o.Id, Status = o.Status };
        if (o.Status == OrderStatus.PendingPayment)
        {
            m.Amount = o.Amount;
            m.ExpiresAt = o.ExpiresAt;
            m.QrContent = OrderRules.QrContent(o.MachineCode, o.Id, o.Amount);
        }
        return m;
    }

    private Task RejectAsync(string machineCode, string? requestId, int? orderId, string code, string message)
    {
        _log.LogInformation("Từ chối yêu cầu {RequestId} (máy {Code}): {Error}", requestId, machineCode, code);
        return PublishStatusAsync(machineCode, new OrderStatusMessage
        {
            RequestId = requestId,
            OrderId = orderId,
            Error = new ErrorInfo(code, message)
        });
    }

    // Publish lỗi chỉ ghi log, không làm hỏng giao dịch đã lưu (File 02, mục 3)
    private async Task PublishStatusAsync(string machineCode, OrderStatusMessage m)
    {
        try { await _notifier.PublishOrderStatusAsync(machineCode, m); }
        catch (Exception ex) { _log.LogWarning(ex, "Không publish được order/status của máy {Code}", machineCode); }
    }

    private async Task PublishCommandAsync(string machineCode, DispenseCommandMessage m)
    {
        try { await _notifier.PublishDispenseCommandAsync(machineCode, m); }
        catch (Exception ex) { _log.LogWarning(ex, "Không publish được dispense/command của máy {Code}", machineCode); }
    }

    private async Task PublishTraysAsync(int machineId)
    {
        try { await _notifier.PublishTraysAsync(machineId); }
        catch (Exception ex) { _log.LogWarning(ex, "Không publish được trays của máy {MachineId}", machineId); }
    }
}