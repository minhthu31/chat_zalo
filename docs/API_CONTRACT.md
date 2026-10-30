# API CONTRACT – CHAT_ZALO

**Phiên bản:** 1.2 
**Phạm vi:** P0 – Chat cá nhân 1–1  
**Áp dụng:** Toàn bộ thành viên dự án chat_zalo

## 1. Quy ước chung

| Hạng mục | Quy định |
|---|---|
| Base URL | `/api` |
| JSON | `camelCase` |
| ID | UUID (`Guid`) |
| Thời gian | ISO 8601 UTC |
| Xác thực | ASP.NET Core Identity + Cookie |
| Realtime | SignalR `/hubs/chat` |
| Content-Type | `application/json` |

- API phải kiểm tra xác thực và phân quyền tại Backend.
- Không trả mật khẩu, PasswordHash hoặc dữ liệu nhạy cảm.
- Frontend Blazor WebAssembly dùng `HttpClient` với browser credentials `Include` khi API dùng Cookie và khác origin; cấu hình thực tế do thành viên tích hợp đảm nhiệm.
- API thay đổi dữ liệu phải có cơ chế chống CSRF phù hợp.

## 2. HTTP Status Code

| Mã | Ý nghĩa | Ví dụ |
|---|---|---|
| `200` | Thành công | Đăng nhập, lấy tin |
| `201` | Tạo mới | Tạo hội thoại |
| `204` | Thành công, không có body | Đăng xuất |
| `400` | Dữ liệu không hợp lệ | Thiếu trường |
| `401` | Chưa xác thực hoặc đăng nhập sai | Sai mật khẩu |
| `403` | Không có quyền | Đọc chat người khác |
| `404` | Không tìm thấy | User không tồn tại |
| `409` | Xung đột dữ liệu | Lời mời kết bạn trùng |
| `500` | Lỗi hệ thống | Lỗi server |

## 3. Response lỗi

Mọi lỗi nghiệp vụ thống nhất JSON:

```json
{
  "code": "FRIEND_REQUEST_ALREADY_EXISTS",
  "message": "Friend request already exists"
}
```

Quy định:

- `code`: chữ IN HOA, dùng dấu `_`.
- `message`: thông báo dễ hiểu.
- Không trả HTTP `200` khi nghiệp vụ thất bại.
- Frontend dựa vào HTTP Status và `code` để xử lý.
- Không trả stack trace hoặc thông tin nhạy cảm.

## 4. Danh sách API chính thức

### 4.1. Authentication

| Method | Endpoint | Chức năng |
|---|---|---|
| POST | `/api/auth/register` | Đăng ký |
| POST | `/api/auth/login` | Đăng nhập |
| POST | `/api/auth/logout` | Đăng xuất |
| GET | `/api/auth/me` | Lấy tài khoản hiện tại |
| GET | `/api/auth/csrf` | Cấp CSRF token |

**RegisterRequest**

```json
{
  "username": "user01",
  "email": "user01@example.com",
  "password": "123!"
}
```

**LoginRequest**

```json
{
  "usernameOrEmail": "user01",
  "password": "123!"
}
```

**UserResponse** (dùng cho login và auth/me)

```json

{
  "id": "550e8400-e29b-41d4-a716-446655440001",
  "username": "user01",
  "displayName": "Nguyen Van A"
}

```

**CSRF Contract**

- `GET /api/auth/csrf` trả HTTP `200`:

```json
{
  "token": "csrf-token-value"
}
```

