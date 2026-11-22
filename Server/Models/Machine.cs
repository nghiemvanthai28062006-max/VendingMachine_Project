using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class Machine
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset? LastHeartbeat { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// Dạng trả về cho App: thêm Connection (tính ra, không lưu DB)
public class MachineView
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset? LastHeartbeat { get; set; }
    public string Connection { get; set; } = "";
}

public class CreateMachineRequest
{
    [Required(ErrorMessage = "Mã máy không được để trống")]
    [StringLength(20, ErrorMessage = "Mã máy tối đa 20 ký tự")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Tên máy không được để trống")]
    [StringLength(100, ErrorMessage = "Tên máy tối đa 100 ký tự")]
    public string Name { get; set; } = "";

    [StringLength(200, ErrorMessage = "Vị trí tối đa 200 ký tự")]
    public string? Location { get; set; }
}

public class UpdateMachineRequest
{
    [Required(ErrorMessage = "Tên máy không được để trống")]
    [StringLength(100, ErrorMessage = "Tên máy tối đa 100 ký tự")]
    public string Name { get; set; } = "";

    [StringLength(200, ErrorMessage = "Vị trí tối đa 200 ký tự")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Trạng thái không được để trống")]
    [RegularExpression("^(ACTIVE|MAINTENANCE)$", ErrorMessage = "Trạng thái chỉ nhận ACTIVE hoặc MAINTENANCE")]
    public string Status { get; set; } = "";
}