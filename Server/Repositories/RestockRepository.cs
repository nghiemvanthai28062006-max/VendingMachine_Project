using Dapper;
using MySqlConnector;
using Server.Models;

namespace Server.Repositories;

public class RestockRepository
{
    private const string TicketSelect = @"
        SELECT rt.id, rt.machine_id, m.code AS machine_code, rt.staff_id, u.full_name AS staff_name,
               rt.status, rt.note, rt.created_at, rt.completed_at,
               (SELECT COUNT(*) FROM restock_ticket_items i WHERE i.ticket_id = rt.id) AS item_count
          FROM restock_tickets rt
          JOIN machines m ON m.id = rt.machine_id
          LEFT JOIN users u ON u.id = rt.staff_id";

    // ---------- đọc ----------
    // Khay có sản phẩm, quantity <= ngưỡng và còn chỗ chứa (BR-13). machineId = null: mọi máy.
    public Task<IEnumerable<RestockCandidate>> GetCandidatesAsync(MySqlConnection c, MySqlTransaction? tx, int? machineId) =>
        c.QueryAsync<RestockCandidate>(
            @"SELECT t.machine_id, m.code AS machine_code, t.id AS tray_id, t.code AS tray_code,
                     p.name AS product_name, t.quantity, t.capacity
                FROM trays t
                JOIN machines m ON m.id = t.machine_id
                JOIN products p ON p.id = t.product_id
               WHERE t.quantity <= t.low_stock_threshold AND t.capacity > t.quantity
                 AND (@machineId IS NULL OR t.machine_id = @machineId)
               ORDER BY t.machine_id, t.code",
            new { machineId }, tx);

    public Task<IEnumerable<StaffInfo>> ListStaffAsync(MySqlConnection c) =>
        c.QueryAsync<StaffInfo>(
            "SELECT id, username, full_name FROM users WHERE role = 'STAFF' AND is_active = 1 ORDER BY full_name");

    public Task<User?> GetUserAsync(MySqlConnection c, int id) =>
        c.QuerySingleOrDefaultAsync<User>(
            "SELECT id, username, full_name, role, is_active FROM users WHERE id = @id", new { id });

    public Task<RestockTicketDetail?> GetTicketAsync(MySqlConnection c, MySqlTransaction? tx, int id) =>
        c.QuerySingleOrDefaultAsync<RestockTicketDetail>($"{TicketSelect} WHERE rt.id = @id", new { id }, tx);

    public Task<IEnumerable<RestockItemView>> GetItemsAsync(MySqlConnection c, MySqlTransaction? tx, int ticketId) =>
        c.QueryAsync<RestockItemView>(
            @"SELECT i.tray_id, t.code AS tray_code, p.name AS product_name, i.planned_qty, i.actual_qty,
                     t.quantity, t.capacity
                FROM restock_ticket_items i
                JOIN trays t ON t.id = i.tray_id
                LEFT JOIN products p ON p.id = t.product_id
               WHERE i.ticket_id = @ticketId
               ORDER BY t.code",
            new { ticketId }, tx);

    public async Task<List<RestockTicketListItem>> ListTicketsAsync(MySqlConnection c, TicketFilter f)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(f.Status)) { where.Add("rt.status = @status"); p.Add("status", f.Status); }
        if (f.MachineId != null) { where.Add("rt.machine_id = @machineId"); p.Add("machineId", f.MachineId); }
        if (f.StaffId != null) { where.Add("rt.staff_id = @staffId"); p.Add("staffId", f.StaffId); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var rows = await c.QueryAsync<RestockTicketListItem>($"{TicketSelect} {whereSql} ORDER BY rt.id DESC", p);
        return rows.ToList();
    }

    // ---------- ghi ----------
    public Task<int> InsertTicketAsync(MySqlConnection c, MySqlTransaction tx, int machineId, int? staffId,
        string? note, DateTimeOffset now) =>
        c.ExecuteScalarAsync<int>(
            @"INSERT INTO restock_tickets (machine_id, staff_id, status, note, created_at)
              VALUES (@machineId, @staffId, 'OPEN', @note, @now);
              SELECT LAST_INSERT_ID();",
            new { machineId, staffId, note, now }, tx);

    public Task<int> InsertItemAsync(MySqlConnection c, MySqlTransaction tx, int ticketId, int trayId, int plannedQty) =>
        c.ExecuteAsync(
            "INSERT INTO restock_ticket_items (ticket_id, tray_id, planned_qty) VALUES (@ticketId, @trayId, @plannedQty)",
            new { ticketId, trayId, plannedQty }, tx);

    // Các chuyển trạng thái: luôn kèm điều kiện trạng thái cũ, trả số dòng bị tác động (phải bằng 1)
    public Task<int> StartAsync(MySqlConnection c, int id) =>
        c.ExecuteAsync("UPDATE restock_tickets SET status = 'IN_PROGRESS' WHERE id = @id AND status = 'OPEN'", new { id });

    public Task<int> CancelAsync(MySqlConnection c, int id) =>
        c.ExecuteAsync(
            "UPDATE restock_tickets SET status = 'CANCELLED' WHERE id = @id AND status IN ('OPEN','IN_PROGRESS')", new { id });

    public Task<int> CompleteAsync(MySqlConnection c, MySqlTransaction tx, int id, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"UPDATE restock_tickets SET status = 'DONE', completed_at = @now
               WHERE id = @id AND status IN ('OPEN','IN_PROGRESS')",
            new { id, now }, tx);

    // KHÓA dòng khay trước khi cộng tồn kho
    public Task<TrayFull?> LockTrayAsync(MySqlConnection c, MySqlTransaction tx, int trayId) =>
        c.QuerySingleOrDefaultAsync<TrayFull>(
            "SELECT id, machine_id, code, quantity, capacity FROM trays WHERE id = @trayId FOR UPDATE",
            new { trayId }, tx);

    // Chỉ cộng khi không vượt sức chứa; trả 0 nếu vượt
    public Task<int> AddQuantityAsync(MySqlConnection c, MySqlTransaction tx, int trayId, int add) =>
        c.ExecuteAsync(
            "UPDATE trays SET quantity = quantity + @add WHERE id = @trayId AND quantity + @add <= capacity",
            new { trayId, add }, tx);

    public Task<int> SetActualAsync(MySqlConnection c, MySqlTransaction tx, int ticketId, int trayId, int actual) =>
        c.ExecuteAsync(
            "UPDATE restock_ticket_items SET actual_qty = @actual WHERE ticket_id = @ticketId AND tray_id = @trayId",
            new { ticketId, trayId, actual }, tx);

    // BR-14: đóng cảnh báo hết/sắp hết của khay vừa châm
    public Task<int> ResolveStockAlertsAsync(MySqlConnection c, MySqlTransaction tx, int trayId, int userId, DateTimeOffset now) =>
        c.ExecuteAsync(
            @"UPDATE alerts SET is_resolved = 1, resolved_at = @now, resolved_by = @userId
               WHERE tray_id = @trayId AND type IN ('LOW_STOCK','OUT_OF_STOCK') AND is_resolved = 0",
            new { trayId, userId, now }, tx);
}