- Backend cấp và kiểm tra token bằng ASP.NET Core Antiforgery.
- Frontend gửi header `X-CSRF-TOKEN` trên các request POST, PUT, PATCH, DELETE, bao gồm đăng ký, đăng nhập và đăng xuất.
- Sau khi trạng thái đăng nhập thay đổi, Frontend lấy lại CSRF token.
- Thiếu hoặc sai token trả HTTP `400` với code `INVALID_CSRF_TOKEN`.
- API chưa xác thực trả `401`, không đủ quyền trả `403`.
- API trả JSON, không chuyển hướng sang trang đăng nhập HTML.
- Cookie xác thực sử dụng HttpOnly và Secure khi chạy HTTPS.
- Nếu FE và BE khác origin, Backend phải cấu hình CORS cho đúng origin FE và AllowCredentials, không dùng wildcard.
- Đăng ký thành công: `201` + `UserResponse`; **không tự đăng nhập**. Trùng username/email: `409`.
- Đăng nhập thành công: `200` + `UserResponse` và thiết lập Cookie HTTP-only, Secure trên HTTPS. Đăng nhập sai: `401` + `INVALID_CREDENTIALS`.
- `/api/auth/me`: `200` + `UserResponse` hoặc `401`. Đăng xuất: `204`, xóa Cookie.
- Cookie được phát hành tại HTTP boundary (controller/Identity SignInManager hoặc auth adapter); `IAuthService.LoginAsync` chỉ trả dữ liệu tài khoản, không tự chứng minh rằng cookie đã thiết lập.

### 4.2. Users

| Method | Endpoint | Chức năng |
|---|---|---|
| GET | `/api/users/me` | Xem hồ sơ |
| PATCH | `/api/users/me` | Sửa hồ sơ |
| GET | `/api/users/search?username=...` | Tìm người dùng |

**UpdateProfileRequest**

```json
{
  "displayName": "Nguyen Van A"
}
```

**UserDto**

```json

{
  "id": "550e8400-e29b-41d4-a716-446655440001",
  "username": "user01",
  "displayName": "Nguyen Van A"
}

```

Không trả Email trong danh sách tìm kiếm công khai.

### 4.3. Friendships

| Method | Endpoint | Chức năng |
|---|---|---|
| POST | `/api/friendships` | Gửi lời mời |
| GET | `/api/friendships` | Danh sách bạn bè |
| GET | `/api/friendships/requests` | Lời mời đang chờ |
| POST | `/api/friendships/{userId}/accept` | Chấp nhận |
| POST | `/api/friendships/{userId}/reject` | Từ chối |
| DELETE | `/api/friendships/{userId}` | Hủy lời mời / hủy kết bạn |

**FriendRequestDto**

```json
{
  "targetUserId": "550e8400-e29b-41d4-a716-446655440002"
}
```

**GET `/api/friendships/requests`** trả object `{ "incoming": FriendshipDto[], "outgoing": FriendshipDto[] }`; `userId` trong từng phần tử là **đối phương**. Trong `incoming`, `{userId}` tại endpoint `accept/reject` là ID người gửi; trong `outgoing` là người nhận. Chỉ trả yêu cầu `Pending`.

**FriendshipDto**

```json

{
  "userId": "550e8400-e29b-41d4-a716-446655440002",
  "username": "user02",
  "displayName": "Nguyen Van B",
  "status": "Accepted"
}

```

Trạng thái: `Pending`, `Accepted`, `Rejected`, `Cancelled`. `GET /api/friendships` chỉ trả `Accepted` (`200`, mảng `FriendshipDto`). `POST /api/friendships`: `201` + `FriendshipDto` khi gửi mới; đã có lời mời Pending trả `409 FRIEND_REQUEST_ALREADY_EXISTS`. Accept/Reject/Delete thành công `204`. Không tự kết bạn. Backend kiểm tra đúng requester/receiver.

### 4.4. Blocks

| Method | Endpoint | Chức năng |
|---|---|---|
| PUT | `/api/blocks/{userId}` | Chặn người dùng |
| DELETE | `/api/blocks/{userId}` | Bỏ chặn |


**Quy định chặn người dùng**

- Không được tự chặn.
- Một người chặn người còn lại thì cả hai không được gửi tin nhắn mới.
- Người thực hiện chặn vẫn được xem lịch sử tin nhắn cũ.
- Người bị chặn không được xem lịch sử qua API trong thời gian đang bị chặn, trả `403 CONVERSATION_ACCESS_DENIED`.
- Bỏ chặn không tự động khôi phục quan hệ bạn bè.
- Backend phải kiểm tra quyền dựa trên tài khoản đang xác thực.

