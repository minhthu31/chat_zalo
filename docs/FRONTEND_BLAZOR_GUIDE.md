# FE CONTRACT – chat_zalo (Blazor WebAssembly)
**Phiên bản:** 1.2 – áp dụng P0; source FE là khung.

## 1. Kiến trúc và quy ước
- FE `frontend/Chat.Client.csproj` (.NET 10, Razor/C#); BE `backend/Chat.Api` (ASP.NET Core 10); DB PostgreSQL `chat_zalo`.
- FE chỉ kết nối `/api/*` bằng HttpClient và `/hubs/chat` bằng SignalR. FE **không truy cập PostgreSQL**, không chứa entity EF, secrets hoặc kết nối DB.
- `Pages/` routing; `Components/` UI tái sử dụng; `Services/` gọi HTTP/SignalR; `Models/` request/response; `State/` UI state; `wwwroot/` assets và cấu hình công khai.
- FE models dùng `Guid`, `long` cho seq, `DateTimeOffset` UTC; JSON camelCase và string enum. Không đổi property trên wire khi BE đã chốt.

## 2. Base URL và Cookie
- File `frontend/wwwroot/appsettings.json` hiện ghi `ApiBaseUrl = http://localhost:5080/` cho **local dev**; đây là cấu hình công khai, không lưu password/token.
- API calls phải dùng đường dẫn `api/...` từ base; Hub là `hubs/chat` từ cùng base. Không hardcode endpoint và domain mỗi trang.
- Khi FE/API khác origin, dùng `HttpClient` với `BrowserRequestCredentials.Include` theo cơ chế Blazor WASM; BE CORS allowlist origin FE, AllowCredentials, Cookie SameSite/Secure phù hợp. Trong HTTPS triển khai thực tế, ưu tiên cùng origin qua reverse proxy.
- Cookie HttpOnly không thể đọc trong C#. Không lưu password/cookie trong localStorage. Mọi request thay đổi trạng thái cần gửi CSRF token theo hợp đồng BE; chưa hoàn tất chống CSRF thì chưa được coi tích hợp auth xong.

## 3. Mapping và trách nhiệm
| FE route/component | Service client được giao | BE endpoint | Payload |
|---|---|---|---|
| Login/Register | AuthApiClient | `/api/auth/*` | RegisterRequest/LoginRequest → UserResponse |
| Profile | UsersApiClient | `/api/users/me` | UpdateProfileRequest → UserDto |
| UserSearch | UsersApiClient | `/api/users/search` | UserDto[] |
| Friendship UI | FriendshipApiClient | `/api/friendships*` | FriendshipDto, incoming/outgoing |
| Block UI | BlocksApiClient | `/api/blocks/{userId}` | 204 |
| Conversation UI | ConversationsApiClient | `/api/conversations*` | ConversationDto |
| Chat UI | MessagesApiClient | `/api/conversations/{id}/messages` | SendMessageRequest/MessageResponse |
| Chat realtime | ChatHubClient | `/hubs/chat` | ConversationChanged |

- FE giải mã lỗi `code`/`message`, xử lý status `200/201/204/400/401/403/404/409` và không đoán theo message string.
- Chat flow: POST message → Hub event → GET messages để đồng bộ; reconnect gọi lại GET bằng seq; đừng coi SignalR event là bản ghi tin nhắn.


## 4. Quy định tích hợp Frontend theo API Contract v1.2

### Authentication và CSRF

- Frontend sử dụng Blazor WebAssembly .NET 10.
- Sử dụng HttpClient với browser credentials `Include` khi FE và BE khác origin.
- Gọi `GET /api/auth/csrf` để lấy CSRF token.
- Gửi header `X-CSRF-TOKEN` cho POST, PUT, PATCH, DELETE.
- Lấy lại token khi trạng thái đăng nhập thay đổi.
- Không lưu mật khẩu, Cookie HTTP-only hoặc DB credentials trong Frontend.

### Messages

- `beforeSeq`: tải lịch sử tin nhắn cũ.
- `afterSeq`: lấy tin nhắn mới.
- Không truyền đồng thời hai tham số.
- Lưu Seq cuối cùng đã đồng bộ cho từng hội thoại.
- Xử lý tin nhắn trùng theo MessageId hoặc Seq.

### SignalR

- Kết nối Hub `/hubs/chat`.
- Nhận event `ConversationChanged`.
- Sau khi nhận event, gọi Messages API với `afterSeq`.
- Khi reconnect, tải tiếp tin nhắn còn thiếu.
- Không dùng SignalR làm nơi lưu dữ liệu.

### Giao diện

- Xây dựng Login, Register, Profile, UserSearch và Chat.
- Sử dụng các Razor Components tái sử dụng.
- Có trạng thái Loading, Empty, Error và Unauthorized.
- Có thể dùng mock data đúng DTO để phát triển giao diện.
- Phải phân biệt chức năng mock với chức năng đã tích hợp Backend thật.
- Không sử dụng ReactJS, JSX hoặc Vite.
- Không kết nối PostgreSQL trực tiếp từ Frontend.

---

## Sprint 3

API/DTO: [API_CONTRACT.md](API_CONTRACT.md#sprint-3).
DI: [Backend Program.cs](../backend/Chat.Api/Program.cs), [Frontend Program.cs](../frontend/Program.cs).

### Kế thừa Sprint 1–2

| Phần hiện có | Sprint 3 |
|---|---|
| Tin nhắn có `createdAt` UTC | FE dùng để hiển thị ngày/giờ địa phương |
| Hồ sơ cũ dùng `GET/PATCH /api/users/me`, tên tối đa 100 ký tự | Giữ API cũ; API hồ sơ mới giới hạn tên 50 ký tự |
| `ConversationMembers` có `LastReadSeq`, `LastDeliveredSeq` | Tái sử dụng, không tạo bảng receipts riêng |
| Messages dùng `body`, Seq và ClientMessageId | Tiếp tục dùng chung cho tin có file |
| FE có `ApiRequestSender` xử lý Cookie/CSRF | Bổ sung multipart và download |
| Có Friendships, Blocks, quyền nhóm và SignalR | Tái sử dụng cho Sprint 3 |

Tên cũ dài hơn 50 ký tự vẫn được đọc, không tự cắt hoặc sửa.

### Tin nhắn và file

1. FE gọi Messages hiện có với `limit=10`.
2. Lấy ID của các tin vừa nhận để gọi `attachment-details`.
3. Ghép file vào tin bằng `messageId`.

`messageIds` là danh sách Guid phân cách bằng dấu phẩy, tối đa 100 ID. Backend kiểm tra quyền từng tin; nếu có ID không được phép truy cập thì từ chối request.

### SignalR

Giữ Hub `/hubs/chat` và event `ConversationChanged`:

```json
{
  "eventId": "Guid",
  "conversationId": "Guid",
  "latestSeq": 120
}
```

Bổ sung event `MessageReceiptChanged`:

```json
{
  "conversationId": "Guid",
  "userId": "Guid",
  "deliveredSeq": 120,
  "readSeq": 118
}
```

- Kiểm tra subscription, quyền nhóm và Blocks trước khi phát.
- Chỉ phát sau commit.
- FE bỏ qua mốc cũ, ghép tên từ API receipts.
- Reconnect: Join lại → đồng bộ Messages bằng `afterSeq` → tải lại receipts và thiết lập thông báo.
