using Dapper;
using MySqlConnector;
using Server.Models;

namespace Server.Repositories;

// Mọi hàm nhận connection và transaction từ Service (Service gom nhiều bước vào một transaction)
public class OrderRepository
{
    private const string OrderSelect = @"
        SELECT o.id, o.request_id, o.machine_id, o.tray_id, o.product_id, o.amount, o.status,
               o.dispense_request_id, o.created_at, o.expires_at, o.paid_at, o.dispense_started_at, o.completed_at,
               m.code AS machine_code, t.code AS tray_code
          FROM orders o
          JOIN machines m ON m.id = o.machine_id
          JOIN trays t ON t.id = o.tray_id";

    // ---------- đọc ----------
    public Task<MachineLite?> GetMachineByCodeAsync(MySqlConnection c, MySqlTransaction? tx, string code) =>
        c.QuerySingleOrDefaultAsync<MachineLite>(
            "SELECT id, code, status FROM machines WHERE code = @code", new { code }, tx);

    // KHÓA dòng khay: các lần tạo đơn cùng khay xếp hàng (File 02, mục 2)
    public Task<TrayLock?> LockTrayByCodeAsync(MySqlConnection c, MySqlTransaction tx, int machineId, string trayCode) =>
        c.QuerySingleOrDefaultAsync<TrayLock>(
            @"SELECT id, machine_id, code, product_id, quantity, low_stock_threshold
                FROM trays WHERE machine_id = @machineId AND code = @trayCode FOR UPDATE",
            new { machineId, trayCode }, tx);

    public Task<TrayLock?> LockTrayByIdAsync(MySqlConnection c, MySqlTransaction tx, int trayId) =>
        c.QuerySingleOrDefaultAsync<TrayLock>(
            @"SELECT id, machine_id, code, product_id, quantity, low_stock_threshold
                FROM trays WHERE id = @trayId FOR UPDATE", new { trayId }, tx);

    public Task<ProductLite?> GetProductAsync(MySqlConnection c, MySqlTransaction? tx, int id) =>
        c.QuerySingleOrDefaultAsync<ProductLite>(
            "SELECT id, name, price, is_active FROM products WHERE id = @id", new { id }, tx);

    public Task<int> CountHoldsAsync(MySqlConnection c, MySqlTransaction tx, int trayId, DateTimeOffset now) =>
        c.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM orders
               WHERE tray_id = @trayId
                 AND (status IN ('PAID','DISPENSING')
                      OR (status = 'PENDING_PAYMENT' AND expires_at > @now))",
            new { trayId, now }, tx);

