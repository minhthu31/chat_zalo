namespace Chat.Client.Services;

// Đổi mã lỗi (code) của backend thành câu thông báo cho người dùng. Luôn dựa vào code, không dựa vào message.
public static class GroupErrors
{
    public static string ToMessage(string code) => code switch
    {
        "VALIDATION_ERROR" => "Dữ liệu chưa hợp lệ (tên nhóm không để trống, tối đa 100 ký tự, cần chọn ít nhất 1 bạn bè không trùng).",
        "NOT_FRIEND" => "Bạn chỉ có thể thêm người đã là bạn bè của mình.",
        "ALREADY_GROUP_MEMBER" => "Người này đã ở trong nhóm.",
        "GROUP_ACCESS_DENIED" => "Bạn không có quyền thực hiện thao tác này trong nhóm.",
        "NOT_FOUND" => "Không tìm thấy nhóm hoặc người dùng.",
        "INVALID_CONVERSATION_KIND" => "Hội thoại này không phải là nhóm.",
        "ADMIN_TRANSFER_PENDING" => "Nhóm đang có một đề nghị chuyển Admin chờ phản hồi.",
        "ADMIN_TRANSFER_ALREADY_RESOLVED" => "Đề nghị chuyển Admin này đã được xử lý rồi.",
        "UNAUTHORIZED" => "Bạn cần đăng nhập.",
        "INVALID_CSRF_TOKEN" => "Phiên làm việc hết hạn, vui lòng tải lại trang.",
        "NETWORK_ERROR" => "Không kết nối được máy chủ.",
        _ => "Có lỗi xảy ra, vui lòng thử lại."
    };
}
