# DATABASE RULES – CHAT_ZALO

**Phiên bản:** 1.2 (Sprint 1) + phụ lục Sprint 2  
**Phạm vi:** Chat cá nhân 1–1 và mở rộng chat nhóm  
**Áp dụng:** Toàn bộ nhóm phát triển `chat_zalo`

## 1. Công nghệ thống nhất

| Hạng mục        | Quy định                                         |
| --------------- | ------------------------------------------------ |
| Database        | PostgreSQL 18, tên `chat_zalo`                   |
| ORM / Migration | Entity Framework Core 10 / Code First            |
| Khóa định danh  | UUID (`Guid`)                                    |
| Thời gian       | UTC (`DateTimeOffset`, PostgreSQL `timestamptz`) |
| Authentication  | ASP.NET Core Identity + Cookie                   |

- `ApplicationUser : IdentityUser<Guid>` trong `Chat.Infrastructure/Identity/`, ánh xạ bảng `Users` và có `DisplayName`.
- `ChatDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`; **không tạo Entity/bảng Users thứ hai**.
- `NormalizedUserName`, `NormalizedEmail` phải có UNIQUE phù hợp; không mặc định Identity bảo đảm email unique.
- Các bảng Identity đi kèm: `Roles`, `UserRoles`, `UserClaims`, `RoleClaims`, `UserLogins`, `UserTokens`.

## 2. Các bảng Sprint 1 – Giữ nguyên

| Bảng                  | PK                               | Ràng buộc/nghiệp vụ                                                                                            |
| --------------------- | -------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `Users`               | `Id`                             | Identity User; tên/email chuẩn hóa duy nhất                                                                    |
| `Friendships`         | `(UserLowId, UserHighId)`        | 2 FK Users, `UserLowId < UserHighId`, `RequesterUserId` thuộc cặp; Status Pending/Accepted/Rejected/Cancelled  |
| `UserBlocks`          | `(BlockerUserId, BlockedUserId)` | FK Users; không tự chặn                                                                                        |
| `Conversations`       | `Id`                             | FK `CreatedByUserId` → Users; `Kind`, `LastMessageSeq >= 0`                                                    |
| `DirectConversations` | `ConversationId`                 | FK Conversations; `UNIQUE(UserLowId, UserHighId)`, cặp chuẩn hóa; chỉ Kind Direct                              |
| `ConversationMembers` | `(ConversationId, UserId)`       | FK Conversations và Users; `LastReadSeq`, `LastDeliveredSeq`                                                   |
| `Messages`            | `Id`                             | FK Conversations/Users; `UNIQUE(ConversationId, Seq)`, `UNIQUE(ConversationId, SenderUserId, ClientMessageId)` |

### 2.1. Nghiệp vụ Sprint 1

- Một cặp người dùng tối đa một hội thoại Direct; tạo mới chỉ khi Accepted và không chặn nhau. Direct có đúng hai thành viên.
- Blocks Direct: kiểm tra hai chiều để chặn gửi; người chặn vẫn xem được lịch sử cũ, người bị chặn không xem được lịch sử qua API khi còn bị chặn; xem `docs/API_CONTRACT.md` v1.2.
- Tin nhắn văn bản hợp lệ 1–4.000 ký tự; chỉ người đủ quyền được gửi/xem.
- Cấp `Seq = LastMessageSeq + 1`, tăng bộ đếm và insert tin trong **cùng transaction** hoặc tương đương an toàn concurrency; không dùng SELECT MAX+1 không khóa.
- Retry cùng `ClientMessageId`, cùng nội dung → trả tin cũ; khác nội dung → `409`; xử lý tranh chấp UNIQUE an toàn.
- `LastReadSeq`, `LastDeliveredSeq` không giảm, không vượt `LastMessageSeq` và `LastReadSeq <= LastDeliveredSeq`.
- Migrations chỉ một owner, EF Configurations áp dụng qua `ApplyConfigurationsFromAssembly` sau cấu hình Identity; không dùng script SQL tạo schema độc lập song song.
- Không commit mật khẩu thật, connection string thật; dùng secrets/env (`ConnectionStrings__ChatDb`).

