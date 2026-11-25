# SCRUM-157 – Backend quên mật khẩu

Phạm vi: SCRUM-14 / US-03. Backend thực hiện request → gửi email → reset → đăng nhập
bằng mật khẩu mới. Trang Angular thuộc SCRUM-158, kiểm thử phối hợp SCRUM-159/QA.

## API bàn giao cho FE

| Method / endpoint | Request JSON | Thành công | Lỗi |
|---|---|---|---|
| POST `/api/auth/forgot-password` | `{ "email": "user@example.com" }` | 202, `{ "message": "Nếu tài khoản phù hợp, hướng dẫn khôi phục sẽ được gửi đến email của bạn." }` | 400 dữ liệu sai; 429 giới hạn tần suất; 503 chưa cấu hình kênh email |
| POST `/api/auth/reset-password` | `{ "token": "<mã 43 ký tự từ email>", "newPassword": "<mật khẩu mới>", "confirmPassword": "<nhập lại>" }` | 204, không body | 400 token sai/hết hạn/đã dùng, tài khoản không khả dụng, mật khẩu sai chính sách; 429 giới hạn tần suất |

Hai endpoint AllowAnonymous; không cần JWT. Error theo ProblemDetails hoặc
ValidationProblemDetails của ApiController. Không coi 202 là email đã gửi: đây là
phản hồi đã tiếp nhận, email được outbox worker xử lý. Không có endpoint trả raw token.
FE cần một form nhận email, một form nhập mã trong email và mật khẩu mới; không
phải luồng click link tự động. Không lưu token vào localStorage hoặc log console.

202 giống nhau cho email tồn tại, không tồn tại, khóa/xóa và tài khoản Google-only.
Google-only tiếp tục dùng kênh khôi phục của Google, không tự thêm mật khẩu local.
Không bảo đảm thời gian phản hồi bằng nhau tuyệt đối. Chính sách mật khẩu mới:
8–128 ký tự, có chữ hoa/thường/số/ký tự đặc biệt, xác nhận khớp, khác mật khẩu hiện tại.

## Thiết kế

- 32 byte ngẫu nhiên mật mã, Base64URL thành token 43 ký tự; hiệu lực mặc định 15 phút.
- Bảng PasswordResetRequests lưu SHA-256 token để tra cứu, FK UserId, thời hạn và thời
  điểm dùng. Bản token cần gửi qua email chỉ lưu dưới dạng Data Protection bảo vệ.
- Outbox gửi email định kỳ 10 giây; lỗi SMTP thử lại sau 1 phút, tối đa 5 lần. Không
  log token, email, password hoặc nội dung exception SMTP. Khi gửi xong/hết hạn/đã dùng
  thì xóa payload bảo vệ. Có thể yêu cầu lại nếu không nhận được email.
- Reset tiêu thụ token và cập nhật password hash trong cùng transaction. Conditional
  update bảo đảm token chỉ dùng một lần. Hash mật khẩu tại thời điểm phát hành giúp
  phát hiện thay đổi mật khẩu; email, trạng thái account cũng được kiểm tra lại.
- Reset thành công vô hiệu hóa các token còn lại của user. Username/UserId/role không
  đổi. Request mới không vô hiệu token cũ ngay; reset thành công mới vô hiệu tất cả.
- Rate limit chung hai endpoint: 10 request/phút/IP, không xếp hàng. Khi sau reverse
  proxy, cần cấu hình trusted forwarded headers theo hạ tầng; không tin tùy ý X-Forwarded-For.
- Không thay đổi JWT/logout hiện tại: JWT đã cấp vẫn tuân theo thời hạn của nó.

## Cập nhật database trên máy dev

Từ thư mục có Recruitment.sln (không chạy EF vào database chung khi chưa phối hợp):

```powershell
dotnet restore
dotnet build Recruitment.sln -c Release
dotnet ef database update --project src/Recruitment.Api.csproj
```

Nếu máy chưa có EF CLI: `dotnet tool install --global dotnet-ef --version 8.0.34`.
ConnectionStrings:DefaultConnection và Jwt:Key/Issuer/Audience dùng cấu hình đã có.
Migration chỉ tạo PasswordResetRequests và các index/FK, không chỉnh Users/Jobs.

