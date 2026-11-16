# CI cho chat_zalo (CHAT-21)

Hai workflow độc lập chạy trên Pull Request vào `main` (khi mở, cập nhật code hoặc mở lại PR) và khi push vào `main`:

| Check giữ nguyên từ Sprint 1 | Các bước chính |
| --- | --- |
| `Backend CI / build` | .NET 10: restore → build → migration → toàn bộ test Backend trên PostgreSQL 18 |
| `Frontend CI / build` | .NET 10: restore → build Blazor WebAssembly → restore và chạy test FE |

Mỗi workflow hủy lần chạy cũ khi cùng PR có commit mới. Backend và Frontend không phụ thuộc kết quả của nhau. Tên workflow và job `build` được giữ nguyên để tiếp tục dùng các check hiện có.

## Database và thông tin kết nối

- Backend tạo container Docker riêng `chat-ci-postgres`, database `chat_zalo_test`, chỉ publish cổng trên loopback của GitHub-hosted runner.
- Mỗi lần chạy sinh mật khẩu ngẫu nhiên. Mật khẩu và connection string được mask trước khi đưa vào môi trường các bước sau; không cần repository secret, không có thông tin đăng nhập cố định trong YAML.
- Chạy migration trước test; luôn dọn container và volume test khi kết thúc. Không kết nối database phát triển/demo của thành viên.
- Giữ `pull_request`, không dùng `pull_request_target` để thực thi code PR với quyền cao hơn. Token chỉ có `contents: read`.

## Leader kiểm tra trước khi merge

1. Mở PR → **Checks**, kiểm tra đúng commit mới nhất.
2. Chờ cả `Backend CI / build` và `Frontend CI / build` hoàn thành thành công.
3. Nếu lỗi, mở bước Restore, Build, Migration hoặc Test đang đỏ. Báo cáo `.trx` nằm trong artifacts `backend-test-results` và `frontend-test-results` (giữ 7 ngày).
4. Review nội dung code rồi mới merge thủ công. Workflow không tự merge, không publish image và không deploy production.

Leader có thể đặt hai check này là bắt buộc trong branch rules/ruleset của `main`; thay đổi workflow không tự cấu hình chính sách repository.

Tham khảo: [GitHub Actions workflow commands (mask và GITHUB_ENV)](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands), [PostgreSQL và Docker trong Actions](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers).