## 3. Mở rộng Sprint 2 – Chat nhóm

### 3.1. Schema bổ sung CHAT-12

| Bảng/cột                               | Thiết kế                                                                                                        | Mục đích                                       |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| `Conversations.Kind` **(đã có)**       | `Direct` hoặc `Group`                                                                                           | Phân biệt hội thoại, giữ dữ liệu Direct        |
| `GroupConversations` **(CHAT-12)**     | `ConversationId` PK/FK → Conversations; `Name` không trắng, tối đa 100 ký tự; `DeletedAt` nullable             | Thông tin nhóm, giải tán mềm                   |
| `ConversationMembers.Role` **(đã có)** | `Admin` hoặc `Member` cho nhóm; Direct mặc định `Member`                                                        | Vai trò trong nhóm                             |
| `ConversationMembers.LeftAt` **(đã có)** | `timestamptz` nullable                                                                                        | Thành viên đang hoạt động nếu `LeftAt IS NULL` |
| `GroupAdminTransferRequests` **(CHAT-12)** | `Id` UUID PK; FK tới nhóm và hai thành viên; `Status`, `CreatedAt`, `ResolvedAt` nullable                    | Đề nghị và xác nhận chuyển Admin               |

- `GroupConversations` chỉ gắn với `Conversations.Kind = Group`; deferred trigger kiểm tra điều này. `DirectConversations` chỉ gắn với `Kind = Direct` theo service Sprint 1.
- Tên nhóm được kiểm tra không trống, độ dài giới hạn phải thống nhất API; không thêm bảng `Users` mới.
- `GroupAdminTransferRequests.Status` dùng các giá trị `Pending`, `Accepted`, `Rejected`, `Cancelled`; CHECK yêu cầu `ResolvedAt` chỉ có khi đã xử lý. Partial unique index giới hạn một yêu cầu `Pending` trên mỗi nhóm.
- `FromUserId`, `ToUserId` phải là hai thành viên khác nhau đang hoạt động trong nhóm; `FromUserId` là Admin hiện tại.
- Không xóa hàng `ConversationMembers` khi rời nhóm nếu muốn lưu lịch sử thành viên; cập nhật `LeftAt`. Xóa/giải tán nhóm dùng đánh dấu ngừng hoạt động, không tự xóa vật lý tin nhắn.

### 3.2. Bắt buộc bảo đảm một Admin

- Người tạo nhóm là `Admin` đầu tiên; còn lại `Member`.
- Nhóm **đang hoạt động** phải có **đúng một Admin đang hoạt động**.
- **Partial unique index** theo `ConversationId` trên `ConversationMembers` với điều kiện `Role = 'Admin' AND LeftAt IS NULL` ngăn hai Admin đang hoạt động.
- Migration CHAT-12 thêm **deferred constraint trigger** kiểm tra mỗi nhóm chưa giải tán có một Admin khi transaction kết thúc. Tạo nhóm, chuyển quyền, rời nhóm và giải tán phải cập nhật các hàng liên quan trong cùng transaction; trigger từ chối commit nếu nhóm hoạt động còn 0 Admin. Backend vẫn phải kiểm tra quyền và dùng concurrency control để trả lỗi nghiệp vụ đúng hợp đồng.
- Chuyển Admin: Admin đề nghị → người nhận chấp nhận → trong một transaction hạ vai trò Admin cũ và nâng thành viên mới; từ chối không đổi vai trò. Lưu trạng thái xử lý để tránh accept lặp/race.
- Admin chỉ được rời nhóm nếu đã chuyển quyền thành công; nếu là thành viên duy nhất, có thể giải tán nhóm.

### 3.3. Quy tắc thành viên và quyền

