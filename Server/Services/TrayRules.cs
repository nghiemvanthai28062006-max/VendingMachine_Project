namespace Server.Services;

public static class TrayRules
{
    // BR-03 và BR-04: khay bán được khi máy ACTIVE, sản phẩm đang bán,
    // còn chai khả dụng (trừ đơn giữ chỗ) và không có cảnh báo kẹt chưa xử lý
    public static bool IsAvailable(string machineStatus, bool? productActive, int quantity, int holds, int jamCount) =>
        machineStatus == "ACTIVE" && productActive == true && quantity - holds > 0 && jamCount == 0;
}