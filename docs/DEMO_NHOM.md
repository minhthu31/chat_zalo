# Demo bài nhóm: lấy một link chung

## Bật demo

Trên **một máy chủ của nhóm**, mở Docker Desktop. Trong PowerShell tại thư mục code, chạy:

```powershell
powershell -NoProfile -File deploy/Start-Demo.ps1
```

Chờ terminal in `LINK DUNG CHUNG: https://....trycloudflare.com` (cũng lưu trong `artifacts/demo-url.txt`). Gửi **cùng link này** cho tất cả thành viên. Mỗi người mở link, đăng ký tài khoản riêng, tìm username rồi kết bạn/chat. Không dùng `localhost` trên máy từng người, không cần cùng Wi-Fi.

Database chỉ nằm trên máy chủ trong Docker volume; các client không cần cài PostgreSQL. Tài khoản cũ trong DB riêng của từng máy không tự nhập vào đây. Chức năng nhóm trên UI vẫn cần các task nhóm tương ứng hoàn thành theo Sprint.

## Điều cần nhớ

- **Máy chủ, Docker và Internet phải đang bật.** Tắt máy thì link không dùng được. Link có thể đổi khi tunnel khởi động lại; lúc đó gửi link mới cho nhóm. Đây là demo tạm thời, không phải hosting độc lập 24/7.
- Nhiều client cùng dùng một BE/DB. Đã kiểm tra 3 phiên độc lập qua link Internet, cả WebSocket; chưa kiểm thử tải lớn. Quick Tunnel có giới hạn dịch vụ riêng, không cam kết số người không giới hạn.
- Link là website công khai, chỉ dùng tài khoản/dữ liệu demo. PostgreSQL không mở cổng ra Internet. HTTPS giữa trình duyệt và Cloudflare được xác minh bình thường; bỏ kiểm tra chứng chỉ nội bộ chỉ áp dụng đoạn tunnel tới Caddy trong mạng Docker.
- Không cần Cloudflare account hay tên miền riêng. Muốn chạy độc lập khi máy bạn tắt thì dùng hướng server trong [DEPLOYMENT_CHAT23.md](DEPLOYMENT_CHAT23.md).

## Tắt link công khai

```powershell
docker compose --env-file deploy/.env -f deploy/compose.yml -f deploy/compose.demo.yml stop tunnel
```

Lệnh này không xóa database. Lần sau chạy lại `Start-Demo.ps1`; có thể thêm `-NoBuild` nếu code không đổi.

Nguồn: [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/). Mạng của máy chủ cần cho phép kết nối ra Cloudflare.