- Mọi thành viên đang hoạt động có thể mời **bạn bè Accepted của chính mình**. Không yêu cầu các thành viên khác kết bạn với người mới.
- Mời người đã ở nhóm → từ chối trùng. Mời người chưa là bạn của người mời → từ chối.
- Chỉ Admin được xóa **thành viên khác**, đề nghị chuyển quyền hoặc giải tán nhóm.
- Member được tự rời nhóm; thành viên đã rời/bị xóa không được gửi tin, đọc lịch sử nhóm hoặc Join SignalR (trừ khi có hợp đồng khác được duyệt).
- Mọi kiểm tra quyền dựa vào người dùng xác thực, trạng thái nhóm và membership thực tế tại Backend.

### 3.4. Blocks nhóm – Quy tắc đã chốt

Khi **A chặn B**:

1. B không được xem **mọi tin cũ và mới do A gửi trong nhóm**.
2. B vẫn đọc được tin do thành viên khác gửi, vẫn gửi được tin nhóm và **không mất membership/vai trò**.
3. Khi A bỏ chặn B, B được đọc lại tin cũ/mới của A (nếu còn quyền đọc hội thoại).
4. **Lọc tại Backend/SQL trước khi áp dụng `LIMIT`**, cho `beforeSeq` và `afterSeq`: trả tối đa 10 **tin được phép xem**, không trả trang hụt do lọc sau khi phân trang. `Seq` có thể không liền nhau.
5. Không trả body, preview, trích dẫn hay file riêng của tin bị ẩn qua endpoint khác; SignalR `ConversationChanged` chỉ báo thay đổi, **không chứa nội dung**. Cần tránh lộ preview qua danh sách hội thoại.
6. **Quy tắc Direct ở Sprint 1 giữ nguyên**; không áp dụng chính sách lọc theo sender của Group cho Direct.

### 3.5. Messages và realtime

- Giữ nguyên bảng `Messages`, khóa unique, transaction cấp Seq, `ClientMessageId` và hợp đồng GET/POST v1.2.
- `Direct`: giữ nguyên điều kiện bạn bè và chặn hai chiều; `Group`: kiểm tra membership, trạng thái nhóm và Blocks khi **đọc**, không yêu cầu tất cả thành viên là bạn bè.
- Backend mặc định `limit=20` theo Sprint 1; Frontend Sprint 2 gọi `limit=10`. Kiểm thử ít nhất 1.000 tin, đọc theo cursor thay vì lấy toàn bộ.
- SignalR `/hubs/chat`, `ConversationChanged` giữ payload v1.2; chỉ connection đã Join và còn quyền được nhận event. Thu hồi quyền sau Leave, bị xóa hoặc nhóm giải tán; lưu xong transaction mới phát event.

## 4. Migration và nghiệm thu Sprint 2

1. CHAT-12 đối chiếu code hiện có với API Contract Sprint 2 và chỉ thêm hai bảng mới ở mục 3.1; các cột `Kind`, `Role`, `LeftAt` đã tồn tại từ Sprint 1.
2. Chỉ người phụ trách Database thực hiện migration schema dùng chung; các thành viên khác trao đổi qua PR/Leader.
3. Không sửa migration đã merge; tạo migration mới, có kiểm thử nâng cấp từ DB Sprint 1 **và** DB mới sạch.
4. Kiểm thử PK/FK/UNIQUE/CHECK, partial unique index, race khi hai người cùng accept chuyển Admin, giữ dữ liệu Direct, lịch sử 1.000 tin và Blocks trước phân trang.
5. CI/build/test hoặc Leader chạy kiểm chứng; thành viên **không bắt buộc viết báo cáo `.md`**. Cập nhật hai tài liệu schema là cần thiết vì chúng là hợp đồng, không phải báo cáo.

**Nguồn sự thật:** `docs/API_CONTRACT.md` quyết định HTTP/DTO/chính sách API; tài liệu này quyết định database invariants; `database/ERD.mmd` là sơ đồ logic; chỉ triển khai các thay đổi mới khi Leader duyệt thiết kế schema và API khớp nhau.
