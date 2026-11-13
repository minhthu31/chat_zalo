namespace Chat.Client.Services;

// Đổi mã lỗi (code) của backend thành câu thông báo cho người dùng. Luôn dựa vào code, không dựa vào message.
public static class FriendshipErrors
{
    public static string ToMessage(string code) => code switch
    {
        "FRIEND_REQUEST_ALREADY_EXISTS" => "Đã có lời mời kết bạn đang chờ.",
        "ALREADY_FRIENDS" => "Hai bạn đã là bạn bè.",
        "SELF_FRIEND_REQUEST_NOT_ALLOWED" => "Bạn không thể kết bạn với chính mình.",
        "USER_NOT_FOUND" => "Không tìm thấy người dùng.",
        "FRIENDSHIP_NOT_FOUND" => "Lời mời hoặc quan hệ bạn bè không còn tồn tại.",
        "FRIEND_REQUEST_NOT_PENDING" or "FRIEND_REQUEST_STATE_CONFLICT" or "FRIENDSHIP_STATE_CONFLICT"
            => "Trạng thái đã thay đổi, vui lòng tải lại.",
        "FRIEND_REQUEST_RECIPIENT_ONLY" or "FRIEND_REQUEST_SENDER_ONLY"
            => "Bạn không có quyền thực hiện thao tác này.",
        "SELF_BLOCK_NOT_ALLOWED" => "Bạn không thể tự chặn chính mình.",
        "NOT_FOUND" => "Không tìm thấy người dùng.",
        "UNAUTHORIZED" => "Bạn cần đăng nhập.",
        "INVALID_CSRF_TOKEN" => "Phiên làm việc hết hạn, vui lòng tải lại trang.",
        "NETWORK_ERROR" => "Không kết nối được máy chủ.",
        _ => "Có lỗi xảy ra, vui lòng thử lại."
    };
}
