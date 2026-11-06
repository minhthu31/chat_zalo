# S1-11 – Frontend Chat, Messages & SignalR

**Branch:** `feature/CHAT-11-frontend-chat-messages-signalr`  **Commit:** `CHAT-11 Frontend chat 1-1, messages API client, SignalR client`
**PR:** `CHAT-11 Giao diện Chat 1-1 Blazor + MessagesApiClient + ChatHubClient (mock + Backend thật)`

## 1. Chạy demo (dữ liệu mẫu, không cần Backend)
```bash
dotnet restore frontend/Chat.Client.csproj
dotnet run --project frontend/Chat.Client.csproj
# mở https://localhost:<port>/chat  (mặc định: DỮ LIỆU MẪU)
```
Kịch bản quay video (khoảng 2 phút):
1. Mở `/chat`: thấy Loading → danh sách 3 hội thoại (B có 45 tin, C có 3 tin, D rỗng → Empty).
2. Chọn "Nguyễn Văn B": 20 tin mới nhất; bấm "Tải tin nhắn cũ hơn" 2 lần → đủ 45 tin, không trùng.
3. Gõ và gửi tin → "Đang gửi…" rồi "✓". Mở "Bảng điều khiển demo" → "Đối phương gửi 1 tin" → tin hiện ngay (ConversationChanged → afterSeq).
4. "Ngắt mạng giả lập" → banner "Mất kết nối"; gửi tin → báo lỗi + nút Gửi lại; "Đối phương gửi 3 tin"; "Bật lại mạng" → tự đồng bộ đủ 3 tin, không trùng; bấm Gửi lại → chỉ 1 tin.
5. "Lần gửi tới sẽ lỗi" → thấy lỗi gửi tin. Chọn hội thoại D → Empty. Nút "Dùng Backend thật" khi chưa đăng nhập → Unauthorized/Error.

## 2. Chạy với Backend thật
```bash
docker compose up -d db
dotnet run --project backend/Chat.Api --urls http://localhost:5080
dotnet run --project frontend/Chat.Client.csproj
```
Cần: đã đăng nhập (Cookie), BE cấu hình `Cors:AllowedOrigins` đúng origin FE + AllowCredentials. Trên trang `/chat` bấm "Dùng Backend thật".

## 3. Kiểm thử
```bash
dotnet test tests/frontend/Chat.Client.Tests/Chat.Client.Tests.csproj
```
12 test: tải trang đầu, beforeSeq, afterSeq/không trùng, retry cùng ClientMessageId, validate 1–4000 ký tự,
reconnect đồng bộ tin bị thiếu, event trùng, lỗi mạng → Error → phục hồi, hội thoại rỗng, và hành vi mock đúng contract (400 INVALID_PAGINATION_PARAMETERS, 409 CLIENT_MESSAGE_ID_CONFLICT, giới hạn limit).

**Bằng chứng nộp (điền sau khi chạy trên máy):**
- [ ] Ảnh/Video demo: `docs/evidence/s1-11-demo.mp4`
- [ ] Ảnh kết quả `dotnet build` / `dotnet test`: `docs/evidence/s1-11-test.png`

## 4. Đối chiếu Acceptance Criteria
| Tiêu chí | Nơi thực hiện |
|---|---|
| Giao diện Chat 1–1 + danh sách hội thoại | `Pages/Chat.razor`, `Components/Conversations/*` |
| Khu vực hiển thị + nhập tin | `Components/Messages/MessageList|MessageBubble|MessageInput` |
| Mock data demo gửi/hiển thị | `Services/MockChatStore.cs` + `Mock*Client` |
| Models + Services khớp DTO/endpoint v1.2 | `Models/*`, `ConversationsApiClient`, `MessagesApiClient` |
| `ChatHubClient` + `ConversationChanged` | `Services/ChatHubClient.cs` (+ `JoinConversation` theo Hub hiện tại) |
| `afterSeq` / `beforeSeq` | `ChatState.SyncAfterAsync`, `ChatState.LoadOlderAsync` |
| Reconnect + chống trùng | `WithAutomaticReconnect` + rejoin; `ChatState` khử trùng theo Id, Seq, EventId |
| Loading/Empty/Error/mất kết nối | `StatePanel`, `ConnectionBanner`, lỗi gửi tin có Gửi lại |
| Không ReactJS/JSX/Vite/npm, không truy cập PostgreSQL | Chỉ Razor/C#; không JS tự viết |

## 5. File dùng chung đã sửa (cần Leader duyệt khi review PR)
- `frontend/Chat.Client.csproj`: thêm `Microsoft.AspNetCore.SignalR.Client` 10.0.0.
- `frontend/Program.cs`: thêm 1 dòng `builder.Services.AddChatFeature(...)` (+ `using`).
- Không sửa `index.html`/`app.css`: `chat.css` được nạp bằng `<HeadContent>` trong `Chat.razor`.

## 6. Ghi chú tích hợp
- `ChatApiHttp` làm Cookie + `X-CSRF-TOKEN` + giải mã `{code,message}` riêng cho Chat. Khi nhóm Auth merge `ApiRequestSender`/`ApiException`, thay thân `ChatApiHttp`/`ChatApiException` là đủ, các client phía trên không đổi. Người dùng hiện tại lấy qua `GET /api/auth/me`, có thể đổi sang `AuthState.CurrentUser`.
- `ConversationDto` v1.2 chỉ có `id, kind, lastMessageSeq` → chế độ Backend thật hiện chỉ hiển thị "Hội thoại <mã>"; cần Leader bổ sung thông tin đối phương vào contract để hiện tên.
- Contract chưa nhắc nhưng Hub thật yêu cầu client gọi `JoinConversation(conversationId)`; `ChatHubClient` đã làm và join lại sau mỗi lần reconnect.
- Thư mục theo đề: `Components/Conversations` (repo khung đang có `Components/Conversation`).