### 4.5. Conversations

| Method | Endpoint | Chức năng |
|---|---|---|
| POST | `/api/conversations/direct` | Tạo hội thoại 1–1 |
| GET | `/api/conversations` | Lấy danh sách hội thoại |

**CreateDirectConversationRequest**

```json
 
{
  "targetUserId": "550e8400-e29b-41d4-a716-446655440002"
}
```

**ConversationDto**

```json

{
  "id": "550e8400-e29b-41d4-a716-446655440003",
  "kind": "Direct",
  "lastMessageSeq": 0
}

```

- Chỉ được tạo hội thoại khi **đã kết bạn** và không bị chặn theo hai chiều; nếu không, `403 DIRECT_CONVERSATION_NOT_ALLOWED`.
- Hai người chỉ có một hội thoại 1–1.
- Nếu hội thoại đã tồn tại, trả lại hội thoại cũ với HTTP `200`.
- Nếu tạo mới, trả `201`.

### 4.6. Messages

| Method | Endpoint | Chức năng |
|---|---|---|
| GET | `/api/conversations/{id}/messages?beforeSeq=...` hoặc `/api/conversations/{id}/messages?afterSeq=...` | Lấy lịch sử hoặc đồng bộ tin nhắn mới |
| POST | `/api/conversations/{id}/messages` | Gửi tin nhắn |

**SendMessageRequest**

```json

{
  "clientMessageId": "550e8400-e29b-41d4-a716-446655440006",
  "body": "Xin chao!"
}

```

**MessageResponse**

```json

{
  "id": "550e8400-e29b-41d4-a716-446655440004",
  "conversationId": "550e8400-e29b-41d4-a716-446655440003",
  "senderUserId": "550e8400-e29b-41d4-a716-446655440001",
  "seq": 1,
  "body": "Xin chao!",
  "createdAt": "2026-10-29T04:00:00Z"
}

```


**Lấy lịch sử và đồng bộ tin nhắn**

Endpoint: `GET /api/conversations/{id}/messages`

| Tham số | Kiểu | Quy định |
|---|---|---|
| `beforeSeq` | `long?` | Lấy tối đa `limit` tin mới nhất có Seq nhỏ hơn mốc |
| `afterSeq` | `long?` | Lấy tối đa `limit` tin sớm nhất có Seq lớn hơn mốc |
| `limit` | `int` | Mặc định 20, tối đa 100 |

- Không truyền mốc: lấy các tin mới nhất.
- Không được truyền đồng thời `beforeSeq` và `afterSeq`.
- Nếu truyền cả hai, trả HTTP `400 INVALID_PAGINATION_PARAMETERS`.
- Response là mảng `MessageResponse`, sắp xếp theo Seq tăng dần.
- `afterSeq` được sử dụng khi nhận sự kiện SignalR và khi reconnect.

Quy định:

- Tin nhắn văn bản từ 1–4.000 ký tự; không tự thay đổi nội dung khi retry.
- Chỉ thành viên hội thoại còn là bạn bè và không bị chặn mới được gửi tin.
- Kiểm tra quyền xem lịch sử theo quy tắc Blocks.
- `Seq` tăng theo từng hội thoại bằng transaction và cập nhật nguyên tử `Conversations.LastMessageSeq`.
- Retry cùng `ClientMessageId` và cùng nội dung trả tin cũ với HTTP `200`.
- Trùng `ClientMessageId` nhưng khác nội dung trả HTTP `409 CLIENT_MESSAGE_ID_CONFLICT`.
- Gửi tin mới thành công trả HTTP `201`.
- Chỉ phát SignalR sau khi transaction commit thành công.


## 5. Interface dùng chung Backend

Các thành viên phải sử dụng đúng tên phương thức, tham số và kiểu trả về đã thống nhất.

### 5.1. IAuthService – Xác thực

```csharp
Task<UserResponse> RegisterAsync(
    RegisterRequest request);

Task<UserResponse> LoginAsync(
    LoginRequest request);

Task LogoutAsync();
```

### 5.2. IUserService – Người dùng