## Cấu hình gửi email

Lưu giá trị SMTP thật bằng User Secrets trên máy chạy, không commit vào appsettings.
Thay từng placeholder bằng cấu hình nhà cung cấp SMTP của nhóm:

```powershell
dotnet user-secrets set "PasswordReset:SmtpHost" "<SMTP_HOST>" --project src/Recruitment.Api.csproj
dotnet user-secrets set "PasswordReset:SmtpPort" "587" --project src/Recruitment.Api.csproj
dotnet user-secrets set "PasswordReset:SmtpEnableSsl" "true" --project src/Recruitment.Api.csproj
dotnet user-secrets set "PasswordReset:SmtpUsername" "<SMTP_USERNAME>" --project src/Recruitment.Api.csproj
dotnet user-secrets set "PasswordReset:SmtpPassword" "<SMTP_PASSWORD_OR_APP_PASSWORD>" --project src/Recruitment.Api.csproj
dotnet user-secrets set "PasswordReset:FromEmail" "<FROM_EMAIL>" --project src/Recruitment.Api.csproj
```

Mặc định TokenLifetimeMinutes=15; có thể cấu hình 1–60. Host/from chưa cấu hình thì
API request trả 503 cho mọi email. SMTP sai mật khẩu/không truy cập được sẽ khiến email
đợi retry; không trả lỗi riêng theo email để tránh lộ tài khoản.
Test SMTP cục bộ có thể dùng Mailpit (không gửi ra ngoài); chọn port 1025,
SmtpEnableSsl=false, không username/password và FromEmail hợp lệ. SMTP thật nên dùng TLS.
Trên server dùng biến môi trường `PasswordReset__SmtpHost`… và secret store của nhóm.
ASP.NET User Secrets chỉ tự nạp trong môi trường Development.

Data Protection phải giữ key qua lần restart và chia sẻ cùng key ring khi nhiều
instance; bảo vệ key bằng cơ chế của hạ tầng, không commit key. Với nhiều worker có
thể gửi trùng email cùng mã (outbox at-least-once), nhưng reset vẫn chỉ commit một lần.
Hiện chưa có job xóa bản ghi lịch sử; bổ sung retention khi triển khai dài hạn.

## Kiểm thử và QA

```powershell
dotnet test Recruitment.sln -c Release
dotnet test Recruitment.sln -c Release --filter "FullyQualifiedName~PasswordReset" --logger "console;verbosity=normal"
```

Test service/SQLite: dữ liệu công khai không tiết lộ tài khoản; bảo vệ token; token
sai/hết hạn/đã dùng; khóa/xóa/đổi email/đổi mật khẩu; giữ role; đổi password rồi login
cũ/mới; vô hiệu token anh em; race trước transaction; rollback khi lỗi ghi; retry SMTP
và không gửi mã obsolete. Test HTTP thật qua in-process TestServer: routing,
AllowAnonymous, automatic DTO validation, response 202 không token, rate limit 429.
SMTP ở test là fake; Google thật không liên quan tính năng này.

QA trên SQL Server và email test của nhóm:
1. Apply migration, cấu hình SMTP, chạy API và yêu cầu reset email local ACTIVE.
2. Xác nhận 202; email đến qua SMTP, không có token trong response/log.
3. Nhập mã từ email vào reset API; nhận 204; mật khẩu mới login được, cũ bị từ chối.
4. Mã dùng lại/sai/hết hạn bị từ chối; password không đổi. Hai yêu cầu reset cùng mã
   chỉ một thành công; kiểm tra với hai mã khác nhau của cùng user.
5. Email không tồn tại, Google-only, locked/deleted: cùng 202; không gửi email reset.
6. Tắt SMTP sau cấu hình: 202 vẫn tiếp nhận, worker ghi warning chung và retry;
   bật lại trong hạn, xác nhận email được gửi. Không báo trên UI rằng đã giao email.
7. FE/QA nối vào SCRUM-158/159, review/PR/merge trước khi Done.

Kết quả tự động và minh chứng thực tế là hai loại riêng; chưa chạy SQL Server thật
hoặc gửi SMTP thật trong môi trường QA khi tạo bản bàn giao này.
