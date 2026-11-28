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

## 9. Sprint 2 – API mới

**Kiểu dùng chung:** `Guid` là chuỗi UUID; `GroupDto={id,kind:"Group",name,lastMessageSeq}`; `GroupMemberDto={userId,username,displayName,role}` (`Admin`/`Member`). Các API `/conversations/{id}` dành cho nhóm phải kiểm tra `kind=Group`, nếu sai trả `400 INVALID_CONVERSATION_KIND`.

| Method | Endpoint | Request → Response thành công |
|---|---|---|
| POST | `/api/auth/change-password` | `{currentPassword,newPassword}` → `204` |
| GET | `/api/users/search/paged?username=an&page=1&pageSize=10` | `200 PagedUsersResponse` |
| POST | `/api/conversations/groups` | `{name,memberIds:Guid[]}` → `201 GroupDto` |
| GET | `/api/conversations/{id}/members` | `200 GroupMemberDto[]` |
| POST | `/api/conversations/{id}/members` | `{userId}` → `201 GroupMemberDto` |
| DELETE | `/api/conversations/{id}/members/{userId}` | Admin xóa thành viên khác → `204` |
| POST | `/api/conversations/{id}/leave` | Người gọi tự rời → `204` |
| POST | `/api/conversations/{id}/admin-transfer` | `{targetUserId}` → `202 AdminTransferDto` |
| POST | `/api/conversations/{id}/admin-transfer/{requestId}/accept` | Người được chọn đồng ý → `204` |
| POST | `/api/conversations/{id}/admin-transfer/{requestId}/reject` | Người được chọn từ chối → `204` |
| DELETE | `/api/conversations/{id}` | Admin giải tán nhóm → `204` |

**DTO bổ sung:**

```json
PagedUsersResponse: {"items":[{"id":"Guid","username":"string","displayName":"string"}],"page":1,"pageSize":10,"totalCount":25,"totalPages":3}
GroupDto: {"id":"Guid","kind":"Group","name":"Nhom hoc","lastMessageSeq":0}
GroupMemberDto: {"userId":"Guid","username":"string","displayName":"string","role":"Member"}
AdminTransferDto: {"id":"Guid","conversationId":"Guid","fromUserId":"Guid","toUserId":"Guid","status":"Pending","createdAt":"2026-11-07T10:00:00Z"}
```

**Validation / lỗi cố định:**

- Users: `page>=1`, `pageSize` mặc định 10, tối đa 100; sắp xếp ổn định. API `/users/search` cũ vẫn trả **mảng**, không đổi kiểu response.
- Password: kiểm tra mật khẩu hiện tại; sai → `400 INVALID_CURRENT_PASSWORD`; đổi thành công cập nhật Security Stamp, đăng xuất phiên hiện tại và từ chối các phiên cũ ở request xác thực tiếp theo (Security Stamp được kiểm tra trên mỗi request). Người dùng phải đăng nhập lại bằng mật khẩu mới. Thiếu/sai CSRF → `400 INVALID_CSRF_TOKEN`.
- Nhóm: tên không rỗng (độ dài tối đa **100 ký tự**); `memberIds` không chứa ID trùng, tối thiểu 1 bạn bè Accepted của người tạo ngoài chính mình; người tạo tự động được thêm làm Admin. Lỗi thiếu quyền → `403 GROUP_ACCESS_DENIED`, chưa kết bạn → `403 NOT_FRIEND`, thêm trùng → `409 ALREADY_GROUP_MEMBER`, không tìm thấy → `404 NOT_FOUND`.
- Chuyển Admin: chỉ Admin hiện tại gửi yêu cầu tới **Member đang hoạt động**; mỗi nhóm tối đa một yêu cầu `Pending` tại một thời điểm (`409 ADMIN_TRANSFER_PENDING`). Chỉ người nhận có quyền accept/reject. Không được chấp nhận lặp (`409 ADMIN_TRANSFER_ALREADY_RESOLVED`). Chấp nhận thành công mới đổi vai trò **trong cùng transaction** (Admin cũ → Member; Member mới → Admin). Từ chối không đổi vai trò. Admin muốn rời phải chuyển quyền trước; nếu nhóm chỉ còn Admin thì giải tán nhóm.
- Giải tán nhóm: đánh dấu ngừng hoạt động, từ chối gửi tin/Join sau đó; không xóa vật lý Messages. Member được tự rời; chỉ Admin được xóa thành viên khác hoặc giải tán nhóm.

