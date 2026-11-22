using System.ComponentModel.DataAnnotations;

namespace Server.Models;

// Một khay cần châm (dòng truy vấn)
public class RestockCandidate
{
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public int TrayId { get; set; }
    public string TrayCode { get; set; } = "";
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public int Capacity { get; set; }
}

public class RestockSuggestionTray
{
    public int TrayId { get; set; }
    public string TrayCode { get; set; } = "";
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public int Capacity { get; set; }
    public int PlannedQty { get; set; }
}

public class RestockSuggestion
{
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public int OutOfStockTrays { get; set; }
    public int LowStockTrays { get; set; }
    public List<RestockSuggestionTray> Trays { get; set; } = new();
}

public class StaffInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
}

public class RestockTicketListItem
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = "";
    public int? StaffId { get; set; }
    public string? StaffName { get; set; }
    public string Status { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ItemCount { get; set; }
}

public class RestockItemView
{
    public int TrayId { get; set; }
    public string TrayCode { get; set; } = "";
    public string? ProductName { get; set; }
    public int PlannedQty { get; set; }
    public int? ActualQty { get; set; }
    public int Quantity { get; set; }   // số chai hiện có
    public int Capacity { get; set; }
}

public class RestockTicketDetail : RestockTicketListItem
{
    public List<RestockItemView> Items { get; set; } = new();
}

public class TrayFull
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string Code { get; set; } = "";
    public int Quantity { get; set; }
    public int Capacity { get; set; }
}

public class TicketFilter
{
    public string? Status { get; set; }
    public int? MachineId { get; set; }
    public int? StaffId { get; set; }
}

public class CreateTicketRequest
{
    [Required(ErrorMessage = "Thiếu máy cần tiếp hàng")]
    public int? MachineId { get; set; }

    public int? StaffId { get; set; }

    [StringLength(255, ErrorMessage = "Ghi chú tối đa 255 ký tự")]
    public string? Note { get; set; }
}

public class ConfirmItemRequest
{
    [Required(ErrorMessage = "Thiếu mã khay")]
    public int? TrayId { get; set; }

    [Required(ErrorMessage = "Thiếu số chai đã châm")]
    [Range(0, 30, ErrorMessage = "Số chai đã châm phải từ 0 đến 30")]
    public int? ActualQty { get; set; }
}

public class ConfirmTicketRequest
{
    [Required(ErrorMessage = "Thiếu danh sách khay")]
    [MinLength(1, ErrorMessage = "Danh sách khay không được rỗng")]
    public List<ConfirmItemRequest>? Items { get; set; }
}