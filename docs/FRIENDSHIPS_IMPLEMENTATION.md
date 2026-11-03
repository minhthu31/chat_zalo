# CHAT – Friendship Backend (S1)

**Phạm vi:** nghiệp vụ kết bạn trong `backend/`  
**Nguồn hợp đồng:** `docs/API_CONTRACT.md` v1.2 và `database/DATABASE_RULES.md` v1.2  
**Không thuộc phạm vi:** sửa schema, tự tạo bảng Users, frontend Blazor, Authentication/Identity, Blocks, Conversations implementation.

## 1. Các file đã triển khai

| File                                                               | Trách nhiệm                                                                                               |
| ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| `backend/Chat.Api/Controllers/FriendshipsController.cs`            | 6 HTTP endpoints, `[Authorize]`, lấy user hiện tại từ Claims, chuẩn hóa lỗi JSON                          |
| `backend/Chat.Application/DTOs/Friendships/FriendRequestDto.cs`    | Body `targetUserId`                                                                                       |
| `backend/Chat.Application/DTOs/Friendships/FriendshipDto.cs`       | DTO bạn bè/lời mời; thêm `FriendshipRequestsDto` cho incoming/outgoing                                    |
| `backend/Chat.Application/Interfaces/IFriendshipService.cs`        | Giữ nguyên 5 chữ ký đã chốt trong contract v1.2                                                           |
| `backend/Chat.Application/Interfaces/IFriendshipQueryService.cs`   | Interface truy vấn GET, tách khỏi interface nghiệp vụ đã chốt                                             |
| `backend/Chat.Application/Services/FriendshipService.cs`           | Quy tắc nghiệp vụ, phân quyền, trạng thái, chống gửi trùng                                                |
| `backend/Chat.Application/Services/FriendshipException.cs`         | Business error code + HTTP status                                                                         |
| `backend/Chat.Application/Interfaces/IFriendshipRepository.cs`     | Persistence abstraction để Application không phụ thuộc EF/Infrastructure                                  |
| `backend/Chat.Infrastructure/Repositories/FriendshipRepository.cs` | EF Core/PostgreSQL queries, insert chống race, chuyển trạng thái nguyên tử và kiểm tra generation lời mời |
| `backend/Chat.Api/Program.cs`                                      | Đăng ký DI service và repository                                                                          |
| `tests/backend/Chat.Database.Tests/FriendshipServiceTests.cs`      | Unit tests cho các nhánh nghiệp vụ                                                                        |
| `docs/API_CONTRACT.md`                                             | Bổ sung bảng mã lỗi Friendships, không đổi endpoint/DTO contract                                          |

Không sửa `Friendship` entity, `FriendshipConfiguration`, `ChatDbContext` hoặc migration vì schema hiện tại đã đáp ứng nghiệp vụ.

## 2. Thiết kế và quy tắc

### 2.1 Chuẩn hóa cặp người dùng

Database có khóa chính kép `(UserLowId, UserHighId)` và CHECK `UserLowId < UserHighId`. Trước mọi truy vấn, service chuẩn hóa hai GUID bằng `Guid.CompareTo`. Vì vậy A-B và B-A luôn trỏ cùng một hàng.

`RequesterUserId` là nguồn xác định người gửi; không suy ra người gửi từ Low/High.

### 2.2 Gửi lời mời

1. Kiểm tra caller đã xác thực và `targetUserId` khác Guid.Empty.
2. Từ chối nếu caller và target giống nhau (`400 SELF_FRIEND_REQUEST`).
3. Kiểm tra target tồn tại (`404 USER_NOT_FOUND`).
4. Tìm quan hệ theo cặp Low/High.
5. Nếu `Pending`: trả `409 FRIEND_REQUEST_ALREADY_EXISTS`.
6. Nếu `Accepted`: trả `409 ALREADY_FRIENDS`.
7. Nếu `Rejected`/`Cancelled`: cập nhật lại cùng hàng thành `Pending`, requester là caller.
8. Nếu chưa có hàng: insert `Pending`. Composite PK là chốt chặn cuối khi hai request tạo cùng lúc; unique violation được ánh xạ về `409 FRIEND_REQUEST_ALREADY_EXISTS`.
9. Trả `201` và `FriendshipDto`, trong đó `userId` là target.

### 2.3 Chấp nhận / từ chối