## 10. Blocks, Messages, SignalR trong Group

- Thành viên được mời **bạn bè Accepted của chính mình**; các thành viên nhóm không cần kết bạn với nhau. Group hoạt động có **đúng một Admin**. Người rời/bị xóa không được đọc/gửi/Join nhóm.
- **A chặn B:** A và B đều không xem được tin cũ, tin mới và tệp đính kèm của nhau trong nhóm. Cả hai vẫn xem tin của C, gửi tin và giữ tư cách thành viên. Tin hiện lại khi không còn lượt chặn nào giữa hai người. Backend lọc **trước LIMIT** để lấy đủ tối đa 10 tin được xem; Seq có thể nhảy. Không lộ `body`/preview của tin bị ẩn. **Quy tắc Direct không đổi.**
- Group dùng **cùng API Messages**, cùng `MessageResponse`, Seq, retry; FE yêu cầu `limit=10`, Backend vẫn mặc định `20`.
- SignalR Hub `/hubs/chat`: event `ConversationChanged` payload `{eventId,conversationId,latestSeq}`. Chỉ **connection đã Join** và còn quyền mới nhận event. Không phát trước khi DB commit. FE nhận event thì GET Messages bằng `afterSeq`; khi reconnect phải Join lại và đồng bộ. SignalR không phát nội dung tin nhắn.

## 11. Quy tắc tích hợp

1. FE dùng đúng endpoint, request, response và HTTP status trong hợp đồng; không tạo API riêng. Backend chuẩn hóa lỗi thành `{code,message}`; mọi thao tác ghi có CSRF.
2. DB giữ schema Sprint 1; chỉ mở rộng nhóm qua EF Migration, kiểm tra `database/DATABASE_RULES.md` và `database/ERD.mmd`. Không tạo User Entity thứ hai.
3. Mỗi người dùng branch riêng, tạo PR vào **`main`**; Leader duyệt. CI Backend + Frontend do Leader phụ trách.
4. Kiểm thử tối thiểu: đăng nhập 2 tài khoản, đổi mật khẩu, tìm kiếm phân trang, mời bạn bè, chuyển Admin, Blocks Group, 1.000 tin/10 tin mỗi lần tải, retry, realtime/reconnect, PostgreSQL 18.
5. Chưa thuộc Sprint 2: file/ảnh và 2FA.

**Tài liệu này là hợp đồng thiết kế Sprint 2; không có nghĩa code mới đã triển khai. Mọi thay đổi endpoint, DTO và quy tắc phải được Leader duyệt trước khi code.**

### Bổ sung giao diện hội thoại cá nhân và danh sách chặn

- `GET /api/blocks` yêu cầu đăng nhập, trả `200 [{userId,username,displayName,createdAt}]`: chỉ danh sách người do chính người gọi chặn; không trả quan hệ chiều ngược hoặc thông tin Identity nhạy cảm. GET không cần CSRF; PUT/DELETE giữ nguyên yêu cầu CSRF và response Sprint 1.
- `GET /api/conversations/direct/titles` yêu cầu đăng nhập, trả `200 [{conversationId,userId,username,displayName}]`: thông tin công khai của người đối thoại trong các hội thoại Direct mà người gọi còn quyền truy cập, áp dụng cùng quy tắc thành viên và Blocks như danh sách hội thoại. FE dùng displayName, nếu trống dùng username. API `GET /api/conversations` và ConversationDto hiện có giữ nguyên.
- FE `/chat` có thanh thông tin bên phải, hủy kết bạn và chặn/bỏ chặn; `/blocks` tải trạng thái từ Backend sau khi tải lại trang. Hủy kết bạn giữ lịch sử và ngừng cho gửi tin theo quy tắc hiện có. Thanh điều hướng dùng chung cho các trang Sprint 1.

