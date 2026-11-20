# CHAT-24 — Upload/Download và storage dùng chung

## API thật

- `POST /api/conversations/{id}/attachments`: Cookie + `X-CSRF-TOKEN`, multipart có đúng một trường file tên `file`; thành công `201 AttachmentDto`, trạng thái metadata `Pending`.
- `GET /api/attachments/{attachmentId}/content`: Cookie; stream nội dung với MIME đã kiểm tra.
- `GET /api/attachments/{attachmentId}/download`: Cookie; cùng kiểm tra quyền như content, trả file dưới dạng attachment.
- DTO giữ nguyên Sprint 3: `id`, `fileName`, `contentType`, `sizeBytes`, `contentUrl`, `downloadUrl`. Không trả `StorageKey`, đường dẫn đĩa hoặc nội dung Identity.

Giới hạn chính xác 10.485.760 byte/file. Giới hạn request là 11 MiB để chứa multipart headers; service vẫn kiểm tra từng byte đọc và giới hạn file 10 MiB. Một request upload một file; giới hạn 5 file/50 MiB mỗi tin thuộc CHAT-26 khi gắn vào Messages.

Các loại cho phép: JPG/JPEG, PNG, WebP, PDF, DOCX, XLSX, TXT UTF-8. Không tin multipart Content-Type. Kiểm tra chữ ký/cấu trúc ảnh, PDF, ZIP/OpenXML và văn bản; trả MIME chuẩn theo nội dung. Không nhận macro-enabled Office. Đây là kiểm tra loại/cấu trúc file, không phải dịch vụ quét malware.

Lỗi giữ `{code,message}`: `400 VALIDATION_ERROR`/`INVALID_CSRF_TOKEN`, `401 UNAUTHORIZED`, `403` theo quyền Direct/Group, `404 ATTACHMENT_NOT_FOUND`/`NOT_FOUND`, `413 FILE_TOO_LARGE`, `415 UNSUPPORTED_FILE_TYPE`. Lỗi không dự kiến ghi ILogger và trả `500 INTERNAL_ERROR`, không trả exception/đường dẫn cho client.

## Quyền và phối hợp với các task sau

- Upload Direct: còn là bạn bè Accepted, hai phía còn membership và không chặn hai chiều, giống gửi Messages Sprint 1.
- Upload Group: dùng `IGroupPermissionService.CanSendMessageAsync`; Blocks trong nhóm không cấm thành viên gửi/upload.
- Pending: chỉ người upload, còn quyền hội thoại, được xem/tải.
- Attached: kiểm tra membership, `JoinedSeq`, trạng thái nhóm và Blocks theo người gửi tin. Direct giữ đúng quy tắc đọc Sprint 1: người chủ động chặn vẫn đọc được lịch sử; người bị chặn không được đọc.
- Mỗi lần content/download đều kiểm tra lại quyền. Response có `Cache-Control: private, no-store` và `X-Content-Type-Options: nosniff`.
- CHAT-25 Avatar dùng nguyên `IFileStorage.WriteAsync/OpenReadAsync/DeleteAsync`; tự kiểm tra ảnh và giới hạn 2 MiB trong Avatar service. Không dùng API Attachments để đọc Avatar.
- CHAT-26 dùng `IAttachmentService`/StoredFiles, kiểm tra uploader, conversation và Pending, rồi đổi thành Attached và thêm MessageAttachments trong cùng transaction lưu tin. Việc upload không tạo Messages và không phát event.
- Không thay đổi schema, interface storage hoặc API Messages/Avatar đã chốt trong hợp đồng Sprint 3.

## Cấu hình và DI

`backend/Chat.Api/Program.cs` đăng ký `IFileStorage -> LocalFileStorage`, `IAttachmentService -> AttachmentService`, `IPendingFileCleanupService -> PendingFileCleanupService`.

Đặt `FileStorage:RootPath` bằng cấu hình local hoặc biến môi trường `FileStorage__RootPath`. Dùng đường dẫn tuyệt đối riêng tư, ngoài `wwwroot`; khi dùng container phải mount thư mục/volume có quyền ghi cho user chạy API để giữ file sau khi tạo lại container. Mặc định dùng thư mục `chat_zalo/private-files` trong LocalApplicationData của tài khoản chạy server.

Không commit nội dung storage lên Git hoặc public thư mục bằng static-file middleware. Storage key do server tạo; tên file gốc chỉ dùng làm metadata/tên download. LocalFileStorage ghi file tạm rồi đổi tên nguyên tử, không ghi đè key đã tồn tại, từ chối đường dẫn tuyệt đối, traversal và symlink.

## Dọn Pending thủ công

Chạy trong môi trường server với connection string và FileStorage:RootPath đúng của ứng dụng:

```powershell
dotnet run --project backend/Chat.Api -- --cleanup-pending=true --cleanup-before=2026-11-01T00:00:00Z --cleanup-limit=100
```

Thay cutoff bằng thời điểm do người vận hành chọn; không có lịch xóa tự động 24 giờ. Chế độ này không mở HTTP server. Mỗi lần xử lý tối đa 1.000 file, mặc định 100. Khóa từng row PostgreSQL và kiểm tra lại trước khi xóa; chỉ xóa Pending cũ không có liên kết Messages hoặc Avatar. Không xóa Attached, Avatar Active/đang sử dụng. File đã mất trên đĩa được xử lý idempotent.

## Kiểm chứng

Test HTTP dùng Kestrel, Identity Cookie/CSRF, PostgreSQL và LocalFileStorage thật trong thư mục test riêng. Các trường hợp gồm quyền Pending/Attached, Direct/Group, Blocks, rời/giải tán nhóm, giới hạn byte, MIME giả và dọn Pending. Test storage kiểm tra traversal, chống ghi đè và dùng chung key Avatar. Toàn bộ test dùng database `chat_zalo_test`, không chạy vào DB demo.

Tài liệu nền: [API_CONTRACT.md](API_CONTRACT.md), [DATABASE_RULES.md](../database/DATABASE_RULES.md).