```csharp
Task<UserDto> GetProfileAsync(Guid userId);

Task<List<UserDto>> SearchAsync(
    string username);

Task<UserDto> UpdateProfileAsync(
    Guid userId,
    UpdateProfileRequest request);
```

### 5.3. IFriendshipService – Kết bạn

```csharp
Task<FriendshipDto> SendRequestAsync(
    Guid currentUserId,
    Guid targetUserId);

Task AcceptAsync(
    Guid currentUserId,
    Guid requesterUserId);

Task RejectAsync(
    Guid currentUserId,
    Guid requesterUserId);

Task RemoveAsync(
    Guid currentUserId,
    Guid targetUserId);

Task<bool> AreFriendsAsync(
    Guid userAId,
    Guid userBId);
```

### 5.4. IBlockService – Chặn người dùng

```csharp
Task BlockAsync(
    Guid currentUserId,
    Guid targetUserId);

Task UnblockAsync(
    Guid currentUserId,
    Guid targetUserId);

Task<bool> IsBlockedAsync(
    Guid userAId,
    Guid userBId);

Task<bool> HasBlockedAsync(
    Guid blockerUserId,
    Guid blockedUserId);
```

`IsBlockedAsync` trả `true` nếu một trong hai người đã chặn người còn lại.

### 5.5. IConversationService – Hội thoại

```csharp
Task<ConversationDto> CreateDirectAsync(
    Guid currentUserId,
    Guid targetUserId);

Task<List<ConversationDto>> GetConversationsAsync(
    Guid currentUserId);
```

### 5.6. IMessageService – Tin nhắn

```csharp
Task<MessageResponse> SendAsync(
    Guid currentUserId,
    Guid conversationId,
    SendMessageRequest request);

Task<List<MessageResponse>> GetHistoryAsync(
    Guid currentUserId,
    Guid conversationId,
    long? beforeSeq,
    int limit = 20);

Task<List<MessageResponse>> GetNewMessagesAsync(
    Guid currentUserId,
    Guid conversationId,
    long afterSeq,
    int limit = 20);

```

### 5.7. Quy định chung

- ID người dùng và hội thoại sử dụng `Guid`.
- Các phương thức bất đồng bộ sử dụng `Task<T>` hoặc `Task`.
- DTO đặt trong `Chat.Application/DTOs/`.
- Interface đặt trong `Chat.Application/Interfaces/`.
- Service đặt trong `Chat.Application/Services/`.
- Không tự ý đổi tên phương thức, tham số hoặc kiểu trả về.
- Mọi thay đổi Interface phải được Leader duyệt.
- Khi triển khai thực tế, bổ sung `CancellationToken` thống nhất cho toàn bộ phương thức nếu nhóm quyết định sử dụng.


## 6. SignalR – Realtime

**Hub:** `/hubs/chat`

Hub bắt buộc xác thực bằng Cookie.

| Event | Payload | Mục đích |
|---|---|---|
| `ConversationChanged` | `eventId`, `conversationId`, `latestSeq` | Thông báo có tin mới |
| `TypingChanged` | `conversationId`, `userId`, `isTyping` | Đang nhập (nếu kịp) |
| `ReceiptChanged` | `eventId`, `conversationId` | Đã nhận/đã đọc (nếu kịp) |

**ConversationChanged Payload**

```json

{
  "eventId": "550e8400-e29b-41d4-a716-446655440005",
  "conversationId": "550e8400-e29b-41d4-a716-446655440003",
  "latestSeq": 1
}

```


**Quy trình gửi tin nhắn**

1. Frontend gọi `POST /api/conversations/{id}/messages`.
2. Backend kiểm tra quyền, cấp Seq và lưu tin nhắn trong transaction PostgreSQL.
3. Sau khi commit thành công, Backend phát `ConversationChanged` đến những người có quyền nhận thông báo.
4. Frontend nhận event và gọi GET Messages với `afterSeq`.
5. Khi reconnect, Frontend tiếp tục đồng bộ từ Seq cuối cùng đã nhận.
6. Frontend xử lý event trùng và không dùng SignalR thay thế Database.