---

## Sprint 3

### 1. Kế thừa Sprint 1–2

| Phần hiện có | Sprint 3 |
|---|---|
| Tin nhắn có `createdAt` UTC | FE dùng để hiển thị ngày/giờ địa phương |
| Hồ sơ cũ dùng `GET/PATCH /api/users/me`, tên tối đa 100 ký tự | Giữ API cũ; API hồ sơ mới giới hạn tên 50 ký tự |
| `ConversationMembers` có `LastReadSeq`, `LastDeliveredSeq` | Tái sử dụng, không tạo bảng receipts riêng |
| Messages dùng `body`, Seq và ClientMessageId | Tiếp tục dùng chung cho tin có file |
| FE có `ApiRequestSender` xử lý Cookie/CSRF | Bổ sung multipart và download |
| Có Friendships, Blocks, quyền nhóm và SignalR | Tái sử dụng cho Sprint 3 |

Tên cũ dài hơn 50 ký tự vẫn được đọc, không tự cắt hoặc sửa.

### 2. API hồ sơ và Avatar

| Method | Endpoint | Thành công |
|---|---|---|
| GET | `/api/users/{userId}/profile` | `200 ProfileDto` |
| PUT | `/api/users/me/profile` | `200 ProfileDto` |
| PUT | `/api/users/me/avatar` | `200 ProfileDto` |
| DELETE | `/api/users/me/avatar` | `204` |
| GET | `/api/users/{userId}/avatar` | `200` ảnh; `404` nếu chưa có |

Request sửa hồ sơ:

```json
{
  "displayName": "Nguyễn Văn A",
  "bio": "Giới thiệu"
}
```

`ProfileDto`:

```json
{
  "id": "Guid",
  "username": "user01",
  "displayName": "Nguyễn Văn A",
  "bio": "Giới thiệu",
  "avatarUrl": null
}
```

Quy định:

- Tên sau trim dài 1–50 ký tự; Bio tối đa 200 ký tự.
- Avatar multipart trường `file`, hỗ trợ JPG/PNG/WebP, tối đa 2 MB.
- Người đã đăng nhập khi tìm kiếm hoặc xem bạn bè được xem tên, Bio, Avatar.
- Chỉ chủ tài khoản được chỉnh sửa; Backend lấy danh tính từ phiên đăng nhập.
- Không trả Email.
- Giữ DTO tìm kiếm cũ; FE lấy thêm hồ sơ theo ID của kết quả đang hiển thị.

Chi tiết CHAT-25:

- Có Avatar: `avatarUrl` là `/api/users/{userId}/avatar`; chưa có: `null`. API ảnh trả `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`; không yêu cầu query version.
- Hai GET hồ sơ/Avatar yêu cầu đăng nhập; Blocks không cấm đọc các trường công khai này. Không áp dụng quyền xem tin nhắn cho hồ sơ.
- User không tồn tại: `404 USER_NOT_FOUND`. User tồn tại nhưng chưa có Avatar hoặc nội dung ảnh đã mất: `404 ATTACHMENT_NOT_FOUND` ở API ảnh.
- PUT hồ sơ là cập nhật cả `displayName` và `bio`; `bio: ""` xóa Bio. Thiếu/null trường bắt buộc trả 400; không tự thay bằng giá trị rỗng. Tên không hợp lệ dùng `INVALID_DISPLAY_NAME`, Bio không hợp lệ dùng `INVALID_BIO`.
- PUT Avatar dùng đúng một trường `file`, kiểm tra nội dung ảnh thực tế và giới hạn 2.097.152 byte. Rỗng/sai multipart: `400 VALIDATION_ERROR`; quá dung lượng: `413 FILE_TOO_LARGE`; sai loại: `415 UNSUPPORTED_FILE_TYPE`.
- Lưu Avatar bằng `IFileStorage` của CHAT-24. Trong cùng transaction: file mới thành `Active`, đổi `Users.AvatarFileId`, file cũ thành `Inactive`. DELETE đặt tham chiếu null và chuyển file cũ thành `Inactive`; chưa có Avatar vẫn trả 204.
- Chỉ xóa file vật lý cũ sau commit thành công; chỉ xóa khi file không còn được tham chiếu. Lỗi xóa ghi log, giữ metadata để xử lý lại, không trả lỗi đổi Avatar khi transaction đã thành công. Khi transaction thất bại, dọn file mới chưa được sử dụng.
- Cleanup CHAT-24 chỉ dọn `Pending` không có liên kết, không tự dọn Avatar `Inactive`. Không mở rộng cleanup để xóa file đang dùng.

