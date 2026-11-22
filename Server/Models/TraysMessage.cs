namespace Server.Models;

// Một dòng truy vấn từ DB (tên cột snake_case tự ánh xạ vào đây)
public class TrayState
{
    public string TrayCode { get; set; } = "";
    public int Quantity { get; set; }
    public string? ProductName { get; set; }
    public int? VolumeMl { get; set; }
    public int? Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool? ProductActive { get; set; }
    public int Holds { get; set; }      // số đơn đang giữ chỗ khay (BR-03)
    public int JamCount { get; set; }   // số cảnh báo JAMMED chưa xử lý (BR-04)
}

// Nội dung message vending/{m}/trays (API.md mục A3)
public class TraysMessage
{
    public string MachineCode { get; set; } = "";
    public string MachineStatus { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TrayItem> Trays { get; set; } = new();
}

public class TrayItem
{
    public string TrayCode { get; set; } = "";
    public TrayProductInfo? Product { get; set; }
    public int Quantity { get; set; }
    public bool Available { get; set; }
}

public class TrayProductInfo
{
    public string Name { get; set; } = "";
    public int VolumeMl { get; set; }
    public int Price { get; set; }
    public string? ImageUrl { get; set; }
}