`{userId}` trong URL luôn là ID người gửi lời mời. Service chỉ cho phép caller là người nhận. Người gửi tự gọi accept/reject nhận `403 ONLY_RECIPIENT_CAN_RESPOND`; người thứ ba không tìm thấy quan hệ phù hợp và nhận 404. Chỉ trạng thái `Pending` mới được chuyển thành `Accepted` hoặc `Rejected`.

### 2.4 Hủy

- `Pending`: chỉ `RequesterUserId` được gọi DELETE để chuyển `Cancelled`.
- `Accepted`: một trong hai người có thể hủy kết bạn, chuyển `Cancelled`.
- `Rejected`/`Cancelled`: không hủy lại.
- Không xóa hàng vật lý; giữ lịch sử trạng thái và bảo toàn invariant một cặp một hàng.

### 2.5 Chống cập nhật nhầm request cũ (optimistic concurrency)

Mỗi lần tạo lại lời mời sau `Rejected`/`Cancelled`, repository cập nhật `RequesterUserId` và `CreatedAt`. Hai thao tác `TryReopenAsync` và `TryChangeStatusAsync` dùng UPDATE có điều kiện đồng thời trên:

- `UserLowId`, `UserHighId`;
- trạng thái mong đợi;
- `RequesterUserId` của snapshot đã đọc;
- `CreatedAt` của đúng thế hệ lời mời.

Do đó request cũ không thể cập nhật một lời mời mới chỉ vì cặp người dùng và trạng thái tình cờ giống nhau (ví dụ Pending → Cancelled → Pending). UPDATE phải tác động đúng một hàng; nếu không, service trả lỗi xung đột 409.

### 2.6 Kiểm tra quyền chat

`AreFriendsAsync(userAId, userBId)` trả `true` khi và chỉ khi tồn tại hàng của cặp chuẩn hóa có `Status == "Accepted"`. Hàm trả false với Guid rỗng, cùng một user, Pending, Rejected hoặc Cancelled.

Conversation service có thể inject `IFriendshipService` và gọi:

```csharp
var allowed = await _friendshipService.AreFriendsAsync(currentUserId, targetUserId);
if (!allowed)
{
    // Từ chối tạo direct conversation theo contract.
}
```

Không gọi trực tiếp DbContext hoặc truy vấn bảng Friendships từ Controller Conversations.

## 3. API và kết quả

| Method | URL                                | Thành công                       |
| ------ | ---------------------------------- | -------------------------------- |
| POST   | `/api/friendships`                 | `201` + FriendshipDto            |
| GET    | `/api/friendships`                 | `200` + `FriendshipDto[]`        |
| GET    | `/api/friendships/requests`        | `200` + `{ incoming, outgoing }` |
| POST   | `/api/friendships/{userId}/accept` | `204`                            |
| POST   | `/api/friendships/{userId}/reject` | `204`                            |
| DELETE | `/api/friendships/{userId}`        | `204`                            |

Tất cả endpoint yêu cầu cookie đăng nhập. Các request POST/DELETE còn phải gửi CSRF token theo quy định chung trong API contract: `X-CSRF-TOKEN`. Không truyền current user ID từ body; API lấy từ Claim `NameIdentifier`.

## 4. Khởi động dự án trên Windows / PowerShell

### Điều kiện

- .NET SDK 10.
- Docker Desktop đang chạy.
- PostgreSQL 18 chạy bằng Docker Compose.
- PowerShell mở tại thư mục gốc repository (thư mục chứa `docker-compose.yml`, `backend/`, `database/`).

Kiểm tra:

```powershell
dotnet --version
docker --version
docker compose version
```

`dotnet --version` cần trả về SDK `10.x`.

### Bước 1 – Khởi động PostgreSQL

```powershell
docker compose up -d db
docker compose ps
```

Đợi container `db` ở trạng thái healthy. Xem log nếu chưa sẵn sàng:

```powershell
docker compose logs -f db
```

Thoát xem log bằng `Ctrl + C` (không dừng container).

### Bước 2 – Cấu hình connection string local

`backend/Chat.Api/appsettings.json` đang có cấu hình demo:

```text
Host=localhost;Port=5432;Database=chat_zalo;Username=chat_zalo;Password=dev_change_me
```

Nếu bạn dùng đúng Docker Compose mặc định thì có thể dùng cấu hình này. Nếu đã đổi password, đặt biến môi trường trong PowerShell:

```powershell
$env:ConnectionStrings__ChatDb = "Host=localhost;Port=5432;Database=chat_zalo;Username=chat_zalo;Password=YOUR_LOCAL_PASSWORD"
```

