# DATABASE RULES

**Phiên bản:** 1.1  
**Phạm vi:** P0 – Chat cá nhân 1–1  
**Áp dụng:** Toàn bộ nhóm phát triển chat_zalo

## 1. Công nghệ thống nhất

| Hạng mục | Quy định |
|---|---|
| Database | PostgreSQL 18 |
| Tên Database | `chat_zalo` |
| ORM | Entity Framework Core 10 |
| Migration | EF Core Code First |
| Primary Key | UUID (`Guid`) |
| Thời gian | UTC (`DateTimeOffset`) |
| Authentication | ASP.NET Core Identity + Cookie |

- `ApplicationUser : IdentityUser<Guid>` nằm trong `Chat.Infrastructure/Identity/`.
- `ChatDbContext` kế thừa `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- Không tạo bảng Users thứ hai.

## 2. Các bảng Database

| Bảng | Khóa chính | Chức năng |
|---|---|---|
| Users | Id | Quản lý tài khoản |
| Friendships | UserLowId, UserHighId | Quan hệ bạn bè |
| UserBlocks | BlockerUserId, BlockedUserId | Chặn người dùng |
| Conversations | Id | Quản lý hội thoại |
| DirectConversations | ConversationId | Hội thoại cá nhân |
| ConversationMembers | ConversationId, UserId | Thành viên hội thoại |
| Messages | Id | Lưu tin nhắn |

Trong bảng `Users`, `Id` là khóa của `ApplicationUser : IdentityUser<Guid>`; cần thêm `DisplayName`. `NormalizedUserName` và `NormalizedEmail` phải có unique index (nếu yêu cầu email duy nhất, Identity mặc định không bảo đảm unique email). `Friendships.RequesterUserId` xác định người gửi lời mời, phải thuộc cặp `UserLowId/UserHighId`.

Ngoài ra sử dụng các bảng Identity: `Roles`, `UserRoles`, `UserClaims`, `RoleClaims`, `UserLogins`, `UserTokens`.

## 3. Khóa ngoại và ràng buộc

| Bảng | Quy định |
|---|---|
| Users | Username, Email duy nhất sau chuẩn hóa |
| Friendships | FK đến Users; `UserLowId < UserHighId`; PK kép |
| UserBlocks | FK đến Users; không tự chặn; PK kép |
| Conversations | FK CreatedByUserId → Users; LastMessageSeq >= 0 |
| DirectConversations | FK ConversationId → Conversations; UNIQUE(UserLowId, UserHighId) |
| ConversationMembers | FK đến Conversations và Users; PK kép |
| Messages | FK đến Conversations và Users; UNIQUE(ConversationId, Seq); UNIQUE(ConversationId, SenderUserId, ClientMessageId) |

Các FK và CHECK phải được cấu hình trong EF Core, không chỉ ghi trong ERD.

## 4. Quy tắc nghiệp vụ

### Kết bạn
- Không tự kết bạn.
- Một cặp người dùng chỉ có một quan hệ bạn bè.
- Trạng thái: `Pending`, `Accepted`, `Rejected`, `Cancelled`.
- Chỉ người nhận mới được chấp nhận hoặc từ chối lời mời.

### Chặn người dùng
- Không tự chặn.
- Người bị chặn không thể gửi tin nhắn riêng mới.
- Backend phải kiểm tra chặn ở cả hai chiều.

### Hội thoại
- Một cặp người dùng chỉ có một hội thoại 1–1.
- Chỉ tạo hội thoại mới khi hai người đã kết bạn và không chặn nhau.
- Mỗi hội thoại cá nhân có đúng hai thành viên.

### Tin nhắn
- Chỉ thành viên hợp lệ được gửi và xem tin.
- Nội dung từ 1–4.000 ký tự sau khi kiểm tra hợp lệ.
- `Seq` tăng theo từng hội thoại.
- Retry cùng `ClientMessageId` và cùng nội dung không được tạo tin trùng.
- Nếu dùng lại `ClientMessageId` với nội dung khác, trả HTTP `409`.
- `LastReadSeq` và `LastDeliveredSeq` không được giảm và không vượt quá `LastMessageSeq`; `LastReadSeq <= LastDeliveredSeq`.


## 5. Quy ước vật lý và nhất quán dữ liệu (v1.1)
- EF Core Identity là một nguồn user duy nhất. `Chat.Domain/Entities/User.cs` hiện là placeholder: **không triển khai thành entity thứ hai**. Tên C# được chốt `ApplicationUser` trong Infrastructure; `Users` là tên bảng được cấu hình qua `ToTable("Users")`.
- Tên các bảng nghiệp vụ cố định như bảng mục 2, Identity tables có thể phải map `ToTable` tương ứng. `DisplayName` là field cần thiết cho `UserDto`.
- `Friendships` có `RequesterUserId` (FK Users), phải là một trong hai ID của cặp; `Status` là chuỗi PascalCase. Cặp ID chuẩn hóa theo thứ tự GUID, unique/PK bất kể chiều gửi.
- DirectConversations phải tham chiếu một `Conversation` với Kind=`Direct`, unique cặp UserLowId/UserHighId. Tạo hội thoại, 2 members trong cùng transaction.
- Atomicity: gán `Seq = LastMessageSeq + 1`, cập nhật Conversations.LastMessageSeq và insert Message trong một transaction hoặc cơ chế concurrency-safe tương đương. Tránh SELECT MAX + insert không khóa.
- `ClientMessageId` unique theo `(ConversationId, SenderUserId, ClientMessageId)`; body giống khi retry trả tin đã lưu, khác trả `409`. `CreatedAt` lưu UTC (`timestamptz`).
- Chỉ thành viên hội thoại có quyền đọc, áp dụng chính sách chặn của `API_CONTRACT.md`; chặn không xóa tin đã lưu.
- Migrations chỉ một owner tại thời điểm làm; commit cùng code entity/config và review của leader; không có raw SQL schema chạy song song với Code First.
- `ERD.mmd` là sơ đồ logic, chưa phải EF Migration; **chưa có triển khai `ChatDbContext`, entity và migration**, nhóm DB phải xây dựng theo tài liệu.
- Local: `Database=chat_zalo`, `Username=chat_zalo`, `Password=dev_change_me` chỉ là thông tin demo trên máy dev; môi trường thật dùng secret từ biến môi trường, không commit mật khẩu thật.