SignalR chỉ thông báo realtime, không thay thế Database.

## 7. Quy định cho thành viên

- Tuân thủ API, DTO, Interface và SignalR Event đã chốt.
- Không tự ý đổi endpoint, tên trường hoặc kiểu dữ liệu.
- Không tự ý sửa Entity/DbContext dùng chung.
- Thay đổi hợp đồng phải được Leader duyệt.
- Mỗi chức năng phát triển trên branch riêng.
- Code hoàn thành phải build được và có kiểm thử.
- Tạo Pull Request trước khi merge vào `develop`.

**Tài liệu liên quan:**
- `database/ERD.mmd`
- `database/DATABASE_RULES.md`
- `docs/GIT_RULES.md`

**Ghi chú:** Tài liệu này là hợp đồng thiết kế của source khung. Chưa có nghĩa các API và chức năng đã được triển khai.

## 8. PHỤ LỤC TÍCH HỢP FE – BE – DB (BẮT BUỘC, v1.)

**Tên dự án và database:** `chat_zalo`. Giữ nguyên namespace, tên `.sln`/`.csproj` đang là `Chat.*` để tránh phá tham chiếu; `chat_zalo` là tên repo, thư mục và database, **không phải lệnh đổi mọi class thành chat_zalo**.

### A. Cấu hình và bảo mật
- API phát triển ở `http://localhost:5080`; FE Blazor ở origin riêng khi chạy `dotnet run`, đọc `ApiBaseUrl` từ `frontend/wwwroot/appsettings.json`. `/api` là **đường dẫn API**, không phải origin. `localhost:5080` chỉ là mặc định phát triển.
- Bản demo production ưu tiên reverse proxy cùng origin qua HTTPS; API và Hub dùng chung auth cookie. Khi dev khác origin: CORS phải chỉ định origin FE cụ thể + `AllowCredentials`, không dùng wildcard; cookie `SameSite`/`Secure` phù hợp, thực hiện CSRF token chống ghi dữ liệu trái phép. `BrowserRequestCredentials.Include` với HttpClient dùng browser fetch; SignalR cần cookie cùng cơ chế của trình duyệt. Không gửi auth cookie bằng tay.
- CSRF: Tất cả POST/PUT/PATCH/DELETE phải có token hợp lệ.
  Backend cấp token qua GET /api/auth/csrf.
  Frontend gửi header X-CSRF-TOKEN.
  Backend kiểm tra token bằng ASP.NET Core Antiforgery.
  Thiếu hoặc sai token trả HTTP 400 INVALID_CSRF_TOKEN.
  CORS không thay thế cơ chế chống CSRF.
- Mọi route trừ register/login (và endpoint cấp CSRF nếu có) yêu cầu đăng nhập. Khi không xác thực, API trả `401` JSON, không redirect HTML. `403` dành cho truy cập bị cấm. FE không lưu mật khẩu hoặc cookie HttpOnly trong localStorage.
- Kết nối DB chỉ ở BE; không gửi chuỗi kết nối tới Blazor. Cấu hình local khớp `Database=chat_zalo;Username=chat_zalo`; secrets thực tế dùng env `ConnectionStrings__ChatDb` hoặc user-secrets.

### B. Quy tắc DTO / status chính thức
- JSON case: camelCase; GUID ở dạng chuỗi; enum ở dạng chuỗi PascalCase như `Pending`, `Direct`; timestamp `DateTimeOffset` UTC định dạng `2026-10-29T04:00:00Z`.
- Tất cả GET trả `200` + DTO/mảng; `PATCH /api/users/me` trả `200` + `UserDto`; `POST /api/auth/register` trả `201` + `UserResponse`; login `200` + UserResponse; logout `204`; blocks, friendship accept/reject/delete `204`.
- `POST /api/conversations/direct`: `201` khi tạo, `200` nếu tồn tại, body cùng kiểu `ConversationDto`.
- `POST /api/conversations/{id}/messages`: `201` tạo mới, `200` retry idempotent, body cùng `MessageResponse`; `GET` history trả mảng tăng dần theo seq, lấy tối đa limit tin gần nhất nhỏ hơn beforeSeq.
- Error body cho *cả* validation/auth/authorization/not found là `{ "code": "UPPER_SNAKE_CASE", "message": "..." }`; `400 VALIDATION_ERROR`, `401 UNAUTHORIZED` hoặc `INVALID_CREDENTIALS`, `403 FORBIDDEN`, `404 NOT_FOUND`, `409 CONFLICT` và mã chuyên biệt. Định dạng này yêu cầu BE tự chuẩn hóa Identity/validation errors; API không được redirect login.
- Không tự phát triển envelope `{success,data}` và không đổi status/DTO riêng từng thành viên. Không trả password/email trong tìm kiếm.

