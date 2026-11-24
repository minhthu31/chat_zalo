namespace Chat.Client.Services;

// Đổi thời gian UTC của backend (createdAt) sang giờ địa phương của người dùng để hiển thị.
// Hợp đồng Sprint 3: backend luôn trả UTC, FE chịu trách nhiệm hiển thị theo múi giờ trình duyệt.
public static class LocalTime
{
    // DateTimeOffset.ToLocalTime() dùng múi giờ của trình duyệt (máy người dùng)
    public static DateTime ToLocal(DateTimeOffset utc) => utc.ToLocalTime().DateTime;

    public static string Time(DateTime local) => local.ToString("HH:mm");

    // Dùng làm gợi ý khi rê chuột vào giờ: đầy đủ ngày, giờ, giây
    public static string Full(DateTime local) => local.ToString("dd/MM/yyyy HH:mm:ss");

    // Nhãn của vạch ngăn giữa các ngày trong cuộc trò chuyện: Hôm nay, Hôm qua hoặc ngày đầy đủ
    public static string DayLabel(DateOnly day, DateOnly today)
    {
        if (day == today) return "Hôm nay";
        if (day == today.AddDays(-1)) return "Hôm qua";
        return day.ToString("dd/MM/yyyy");
    }
}
