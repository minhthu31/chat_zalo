# Database – chat_zalo

**Nguồn sự thật:** PostgreSQL 18, EF Core Code First, `database/DATABASE_RULES.md` v1.1 và `database/ERD.mmd`.

## Khởi chạy local
1. `docker compose up -d db` (đứng tại gốc `chat_zalo/`).
2. DB `chat_zalo`, username `chat_zalo`, password demo `dev_change_me`; host `localhost:5432` từ máy dev.
3. ConnectionStrings:ChatDb có thể override bằng biến môi trường `ConnectionStrings__ChatDb`. Không lưu mật khẩu thật trong git.
4. Khi thành viên DB triển khai `ChatDbContext`, Identity và EF packages, mới chạy migration:

```powershell
dotnet ef migrations add InitialCreate --project backend/Chat.Infrastructure --startup-project backend/Chat.Api --output-dir Migrations
dotnet ef database update --project backend/Chat.Infrastructure --startup-project backend/Chat.Api
```

Các lệnh migration **chưa thể chạy trên skeleton** vì DbContext và đăng ký DI chưa được implement. Cài dotnet-ef đồng version EF Core 10; `--output-dir Migrations` tính theo Chat.Infrastructure.

## Lưu ý quan trọng
- Không dùng `EnsureCreated()` và migrations đồng thời. Không tạo SQL init schema ở `database/schema/` để tranh quyền với Code First.
- Khi thay đổi `POSTGRES_DB`, `POSTGRES_USER` trong Compose sau khi volume có dữ liệu, biến env **không tự rename DB/user cũ**. Nếu là DB thử nghiệm không có dữ liệu cần giữ, chạy `docker compose down -v` rồi `docker compose up -d db` (lệnh `down -v` xóa toàn bộ dữ liệu DB trong volume, phải sao lưu trước).
- Mọi entity, constraint, seed và migration phải qua PR do Leader và owner DB duyệt.