Không commit mật khẩu thật lên Git.

### Bước 3 – Cài EF tool và cập nhật database

Chỉ cần cài tool một lần:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
```

Nếu máy đã cài:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.14
```

Áp dụng migration hiện có:

```powershell
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --context ChatDbContext
```

Không tạo migration mới cho Friendships vì entity, bảng, khóa chính, CHECK và FK đã tồn tại trong schema. Không chạy `EnsureCreated()`.

### Bước 4 – Restore và build

```powershell
dotnet restore backend/Chat.sln
dotnet build backend/Chat.sln
```

Nếu build thành công, cuối output cần có `Build succeeded`.

### Bước 5 – Chạy Backend API

```powershell
dotnet run --project backend/Chat.Api --urls http://localhost:5080
```

Giữ terminal này mở. Kiểm tra ở terminal PowerShell khác:

```powershell
Invoke-RestMethod http://localhost:5080/health
```

Kết quả mong đợi:

```json
{
  "status": "ok"
}
```

OpenAPI trong Development: `http://localhost:5080/openapi/v1.json` (nếu cấu hình OpenAPI của project đang bật).

### Bước 6 – Chạy Frontend (nếu cần)

Mở terminal thứ hai tại thư mục gốc:

```powershell
dotnet restore frontend/Chat.Client.csproj
dotnet run --project frontend/Chat.Client.csproj
```

Đọc địa chỉ Blazor được in ra terminal. Frontend hiện là project riêng; API có thể kiểm thử độc lập bằng PowerShell/Postman.

## 5. Kiểm thử API bằng PowerShell

Do API bật Cookie Authentication và CSRF, không chỉ gửi một request POST đơn lẻ. Phải giữ cookie trong cùng `WebRequestSession`.

### 5.1 Tạo session và lấy CSRF token

```powershell
$baseUrl = "http://localhost:5080"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$csrf = Invoke-RestMethod `
  -Uri "$baseUrl/api/auth/csrf" `
  -Method GET `
  -WebSession $session

$csrfToken = $csrf.token
```

### 5.2 Đăng nhập

Dùng một tài khoản đã đăng ký. Ví dụ:

```powershell
$loginBody = @{
    usernameOrEmail = "user01"
    password = "YOUR_PASSWORD"
} | ConvertTo-Json

$login = Invoke-RestMethod `
  -Uri "$baseUrl/api/auth/login" `
  -Method POST `
  -ContentType "application/json" `
  -Headers @{ "X-CSRF-TOKEN" = $csrfToken } `
  -Body $loginBody `
  -WebSession $session
```

Sau login, lấy CSRF token mới vì token gắn với danh tính hiện tại:

```powershell
$csrf = Invoke-RestMethod `
  -Uri "$baseUrl/api/auth/csrf" `
  -Method GET `
  -WebSession $session
$csrfToken = $csrf.token
```

### 5.3 Gửi lời mời

Thay GUID bằng `Id` của tài khoản đích:

```powershell
$targetUserId = "GUID-USER-B"

$body = @{
    targetUserId = $targetUserId
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "$baseUrl/api/friendships" `
  -Method POST `
  -ContentType "application/json" `
  -Headers @{ "X-CSRF-TOKEN" = $csrfToken } `
  -Body $body `
  -WebSession $session
```

Mong đợi HTTP 201 và body có `userId`, `username`, `displayName`, `status = Pending`.

### 5.4 Xem bạn bè và lời mời

```powershell
Invoke-RestMethod -Uri "$baseUrl/api/friendships" -Method GET -WebSession $session

Invoke-RestMethod -Uri "$baseUrl/api/friendships/requests" -Method GET -WebSession $session
```

### 5.5 Chấp nhận / từ chối / hủy

Đối với accept/reject, `userId` phải là ID người gửi lời mời:

```powershell
$requesterId = "GUID-USER-A"

Invoke-RestMethod `
  -Uri "$baseUrl/api/friendships/$requesterId/accept" `
  -Method POST `
  -Headers @{ "X-CSRF-TOKEN" = $csrfToken } `
  -WebSession $session
```

Từ chối:

```powershell
Invoke-RestMethod `
  -Uri "$baseUrl/api/friendships/$requesterId/reject" `
  -Method POST `
  -Headers @{ "X-CSRF-TOKEN" = $csrfToken } `
  -WebSession $session
