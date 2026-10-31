# Chat_zalo – Chat 1–1 thời gian thực

## Stack đã chốt
- **Frontend:** C#, Blazor WebAssembly .NET 10 (`frontend/Chat.Client.csproj`)
- **Backend:** C#, ASP.NET Core .NET 10 (`backend/Chat.sln`)
- **Database:** PostgreSQL 18, **database `chat_zalo`**
- **ORM:** EF Core 10 Code First + ASP.NET Core Identity `Guid`
- **Realtime:** SignalR `/hubs/chat`; **Auth:** Identity Cookie + CSRF.

## Khung cấu trúc
```text
chat_zalo/
├── frontend/                  # Blazor UI – pages/components/services placeholders
├── backend/                   # Chat.Api, Chat.Application, Chat.Domain, Chat.Infrastructure
├── database/                  # ERD, database rules, migrations và database.sql
├── docs/                      # API contract, FE, Git rules
├── tests/                     # test placeholders
└── docker-compose.yml         # PostgreSQL,, chưa có container FE/BE
```

## Local development
Cần **.NET SDK 10**, Docker và PostgreSQL image. Từ thư mục gốc:
```powershell
docker compose up -d db
dotnet restore backend/Chat.sln
dotnet build backend/Chat.sln
dotnet run --project backend/Chat.Api --urls http://localhost:5080
# terminal khác
dotnet restore frontend/Chat.Client.csproj
dotnet run --project frontend/Chat.Client.csproj
```
- Backend profile `Chat.Api` mặc định chạy `http://localhost:5080` (khớp `frontend/wwwroot/appsettings.json`).
- BE connection string demo `Host=localhost;Port=5432;Database=chat_zalo;Username=chat_zalo;Password=dev_change_me`; cấu hình nằm trong `backend/Chat.Api/appsettings.json`; secrets thật override qua `ConnectionStrings__ChatDb`.
- FE `frontend/wwwroot/appsettings.json`: `ApiBaseUrl=http://localhost:5080/` (công khai). Tên origin FE tùy cổng runtime; đội auth phải cấu hình CORS/Cookie/CSRF trước khi gọi API.
- Backend đã đăng ký Identity Core và DbContext; có `/health`, controllers và OpenAPI trong Development. Controllers/services nghiệp vụ vẫn là placeholders, chưa có luồng chat hoàn chỉnh hoặc SignalR runtime.
- Docker Compose **chỉ tạo DB**, không chạy FE/BE; `docker compose down -v` sẽ xóa volume dữ liệu, chỉ làm khi đã sao lưu hoặc không có dữ liệu cần giữ.

## Phạm vi
- **P0:** đăng ký/đăng nhập/hồ sơ, tìm bạn/kết bạn, chặn, hội thoại 1–1, nhắn tin và lịch sử, realtime notification.
- **P1, chưa giao khi P0 chưa xong:** chat nhóm, upload, typing/receipt.

## Tài liệu phải đọc trước khi code
1. [API Contract v1.1](docs/API_CONTRACT.md) – FE/BE JSON, HTTP, Identity, SignalR.
2. [Database Rules v1.1](database/DATABASE_RULES.md) – schema, invariants và migration ownership.
3. [ERD](database/ERD.mmd) – sơ đồ logic.
4. [Frontend Blazor Guide](docs/FRONTEND_BLAZOR_GUIDE.md) – client/service boundaries.
5. Source chính nằm trong `backend/`, `frontend/`, `tests/` và `database/`; không giữ bản source lồng. Xem `git status` và diff trước khi commit.
6. [Database README](database/README.md) – cách bootstrap database.

