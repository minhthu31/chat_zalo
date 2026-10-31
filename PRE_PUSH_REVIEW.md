# CHAT-1 Database / Identity — xác minh 2026-10-31

Phạm vi: Entity, Identity, EF Configurations, migrations và database tests.
Tất cả lệnh chạy từ gốc repository trên backend/Chat.sln. Không sử dụng kết quả source lồng.

## Thay đổi

- ApplicationUser kế thừa IdentityUser<Guid>, là entity user duy nhất, map vào Users.
- ChatDbContext gọi base.OnModelCreating rồi ApplyConfigurationsFromAssembly. Đã bỏ mapping lặp trong DbContext.
- Default bigint dùng 0L. Model khớp snapshot, không cần migration rỗng hay sửa migration lịch sử.
- Bổ sung 11 trường hợp test xác nhận SQLSTATE và tên constraint: unique email, FK user, requester/status, kind, sequence không âm, role và read/delivered. Test mới rollback transaction.
- Test atomic sequence chạy ba worker đồng thời với DbContext/transaction riêng và FOR UPDATE; kiểm tra sequence 1, 2, 3 và counter cuối bằng 3.

## Bằng chứng source chính

Database thử tạo mới, trống: chat_zalo_chat1_review_20261031 tại localhost:5433.
SHOW server_version trả 18.6.

| Lệnh/bước | Kết quả |
|---|---|
| dotnet build backend/Chat.sln --no-restore --verbosity minimal | 0 warnings, 0 errors |
| dotnet ef dbcontext info --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --no-build | Chat.Infrastructure.Data.ChatDbContext, provider Npgsql |
| dotnet ef migrations has-pending-model-changes --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --no-build | No changes have been made to the model since the last migration |
| dotnet ef database update với --connection trỏ database trống | Áp dụng đủ 4 migrations, Done |
| dotnet test backend/Chat.sln --no-build --no-restore --verbosity minimal | 17 passed, 0 failed, 0 skipped |

Migrations đã áp dụng:

1. 20261029192059_InitialCreate
2. 20261030104611_AddUniqueEmailConstraint
3. 20261030151320_RestoreConversationRelationshipsAndConstraints
4. 20261030151359_FixConversationForeignKeys

Có 13 bảng ứng dụng/Identity và __EFMigrationsHistory.
[Schema truy vấn trực tiếp từ PostgreSQL](database/CHAT1_SCHEMA_EVIDENCE.txt) ghi danh sách bảng, migration history, định nghĩa PK/FK/CHECK và unique indexes.
Schema cuối không còn FK/cột shadow ConversationId1.

## Chạy lại

Tạo database thử trống trên PostgreSQL 18, thay thông tin kết nối rồi chạy từ gốc repository:

```powershell
$env:CHAT_TEST_CONNECTION_STRING = 'Host=localhost;Port=5433;Database=chat_zalo_test;Username=chat_zalo;Password=YOUR_LOCAL_PASSWORD'
dotnet build backend/Chat.sln
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --connection "$env:CHAT_TEST_CONNECTION_STRING"
dotnet ef migrations has-pending-model-changes --project backend/Chat.Infrastructure --startup-project backend/Chat.Api
dotnet test backend/Chat.sln --no-build
```

## Giới hạn nghiệm thu

- Repo chỉ có DATABASE_RULES.md v1.1 và ERD hiện có. Cần bản v1.2 của leader để đối chiếu; không tự đổi số phiên bản để coi là đã đáp ứng.
- Quy tắc liên bảng/luồng nghiệp vụ như đúng hai thành viên, DirectConversations thuộc hội thoại Direct, read/delivered không vượt counter hội thoại và không được giảm chưa được bảo đảm bởi CHECK hiện tại. Cần đối chiếu v1.2 để chốt trách nhiệm DB hay tầng ứng dụng. Không triển khai API/chat trong CHAT-1.
- Test sequence xác minh mẫu transaction tầng DB, không chứng minh API gửi tin.
- Source lồng đã xóa ở working tree. Chưa commit/push; khi tạo PR cần bao gồm file mới backend/tests/database và deletion source lồng.