### 3. Tin nhắn và file

Giữ nguyên API Messages cũ, bổ sung:

| Method | Endpoint | Thành công |
|---|---|---|
| POST | `/api/conversations/{id}/attachments` | `201 AttachmentDto` |
| POST | `/api/conversations/{id}/messages/attachments` | `201` tạo mới, `200` retry |
| GET | `/api/conversations/{id}/messages/attachment-details?messageIds=...` | `200 AttachmentMessageDto[]` |
| GET | `/api/attachments/{attachmentId}/content` | `200` nội dung |
| GET | `/api/attachments/{attachmentId}/download` | `200` tải xuống |

Upload dùng multipart trường `file`.

Request gửi tin:

```json
{
  "clientMessageId": "Guid",
  "body": "Tài liệu của nhóm",
  "attachmentIds": ["Guid"]
}
```

`AttachmentDto`:

```json
{
  "id": "Guid",
  "fileName": "tai-lieu.pdf",
  "contentType": "application/pdf",
  "sizeBytes": 123456,
  "contentUrl": "/api/attachments/Guid/content",
  "downloadUrl": "/api/attachments/Guid/download"
}
```

`AttachmentMessageDto`:

```json
{
  "messageId": "Guid",
  "attachments": []
}
```

Response gửi tin là `{message, attachments}`; `message` giữ đúng cấu trúc `MessageResponse` hiện có.

**Luồng tải lịch sử:**

1. FE gọi Messages hiện có với `limit=10`.
2. Lấy ID của các tin vừa nhận để gọi `attachment-details`.
3. Ghép file vào tin bằng `messageId`.

`messageIds` là danh sách Guid phân cách bằng dấu phẩy, tối đa 100 ID. Backend kiểm tra quyền từng tin; nếu có ID không được phép truy cập thì từ chối request.

Quy định:

- Tối đa 5 file/tin, 10 MB/file, tổng 50 MB/tin.
- Quy ước 1 MB = 1.048.576 byte.
- Hỗ trợ JPG, PNG, WebP, PDF, DOCX, XLSX, TXT.
- `body` tối đa 4.000 ký tự, cho phép rỗng khi có file.
- Dùng chung bảng Messages, Seq và cơ chế chống trùng ClientMessageId.
- Tin chỉ có file vẫn xuất hiện trong lịch sử.
- File thuộc người upload và đúng hội thoại; một file chỉ gắn một tin.
- Kiểm tra quyền gửi khi upload và quyền xem tin khi xem/tải, áp dụng Blocks.
- Kiểm tra loại file thực tế; không công khai thư mục lưu file.
- Chỉ phát `ConversationChanged` sau khi lưu tin và liên kết file commit thành công.

Chi tiết CHAT-24/CHAT-26:

