# Database – chat_zalo

Schema được quản lý bằng EF Core migrations trong `backend/Chat.Infrastructure/Migrations`.
`database.sql` là bản xuất tự động từ migrations, không sửa schema trực tiếp trong file SQL.

## Khởi tạo

Từ thư mục gốc repository (nơi có `backend/` và `database/`):

```powershell
docker compose up -d db
$env:ConnectionStrings__ChatDb = 'Host=localhost;Port=5432;Database=chat_zalo;Username=chat_zalo;Password=dev_change_me'
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api
```

Hoặc import SQL vào database PostgreSQL 18 trống đã tạo sẵn:

```powershell
psql -X -h localhost -U chat_zalo -d chat_zalo -v ON_ERROR_STOP=1 -f database/database.sql
```

Script có kiểm tra lịch sử migration để có thể chạy lại. Script không tạo database/user và không chứa dữ liệu người dùng.
Không chạy `EnsureCreated()` trước migrations. Không dùng SQL đã dán cũ: bản đó xóa constraints và tạo khóa ngoại `ConversationId1`.

## Xuất lại SQL

```powershell
dotnet ef migrations script --idempotent --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --output database/database.sql
```

Tên file migration bỏ tiền tố ngày giờ theo yêu cầu. Giá trị `[Migration("...")]` giữ nguyên để EF sắp xếp và nhận diện lịch sử.
`RemoveConstraintsAndAddShadowRelationships.cs` là migration cũ có tên nội bộ `AddUniqueEmailConstraint`; giữ lại để tương thích lịch sử, sau đó dùng các migrations khắc phục.

## Kiểm tra database

Chỉ chạy test trên database thử riêng đã áp dụng migrations; test có ghi dữ liệu.

```powershell
$env:CHAT_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=chat_zalo_test;Username=chat_zalo;Password=YOUR_LOCAL_PASSWORD'
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --connection "$env:CHAT_TEST_CONNECTION_STRING"
dotnet test backend/Chat.sln
```

Kết quả xác minh source chính trên PostgreSQL 18.6: 17/17 test đạt.
Xem [báo cáo CHAT-1](../PRE_PUSH_REVIEW.md) và [schema thực tế](CHAT1_SCHEMA_EVIDENCE.txt).
Rules trong repo hiện là v1.1; cần bản v1.2 của leader để xác nhận phần chênh lệch.

Mật khẩu Compose chỉ dùng demo local. Với tài khoản riêng, cấu hình bằng biến môi trường; không commit mật khẩu thật.
Không áp dụng migration lên database có dữ liệu trước khi sao lưu và kiểm tra: migration cũ thêm khóa ngoại mặc định rỗng có thể thất bại nếu bảng đã có dữ liệu.
