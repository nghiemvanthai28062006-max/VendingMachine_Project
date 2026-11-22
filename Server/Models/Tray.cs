using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class Tray
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public string Code { get; set; } = "";
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public int Capacity { get; set; }
    public int LowStockThreshold { get; set; }
}

public class CreateTrayRequest
{
    [Required(ErrorMessage = "Mã khay không được để trống")]
    [StringLength(10, ErrorMessage = "Mã khay tối đa 10 ký tự")]
    public string Code { get; set; } = "";

    public int? ProductId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số chai ban đầu không được âm")]
    public int Quantity { get; set; }

    [Range(1, 30, ErrorMessage = "Sức chứa phải từ 1 đến 30")]
    public int Capacity { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Ngưỡng cảnh báo không được âm")]
    public int LowStockThreshold { get; set; } = 2;
}

public class UpdateTrayRequest
{
    public int? ProductId { get; set; }

    [Range(1, 30, ErrorMessage = "Sức chứa phải từ 1 đến 30")]
    public int Capacity { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Ngưỡng cảnh báo không được âm")]
    public int LowStockThreshold { get; set; }
}