- Một lần upload một file; upload chỉ tạo StoredFiles `Pending`, chưa tạo tin hoặc phát event. File Pending chỉ uploader còn quyền hội thoại được xem/tải.
- File Attached chỉ được xem/tải khi người gọi được xem tin liên kết, bao gồm `JoinedSeq`, membership và Blocks. Trong nhóm, tệp bị ẩn giữa hai người khi một trong hai chặn người còn lại.
- Content/download trả MIME kiểm tra từ nội dung, `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`; không trả storage key hoặc đường dẫn máy chủ.
- Dọn Pending thủ công theo cutoff/limit do người vận hành chọn; khóa và kiểm tra lại trạng thái/liên kết trước khi xóa. Không tự dọn sau 24 giờ; không xóa Attached hoặc Avatar đang dùng. Cấu hình và lệnh: [CHAT24_STORAGE.md](CHAT24_STORAGE.md).
- Endpoint gửi kèm file yêu cầu `attachmentIds` có 1–5 Guid khác nhau, không rỗng; trùng ID/thiếu danh sách/sai số lượng: `400 INVALID_ATTACHMENT_COUNT`. `clientMessageId` không được là Guid rỗng; lỗi dữ liệu khác: `400 VALIDATION_ERROR`.
- Tra tin theo khóa chống trùng `(conversationId, senderUserId, clientMessageId)` trước khi yêu cầu file còn Pending. Retry cùng body và cùng tập ID file trả 200 với tin cũ, không thêm Seq/liên kết hoặc phát lại event. Thứ tự ID không làm thay đổi tập file; response sắp xếp attachments theo `id` để ổn định.
- Cùng ClientMessageId nhưng khác body hoặc tập file: `409 CLIENT_MESSAGE_ID_CONFLICT`, kể cả khi đổi giữa API tin văn bản và API tin kèm file. Tin văn bản và tin kèm file dùng chung khóa chống trùng.
- Tạo mới: file không tồn tại dùng `404 ATTACHMENT_NOT_FOUND`; file sai uploader/hội thoại/quyền dùng `403 CONVERSATION_ACCESS_DENIED`; file đã gắn vào tin khác dùng `409 ATTACHMENT_ALREADY_USED`. Chỉ được gắn file `UsageType=Attachment`, trạng thái Pending.
- Tạo Messages, cấp Seq, chuyển Pending → Attached và tạo liên kết phải nằm cùng transaction. Hai request tranh chấp cùng file: chỉ một thành công; request còn lại trả 409, không để lại tin rỗng/liên kết dở dang.
- `attachment-details`: thiếu/rỗng/sai Guid/trên 100 ID trả `400 VALIDATION_ERROR`; ID lặp được xử lý một lần. Tin không tồn tại trả `404 NOT_FOUND`; tin sai hội thoại/không có quyền trả `403 CONVERSATION_ACCESS_DENIED`. Từ chối toàn request, không trả dữ liệu một phần.
- Trả một `AttachmentMessageDto` cho mỗi tin hợp lệ được hỏi, kể cả `attachments: []` cho tin văn bản; thứ tự theo lần xuất hiện đầu tiên của messageIds. Không thay response phân trang Messages cũ.

### 4. Thông báo từng hội thoại

```text
GET /api/conversations/{id}/notification-settings
PUT /api/conversations/{id}/notification-settings
```

Request và response `200`:

```json
{
  "isMuted": false
}
```

- Mặc định `false`.
- Lưu riêng theo người dùng và hội thoại.
- Mute chỉ tắt âm thanh/banner; vẫn nhận và lưu tin bình thường.
- Không thay đổi Join/Leave hoặc quyền truy cập.

Chi tiết CHAT-27:

- Cả GET/PUT yêu cầu đăng nhập và kiểm tra quyền hội thoại hiện tại. Group yêu cầu nhóm đang hoạt động và membership còn hiệu lực; Direct dùng quyền truy cập hội thoại Sprint 1–2.
- Chưa có bản ghi cài đặt thì GET trả `200 {"isMuted":false}`; không bắt buộc tạo row khi GET. PUT tạo/cập nhật đúng khóa `(ConversationId, UserId)` lấy user từ phiên đăng nhập.
- PUT yêu cầu có `isMuted` kiểu boolean; thiếu/null/sai kiểu trả `400 VALIDATION_ERROR`. PUT cùng giá trị là idempotent.
- Hội thoại không tồn tại trả `404 NOT_FOUND`; không có quyền trả `403 CONVERSATION_ACCESS_DENIED` hoặc mã quyền nhóm hiện có.
- Một tài khoản dùng chung cài đặt trên mọi thiết bị. Thiết bị khác đọc lại API khi mở hội thoại/reconnect; không bắt buộc event mới.
- Không dùng IsMuted để lọc người nhận SignalR, chặn lưu/đồng bộ tin, hoặc ngừng receipts. FE chỉ tắt âm thanh/banner.