```

Hủy lời mời đã gửi hoặc hủy kết bạn:

```powershell
Invoke-RestMethod `
  -Uri "$baseUrl/api/friendships/$targetUserId" `
  -Method DELETE `
  -Headers @{ "X-CSRF-TOKEN" = $csrfToken } `
  -WebSession $session
```

`204 No Content` là thành công; PowerShell có thể không in body.

## 6. Test và giới hạn xác minh

### Unit tests hiện có

`FriendshipServiceTests.cs` kiểm tra các nhánh nghiệp vụ service, bao gồm tự kết bạn, lời mời trùng, phân quyền accept, accept/reject, hủy lời mời, mở lại quan hệ và request generation cũ. `FriendshipsApiTests.cs` kiểm tra đủ 6 route, cookie xác thực, CSRF thiếu/sai/hợp lệ, Claim identity, status code và DI. Chạy:

```powershell
dotnet test tests/backend/Chat.Database.Tests/Chat.Database.Tests.csproj --filter FullyQualifiedName~Friendship
```

### Build và toàn bộ test

```powershell
dotnet restore backend/Chat.sln
dotnet build backend/Chat.sln
dotnet test backend/Chat.sln
```

### PostgreSQL 18 test database

Tạo database riêng, không dùng database có dữ liệu phát triển:

```powershell
$env:CHAT_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=chat_zalo_test;Username=chat_zalo;Password=YOUR_LOCAL_PASSWORD"
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --context ChatDbContext --connection "$env:CHAT_TEST_CONNECTION_STRING"
dotnet test tests/backend/Chat.Database.Tests/Chat.Database.Tests.csproj
```

**Trạng thái xác minh của bản sửa này:** môi trường hiện tại không có .NET SDK hoặc PostgreSQL runtime, nên chưa thể thực thi `dotnet build`, `dotnet test`, HTTP integration tests hoặc PostgreSQL 18 concurrency tests. Không được xem các hạng mục đó là PASS cho đến khi chạy trên máy phát triển/CI có .NET 10 và PostgreSQL 18. HTTP integration tests dùng fake application service để kiểm tra pipeline Cookie/CSRF/controller và DI; chúng không thay thế PostgreSQL repository/concurrency integration tests thật. Các test PostgreSQL 18 thật vẫn chưa được xác minh.

## 7. Git branch và Pull Request

Từ thư mục gốc:

```powershell
git status
git switch feature/CHAT-4-friendships
git add backend/Chat.Api/Controllers/FriendshipsController.cs backend/Chat.Api/Program.cs backend/Chat.Application/DTOs/Friendships backend/Chat.Application/Interfaces/IFriendshipService.cs backend/Chat.Application/Interfaces/IFriendshipRepository.cs backend/Chat.Application/Services/FriendshipService.cs backend/Chat.Application/Services/FriendshipException.cs backend/Chat.Infrastructure/Repositories/FriendshipRepository.cs tests/backend/Chat.Database.Tests docs/API_CONTRACT.md docs/FRIENDSHIPS_IMPLEMENTATION.md
git diff --cached
git commit -m "feat(friendships): implement friend request workflows"
git push -u origin feature/CHAT-4-friendships
```

Sau khi push, tạo Pull Request theo `docs/GIT_RULES.md`, target branch là `main` theo yêu cầu Leader; không tự merge. PR cần nêu: 6 endpoints, error codes, test cases, build result và xác nhận không đổi schema.

## 8. Checklist nghiệm thu

- [ ] Đủ 6 endpoint và đúng HTTP status.
- [ ] DTO/JSON camelCase khớp API contract v1.2.
- [ ] Không tự kết bạn.
- [ ] Không tạo hai hàng cho một cặp người dùng.
- [ ] Pending/Accepted/Rejected/Cancelled đúng nghiệp vụ.
- [ ] Chỉ người nhận được accept/reject.
- [ ] Chỉ requester được hủy lời mời Pending.
- [ ] Hai bên Accepted đều có thể hủy kết bạn.
- [ ] GET friends chỉ trả Accepted.
- [ ] GET requests trả đúng incoming/outgoing và `userId` là đối phương.
- [ ] `AreFriendsAsync` chỉ true khi Accepted.
- [ ] Không sửa schema/migration ngoài phạm vi.
- [ ] `dotnet build` thành công.
- [ ] Unit tests, HTTP integration tests và PostgreSQL 18 concurrency tests chạy thành công (hiện chưa được xác minh).
- [ ] Push branch và tạo PR để Leader review.
