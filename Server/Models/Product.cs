using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int VolumeMl { get; set; }
    public int Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ProductRequest
{
    [Required(ErrorMessage = "Tên sản phẩm không được để trống")]
    [StringLength(100, ErrorMessage = "Tên sản phẩm tối đa 100 ký tự")]
    public string Name { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Dung tích phải lớn hơn 0")]
    public int VolumeMl { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Giá phải lớn hơn hoặc bằng 0")]
    public int Price { get; set; }

    [StringLength(255, ErrorMessage = "Đường dẫn ảnh tối đa 255 ký tự")]
    public string? ImageUrl { get; set; }
}

public class ActiveRequest
{
    [Required(ErrorMessage = "Thiếu trường isActive")]
    public bool? IsActive { get; set; }
}