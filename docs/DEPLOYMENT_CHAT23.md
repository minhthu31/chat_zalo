# CHAT-23: triển khai nhiều client dùng chung server

Một địa chỉ HTTPS phục vụ Blazor, `/api/*` và `/hubs/*`. Các máy/người dùng truy cập cùng website; chỉ backend kết nối PostgreSQL. Không đổi API/DTO/schema hoặc quy tắc nghiệp vụ của Sprint 1/2. Chức năng nhóm chỉ có thể sử dụng trên UI khi các task nhóm tương ứng hoàn thành.

## Chạy trên server Internet

Cần Linux x64 có Docker Engine + Compose, một hostname trỏ DNS tới server, và cổng 80/443 mở. Không cần mở cổng database hoặc API ra Internet. Chưa có server/hostname thì bộ này chỉ có thể được chạy kiểm chứng local, chưa phải website Internet.

Từ thư mục repository:

```sh
cp deploy/.env.example deploy/.env
# Sửa CHAT_DOMAIN thành hostname thật, POSTGRES_PASSWORD thành mật khẩu riêng.
docker compose --env-file deploy/.env -f deploy/compose.yml up -d --build
docker compose --env-file deploy/.env -f deploy/compose.yml ps -a
```

Mở `https://<CHAT_DOMAIN>` trên các máy khác nhau, đăng ký tài khoản tại đây rồi tìm nhau/kết bạn/chat. Không dùng `localhost` trên máy của từng người. Caddy tự cấp/gia hạn chứng chỉ khi DNS và cổng mạng đúng. REST và SignalR dùng cùng origin; không phải sửa URL theo từng client. Cookie production giữ HttpOnly/Secure; CSRF vẫn bắt buộc. Backend chỉ tin forwarded headers từ IP proxy được cấu hình trong mạng Docker.

Để kiểm thử local có thể đặt `CHAT_DOMAIN=localhost`. Caddy dùng CA nội bộ; trình duyệt có thể yêu cầu tin cậy chứng chỉ local. Đây không phải địa chỉ mà máy khác có thể dùng qua Internet. Không dùng chứng chỉ local này cho server public.

## Database và cập nhật code

Stack tạo database `chat_zalo` riêng trong volume `chat23_postgres`. Dữ liệu/tài khoản trong PostgreSQL local của các thành viên không tự nhập vào đây. Migration chạy bằng service một lần `migrate`; API chỉ khởi động khi migration thành công. CHAT-12 repair được giữ nguyên: nếu dữ liệu nhóm cũ không hợp lệ, dừng và sửa dữ liệu theo lỗi trước khi chạy lại, không bỏ trigger.

Trước cập nhật, sao lưu database; sau đó lấy code và chạy lại lệnh `up -d --build` ở trên. Kiểm tra `migrate` kết thúc exit 0 và `/health` trả `{"status":"ok"}`. Có thể xem lỗi bằng:

```sh
docker compose --env-file deploy/.env -f deploy/compose.yml logs migrate api web
```

Volume `keys` giữ Data Protection keys để cookie không mất hiệu lực chỉ vì tạo lại container API. Volume Caddy giữ chứng chỉ. `docker compose ... down` giữ volume; không dùng `down -v` nếu muốn giữ dữ liệu. Nếu đổi mật khẩu database đã tồn tại, phải đổi password role PostgreSQL tương ứng; thay `.env` riêng không đổi password role cũ.

Ví dụ backup trên Linux (thư mục backup nằm ngoài Git):

```sh
mkdir -p backups
docker compose --env-file deploy/.env -f deploy/compose.yml exec -T db pg_dump -U chat_zalo -d chat_zalo -Fc > backups/chat_zalo.dump
```

Test tự động tiếp tục dùng `chat_zalo_test` riêng (CI đã tạo), không trỏ test vào database người dùng. Đăng nhập và kiểm tra hai/nhiều client trên cùng website là kiểm thử tích hợp bản triển khai.

Có thể kiểm tra bản triển khai bằng Python (tạo 3 tài khoản demo và tin nhắn riêng, không xóa dữ liệu hiện có):

```sh
python -m venv artifacts/smoke-env
artifacts/smoke-env/bin/pip install -r deploy/requirements-smoke.txt
artifacts/smoke-env/bin/python deploy/smoke.py https://chat.example.com
# Chỉ khi kiểm tra chứng chỉ nội bộ của localhost:
# artifacts/smoke-env/bin/python deploy/smoke.py https://localhost --insecure-localhost
```

Bộ smoke kiểm tra cookie/CSRF thật, tìm kiếm/phân trang, chuỗi bạn bè của 3 người, gửi/chấp nhận/hủy bạn, Direct chat, WebSocket qua proxy, block/unblock và thu hồi hai phiên khi đổi mật khẩu. Không thay thế kiểm thử tải nhiều người đồng thời hoặc test UI/nhóm của các task sau.

## Phạm vi và kiểm chứng

Nhánh `feature/CHAT-23-deployment` dùng một API instance, hỗ trợ nhiều client cùng truy cập. Chưa kiểm thử tải lớn và chưa hỗ trợ scale ngang nhiều API instance: tracker SignalR hiện lưu trong bộ nhớ, cần backplane/distributed tracker nếu mở rộng về sau. Không đổi mục tiêu các task nghiệp vụ hiện có.

Kết quả kiểm chứng local trên nền `main` commit `118727c`:

- Backend Release build qua; 171/171 backend test trên PostgreSQL test riêng và 13/13 frontend test qua.
- Docker build và chạy thực tế cả FE Caddy, API Production, PostgreSQL 18.6 và migration runner; migration runner áp dụng được toàn bộ migration lên DB test rỗng riêng trong container.
- Smoke HTTPS qua với 3 client độc lập, cả WebSocket và toàn bộ luồng liệt kê phía trên. Cookie/data vẫn dùng được sau khi tạo lại container API.
- `.env` local và dữ liệu/key/certificate không đưa lên Git. Workflow Deployment CI chạy lại build + smoke trên stack riêng khi mở PR.
- Chưa triển khai Internet, chưa kiểm chứng từ nhiều máy vật lý: cần VPS/hosting và hostname thật. `https://localhost` chỉ là bản chạy thật tại máy, dùng chứng chỉ CA nội bộ.

Tài liệu kỹ thuật: [ASP.NET Core forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [EF migration deployment](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying), [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https), [Npgsql GSSAPI](https://www.npgsql.org/doc/security.html).