### 5. Đã nhận và đã xem

```text
GET /api/conversations/{id}/receipts
PUT /api/conversations/{id}/receipts/delivered
PUT /api/conversations/{id}/receipts/read
```

Request PUT:

```json
{
  "seq": 120
}
```

PUT trả `200 ReceiptDto`:

```json
{
  "userId": "Guid",
  "deliveredSeq": 120,
  "readSeq": 118
}
```

GET trả `200`:

```json
{
  "conversationId": "Guid",
  "items": [
    {
      "userId": "Guid",
      "displayName": "B",
      "deliveredSeq": 120,
      "readSeq": 118
    }
  ]
}
```

Quy định:

- **Đã gửi:** Backend lưu thành công.
- **Đã nhận:** thiết bị lấy được dữ liệu tin và xác nhận.
- **Đã xem:** người nhận mở hội thoại và đọc đến tin.
- Tái sử dụng mốc trong `ConversationMembers`.
- Mốc chỉ tăng; cập nhật đồng thời lấy giá trị lớn nhất.
- `readSeq ≤ deliveredSeq ≤ LastMessageSeq`; read tự nâng delivered nếu cần.
- Không xác nhận tin chưa tồn tại hoặc không có quyền xem.
- Không suy ra đã xem tin bị Blocks ẩn.
- Nhóm hiển thị tên người đã xem, ví dụ **“B, C đã xem”**, không tính người gửi.

Chi tiết CHAT-28:

- GET/PUT yêu cầu quyền hội thoại hiện tại. GET trả thông tin các thành viên đang hoạt động trong `items`. PUT dùng user đang đăng nhập. Thiếu/null/sai kiểu/âm/vượt `LastMessageSeq` dùng `400 INVALID_RECEIPT_SEQ`.
- Chat cá nhân dùng `deliveredSeq` và `readSeq` trong `items` và response PUT. Sau khi kiểm tra quyền, `seq` bằng hoặc thấp hơn mốc tương ứng trả mốc hiện tại. Khi nâng mốc, tin đích phải tồn tại và thỏa `JoinedSeq`.
- Chat nhóm dùng cùng hai endpoint PUT nhưng `seq` dương xác nhận đúng một tin. Response bổ sung `messageId`, `seq`, `isDelivered` và `isRead`. GET bổ sung `messages` gồm `messageId`, `seq`, `senderUserId`, `deliveredByUserIds` và `readByUserIds`. FE tra tên từ `items` và dùng các danh sách theo tin để hiển thị receipts nhóm.
- Khi tải một trang chat nhóm, FE gọi GET receipts với `?messageIds=id1,id2` theo các tin trên trang, tối đa 100 ID. GET chỉ trả các tin trong nhóm mà người gọi còn quyền xem, theo thứ tự seq tăng dần. Khi bỏ qua tham số, BE trả tối đa 100 tin mới nhất người gọi được xem. Chat cá nhân vẫn dùng `items`.
- `seq=0` trả mốc hiện tại sau khi kiểm tra quyền, không cập nhật hoặc phát event. Với nhóm, GET cung cấp trạng thái theo tin. Xác nhận Đã xem cũng ghi Đã nhận. Gửi lại xác nhận đã lưu và còn quyền xem trả kết quả hiện tại, không ghi lại hoặc phát event.
- Nhóm áp dụng `JoinedSeq` và Blocks cho từng tin. Khi A chặn B, cả hai vẫn xác nhận được tin của C. Xác nhận tin bị ẩn do chặn trả `403 CONVERSATION_ACCESS_DENIED`. Sau khi không còn lượt chặn nào giữa hai người, FE gửi Đã nhận khi tải được tin và Đã xem khi tin xuất hiện trong vùng nhìn thấy. BE lưu từng xác nhận riêng, kể cả với tin cũ hơn tin C.
- Event chỉ phát sau commit và khi trạng thái đổi, tới các connection đã Join và còn quyền xem. Nhóm kiểm tra quyền xem tin đích cho từng người nhận event.