### C. Bảng ánh xạ và ownership
| FE (Blazor) | BE (API/Services) | DB (EF Core) |
|---|---|---|
| Auth pages + AuthApiClient | AuthController + IAuthService + Identity sign-in | ApplicationUser/Users + Identity tables |
| Profile, Search + UsersApiClient | UsersController + IUserService | Users |
| Friendship components + FriendshipApiClient | FriendshipsController + IFriendshipService | Friendships + Users |
| Block controls + BlocksApiClient | BlocksController + IBlockService | UserBlocks |
| Conversation list + ConversationsApiClient | ConversationsController + IConversationService | Conversations, DirectConversations, ConversationMembers |
| Chat page + MessagesApiClient + SignalR client | MessagesController + IMessageService + ChatHub | Messages + Conversations.LastMessageSeq |

- FE Models ánh xạ cùng JSON contract, **không tham chiếu trực tiếp** `Chat.Infrastructure` hoặc EF Entity. BE DTO đặt `Chat.Application/DTOs`; interface BE đặt `Chat.Application/Interfaces`; FE models ở `frontend/Models`.
- Service trong `Chat.Application` không phụ thuộc HTTP `HttpContext`; controller/adapter chịu trách nhiệm cookie, lấy user hiện tại và HTTP code. Nếu sau này thay chữ ký interface, sửa contract và có Leader duyệt trước merge.
- DB schema thay đổi chỉ qua EF migrations ở `backend/Chat.Infrastructure/Migrations/` (theo cấu hình project); không chạy SQL init song song và không tự sửa DbContext để khớp FE.

### D. Quy tắc realtime
- Gửi tin qua HTTP POST, lưu transaction cùng tăng `LastMessageSeq` rồi **sau commit** phát `ConversationChanged` tới đúng nhóm được phân quyền theo `conversationId`; `eventId` hỗ trợ chống nhận lặp. Hub không nhận quyền từ client một cách mù quáng.
- FE nhận signal thì gọi API lấy tin mới; reconnect tải lại từ seq đã biết và khử trùng `message.id`/`seq`. Typing/receipt **P1** (không bắt buộc P0); không dùng event thay Database.

### E. Tiêu chí nghiệm thu tích hợp
1. FE tạo account → login → `GET /api/auth/me` trả đúng user; browser có HttpOnly cookie, logout rồi nhận `401`.
2. FE tìm bạn → gửi/nhận/duyệt lời mời → tạo direct conversation đúng 1 cặp (gửi lại trả `200`).
3. Hai tài khoản khác nhau gửi nhận tin, F5 vẫn còn lịch sử, seq tăng và retry không trùng tin.
4. User thứ ba bị `403` khi đọc chat; chặn hai chiều ngăn gửi mới.
5. Cả nhóm chạy chung database `chat_zalo`, migration từ DB mới sạch thành công; browser Blazor không chứa DB credentials.

**Nguồn sự thật:** `docs/API_CONTRACT.md` là HTTP/DTO/Hub contract; `database/DATABASE_RULES.md` là schema/invariants; `database/ERD.mmd` là sơ đồ suy ra từ database rules; `docs/FRONTEND_BLAZOR_GUIDE.md` là quy tắc FE; `docs/GIT_RULES.md` là quy trình thay đổi. Khi mâu thuẫn, mở issue và Leader duyệt, không tự chọn theo sở thích.
