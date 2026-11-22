namespace Chat.Client.Services;

// Đổi mã lỗi (code) của backend khi tải/gửi tệp thành câu thông báo. Luôn dựa vào code, không dựa vào message.
public static class AttachmentErrors
{
    public static string ToMessage(string code) => code switch
    {
        "FILE_TOO_LARGE" => "Một tệp quá lớn (tối đa 10 MB mỗi tệp).",
        "MESSAGE_ATTACHMENTS_TOO_LARGE" => "Tổng dung lượng các tệp quá lớn (tối đa 50 MB mỗi tin).",
        "UNSUPPORTED_FILE_TYPE" => "Loại tệp không được hỗ trợ (chỉ nhận JPG, PNG, WebP, PDF, DOCX, XLSX, TXT).",
        "INVALID_ATTACHMENT_COUNT" => "Số tệp mỗi tin phải từ 1 đến 5.",
        "ATTACHMENT_NOT_FOUND" => "Một tệp đã tải lên không còn tồn tại, vui lòng gửi lại.",
        "ATTACHMENT_ALREADY_USED" => "Một tệp đã được dùng cho tin khác, vui lòng chọn lại.",
        "CLIENT_MESSAGE_ID_CONFLICT" => "Tin này đã được gửi với nội dung khác, vui lòng thử lại.",
        "CONVERSATION_ACCESS_DENIED" => "Bạn không có quyền gửi tệp trong hội thoại này (có thể một trong hai người đã chặn người kia).",
        "GROUP_ACCESS_DENIED" => "Bạn không còn quyền trong nhóm này.",
        "VALIDATION_ERROR" => "Dữ liệu gửi không hợp lệ, vui lòng thử lại.",
        "UNAUTHORIZED" => "Bạn cần đăng nhập.",
        "INVALID_CSRF_TOKEN" => "Phiên làm việc hết hạn, vui lòng tải lại trang.",
        "NETWORK_ERROR" => "Không kết nối được máy chủ. Nội dung được giữ lại, bạn có thể gửi lại.",
        _ => "Không gửi được tệp, vui lòng thử lại."
    };
}