    public Task<int> CountJamAsync(MySqlConnection c, MySqlTransaction tx, int trayId) =>
        c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM alerts WHERE tray_id = @trayId AND type = 'JAMMED' AND is_resolved = 0",
            new { trayId }, tx);

    public Task<Order?> GetByRequestIdAsync(MySqlConnection c, MySqlTransaction tx, string requestId) =>
        c.QuerySingleOrDefaultAsync<Order>($"{OrderSelect} WHERE o.request_id = @requestId", new { requestId }, tx);

    public Task<Order?> GetByIdAsync(MySqlConnection c, MySqlTransaction tx, int id) =>
        c.QuerySingleOrDefaultAsync<Order>($"{OrderSelect} WHERE o.id = @id", new { id }, tx);

    public Task<PaymentRow?> GetPaymentByOrderAsync(MySqlConnection c, MySqlTransaction tx, int orderId) =>
        c.QuerySingleOrDefaultAsync<PaymentRow>(
            "SELECT id, order_id, transaction_code, status FROM payments WHERE order_id = @orderId ORDER BY id DESC LIMIT 1",
            new { orderId }, tx);

    public Task<int> CountTransactionUsedByOtherAsync(MySqlConnection c, MySqlTransaction tx, string code, int orderId) =>
        c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM payments WHERE transaction_code = @code AND order_id <> @orderId",
            new { code, orderId }, tx);

    // ---------- ghi ----------
    public Task<int> InsertOrderAsync(MySqlConnection c, MySqlTransaction tx, string requestId, int machineId,
        int trayId, int productId, int amount, DateTimeOffset now, DateTimeOffset expiresAt) =>
        c.ExecuteScalarAsync<int>(
            @"INSERT INTO orders (request_id, machine_id, tray_id, product_id, amount, status, created_at, expires_at)
              VALUES (@requestId, @machineId, @trayId, @productId, @amount, 'PENDING_PAYMENT', @now, @expiresAt);
              SELECT LAST_INSERT_ID();",
            new { requestId, machineId, trayId, productId, amount, now, expiresAt }, tx);

    public Task<int> InsertPaymentAsync(MySqlConnection c, MySqlTransaction tx, int orderId, int amount, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"INSERT INTO payments (order_id, method, amount, status, created_at)
              VALUES (@orderId, 'QR_SIM', @amount, 'PENDING', @now)",
            new { orderId, amount, now }, tx);

    public Task<int> UpdatePaymentAsync(MySqlConnection c, MySqlTransaction tx, int orderId, string status,
        string? transactionCode, DateTimeOffset? paidAt) =>
        c.ExecuteAsync(
            @"UPDATE payments SET status = @status, transaction_code = @transactionCode, paid_at = @paidAt
               WHERE order_id = @orderId AND status = 'PENDING'",
            new { orderId, status, transactionCode, paidAt }, tx);

    // Các chuyển trạng thái: luôn kèm "AND status = <cũ>", trả số dòng bị tác động (phải = 1)
    public Task<int> MarkPaidAsync(MySqlConnection c, MySqlTransaction tx, int id, DateTimeOffset now) =>
        c.ExecuteAsync("UPDATE orders SET status = 'PAID', paid_at = @now WHERE id = @id AND status = 'PENDING_PAYMENT'",
            new { id, now }, tx);

    public Task<int> MarkPaymentFailedAsync(MySqlConnection c, MySqlTransaction tx, int id) =>
        c.ExecuteAsync("UPDATE orders SET status = 'PAYMENT_FAILED' WHERE id = @id AND status = 'PENDING_PAYMENT'",
            new { id }, tx);

    public Task<int> MarkExpiredAsync(MySqlConnection c, MySqlTransaction tx, int id) =>
        c.ExecuteAsync("UPDATE orders SET status = 'EXPIRED' WHERE id = @id AND status = 'PENDING_PAYMENT'",
            new { id }, tx);

    public Task<int> MarkDispensingAsync(MySqlConnection c, MySqlTransaction tx, int id, DateTimeOffset now) =>
        c.ExecuteAsync(
            "UPDATE orders SET status = 'DISPENSING', dispense_started_at = @now WHERE id = @id AND status = 'PAID'",
            new { id, now }, tx);

    public Task<int> MarkCompletedAsync(MySqlConnection c, MySqlTransaction tx, int id, string dispenseRequestId, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"UPDATE orders SET status = 'COMPLETED', completed_at = @now, dispense_request_id = @dispenseRequestId
               WHERE id = @id AND status = 'DISPENSING'",
            new { id, dispenseRequestId, now }, tx);

    public Task<int> DecrementTrayAsync(MySqlConnection c, MySqlTransaction tx, int trayId) =>
        c.ExecuteAsync("UPDATE trays SET quantity = quantity - 1 WHERE id = @trayId AND quantity > 0",
            new { trayId }, tx);

    // Không tạo trùng khi khay đã có cảnh báo cùng loại chưa xử lý (BR-11)
    public Task<int> InsertAlertIfAbsentAsync(MySqlConnection c, MySqlTransaction tx, int machineId, int trayId,
        string type, string message, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"INSERT INTO alerts (machine_id, tray_id, type, message, created_at)
              SELECT @machineId, @trayId, @type, @message, @now FROM DUAL
               WHERE NOT EXISTS (SELECT 1 FROM alerts
                                  WHERE tray_id = @trayId AND type = @type AND is_resolved = 0)",
            new { machineId, trayId, type, message, now }, tx);

    // ---------- M3: dọn đơn quá hạn và hoàn tiền (BR-10, BR-20) ----------
    // FOR UPDATE OF o: khóa các đơn ứng viên để callback/result không chen ngang.
    // orderId = null: quét tất cả; có giá trị: chỉ kiểm tra đơn đó.
    public Task<IEnumerable<StaleOrder>> GetStaleOrdersAsync(MySqlConnection c, MySqlTransaction tx,
        DateTimeOffset now, DateTimeOffset paidCutoff, DateTimeOffset dispenseCutoff, int? orderId) =>
        c.QueryAsync<StaleOrder>(
            @"SELECT o.id AS order_id, o.machine_id, m.code AS machine_code, o.status, o.amount
                FROM orders o JOIN machines m ON m.id = o.machine_id
               WHERE ((o.status = 'PENDING_PAYMENT' AND o.expires_at <= @now)
                   OR (o.status = 'PAID' AND o.paid_at <= @paidCutoff)
                   OR (o.status = 'DISPENSING' AND o.dispense_started_at <= @dispenseCutoff))
                 AND (@orderId IS NULL OR o.id = @orderId)
                 FOR UPDATE OF o",
            new { now, paidCutoff, dispenseCutoff, orderId }, tx);

    public Task<int> FailPendingPaymentAsync(MySqlConnection c, MySqlTransaction tx, int orderId) =>
        c.ExecuteAsync("UPDATE payments SET status = 'FAILED' WHERE order_id = @orderId AND status = 'PENDING'",
            new { orderId }, tx);

    public Task<int> MarkDispenseFailedAsync(MySqlConnection c, MySqlTransaction tx, int id,
        string fromStatus, string? dispenseRequestId) =>
        c.ExecuteAsync(
            @"UPDATE orders SET status = 'DISPENSE_FAILED',
                                dispense_request_id = COALESCE(@dispenseRequestId, dispense_request_id)
               WHERE id = @id AND status = @fromStatus",
            new { id, fromStatus, dispenseRequestId }, tx);

    // Hoàn tiền giả lập luôn thành công nên ghi COMPLETED ngay
    public Task<int> InsertRefundAsync(MySqlConnection c, MySqlTransaction tx, int orderId, int paymentId,
        int amount, string reason, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"INSERT INTO refunds (order_id, payment_id, amount, reason, status, created_at, completed_at)
              VALUES (@orderId, @paymentId, @amount, @reason, 'COMPLETED', @now, @now)",
            new { orderId, paymentId, amount, reason, now }, tx);

    public Task<int> MarkRefundedAsync(MySqlConnection c, MySqlTransaction tx, int id) =>
        c.ExecuteAsync("UPDATE orders SET status = 'REFUNDED' WHERE id = @id AND status = 'DISPENSE_FAILED'",
            new { id }, tx);

    // ---------- REST tra cứu ----------
    public async Task<(List<OrderListItem> Items, long Total)> SearchAsync(MySqlConnection c, OrderFilter f)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (f.From != null) { where.Add("o.created_at >= @fromDate"); p.Add("fromDate", f.From.Value.Date); }
        if (f.To != null) { where.Add("o.created_at < @toExclusive"); p.Add("toExclusive", f.To.Value.Date.AddDays(1)); }
        if (!string.IsNullOrEmpty(f.Status)) { where.Add("o.status = @status"); p.Add("status", f.Status); }
        if (f.MachineId != null) { where.Add("o.machine_id = @machineId"); p.Add("machineId", f.MachineId); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var total = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM orders o {whereSql}", p);

        p.Add("pageSize", f.PageSize);
        p.Add("offset", (f.Page - 1) * f.PageSize);
        var items = await c.QueryAsync<OrderListItem>(
            $@"SELECT o.id, m.code AS machine_code, t.code AS tray_code, pr.name AS product_name,
                      o.amount, o.status, o.created_at, o.paid_at, o.completed_at
                 FROM orders o
                 JOIN machines m ON m.id = o.machine_id
                 JOIN trays t ON t.id = o.tray_id
                 JOIN products pr ON pr.id = o.product_id
                 {whereSql}
                ORDER BY o.id DESC
                LIMIT @pageSize OFFSET @offset", p);
        return (items.ToList(), total);
    }

    public async Task<OrderDetail?> GetDetailAsync(MySqlConnection c, int id)
    {
        var d = await c.QuerySingleOrDefaultAsync<OrderDetail>(
            @"SELECT o.id, m.code AS machine_code, t.code AS tray_code, pr.name AS product_name,
                     o.amount, o.status, o.created_at, o.expires_at, o.paid_at,
                     o.dispense_started_at, o.completed_at
                FROM orders o
                JOIN machines m ON m.id = o.machine_id
                JOIN trays t ON t.id = o.tray_id
                JOIN products pr ON pr.id = o.product_id
               WHERE o.id = @id", new { id });
        if (d is null) return null;

        d.Payment = await c.QuerySingleOrDefaultAsync<PaymentInfo>(
            "SELECT status, method, transaction_code, amount FROM payments WHERE order_id = @id ORDER BY id DESC LIMIT 1", new { id });
        d.Refund = await c.QuerySingleOrDefaultAsync<RefundInfo>(
            "SELECT status, amount, reason FROM refunds WHERE order_id = @id ORDER BY id DESC LIMIT 1", new { id });
        return d;
    }
}