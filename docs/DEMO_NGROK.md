# Demo qua link ngrok cố định

Tài khoản ngrok miễn phí được cấp một dev domain cố định. Tên do ngrok chọn, không tự đổi thành `chatzalo` được.

1. Sao chép `deploy/.env.ngrok.example` thành `deploy/.env.ngrok`.
2. Điền token của tài khoản vào `NGROK_AUTHTOKEN=`. File thật được Git bỏ qua; không chia sẻ token.
3. Giữ `CHAT_DOMAIN=localhost` trong `deploy/.env` của demo hiện tại.
4. Chạy từ thư mục repository:

```powershell
docker compose --env-file deploy/.env -f deploy/compose.yml -f deploy/compose.ngrok.yml up -d ngrok
docker logs chat23-ngrok-1
```

Lấy địa chỉ HTTPS trong dòng `started tunnel` và gửi cùng địa chỉ cho cả nhóm. Nếu trình duyệt hiện trang cảnh báo của ngrok miễn phí, chọn **Visit Site** để tiếp tục.

Máy chủ, Docker và Internet phải hoạt động. Link cố định không biến máy cá nhân thành hosting chạy khi tắt máy. Database và cookie keys dùng lại các volume hiện tại, không tạo database người dùng mới.

Không dùng `down -v` vì sẽ xóa volume dữ liệu. Free plan có giới hạn sử dụng: https://ngrok.com/docs/pricing-limits/free-plan-limits/