### 6. SignalR

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

- Với chat nhóm, event bổ sung `messageId`, `seq`, `isDelivered` và `isRead`. FE áp dụng thay đổi theo cặp tin nhắn và người xác nhận.
- Kiểm tra subscription, quyền nhóm, `JoinedSeq` và Blocks trước khi phát.
- Chỉ phát sau commit.
- FE bỏ qua mốc cũ, ghép tên từ API receipts.
- Reconnect: Join lại → đồng bộ Messages bằng `afterSeq` → tải lại receipts và thiết lập thông báo.

### 7. HTTP và mã lỗi

Response lỗi giữ cấu trúc:

```json
{
  "code": "INVALID_BIO",
  "message": "Bio không được vượt quá 200 ký tự."
}
```

| HTTP | Code |
|---|---|
| 400 | `INVALID_DISPLAY_NAME`, `INVALID_BIO`, `INVALID_ATTACHMENT_COUNT`, `INVALID_RECEIPT_SEQ`, `INVALID_CSRF_TOKEN` |
| 401 | Giữ mã chưa đăng nhập hiện có |
| 403 | Giữ mã quyền truy cập hiện có |
| 404 | `USER_NOT_FOUND`, `ATTACHMENT_NOT_FOUND` |
| 409 | `CLIENT_MESSAGE_ID_CONFLICT`, `ATTACHMENT_ALREADY_USED` |
| 413 | `FILE_TOO_LARGE`, `MESSAGE_ATTACHMENTS_TOO_LARGE` |
| 415 | `UNSUPPORTED_FILE_TYPE` |
| 500 | `INTERNAL_ERROR` — thông báo chung, chi tiết ghi log |

Cookie và CSRF áp dụng cả upload multipart.

### 8. Tìm bạn bè để mở chat — CHAT-30

```text
GET /api/friendships/search?keyword=...&page=1&pageSize=10
```

Yêu cầu đăng nhập; GET không yêu cầu CSRF. Thành công trả 200:

```json
{
  "items": [{"id":"Guid","username":"user01","displayName":"Nguyễn Văn A"}],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "totalPages": 1
}
```

- Chỉ tìm trong quan hệ Friendships Accepted của người gọi, không trả Pending/Rejected/Cancelled hoặc người chưa kết bạn.
- Tìm chuỗi con username hoặc displayName, không phân biệt hoa/thường; trim keyword. Bỏ keyword/rỗng/toàn khoảng trắng trả danh sách bạn bè. Các ký tự `%`, `_`, `\` được hiểu nguyên văn, không làm wildcard.
- Mặc định page=1, pageSize=10; page >= 1, pageSize từ 1–100. Sai khoảng trả `400 INVALID_PAGE`/`INVALID_PAGE_SIZE`; sai kiểu hoặc vượt kiểu Int32 trả `400 VALIDATION_ERROR` theo model validation hiện có.
- Sắp xếp username tăng dần, sau đó id. TotalCount tính sau lọc, trước phân trang; totalPages = ceil(totalCount/pageSize), không có kết quả thì totalPages=0. Trang vượt cuối vẫn 200, giữ page/pageSize và tổng, items rỗng.
- DTO dùng `SearchFriendDto` và `PagedFriendSearchResponse`, chỉ chứa các trường ở ví dụ; không thêm Email/PasswordHash. Không thay response Users/Friendships cũ.
- Mở chat dùng lại `POST /api/conversations/direct`. Tạo mới yêu cầu Accepted và không bị chặn; hội thoại cũ dùng quyền xem lịch sử hiện có. Gửi tin vẫn kiểm tra lại Accepted/Blocks tại Backend, không tin trạng thái vừa lấy từ search.
