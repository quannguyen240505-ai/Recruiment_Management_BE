# Recruiment_Management_BE

Backend ASP.NET Core Web API (.NET 8) cho hệ thống quản lý tuyển dụng.

## Yêu cầu
- .NET 8 SDK
- Git
- (Từ SCRUM-128) SQL Server hoặc cơ sở dữ liệu nhóm đã thống nhất

## Chạy backend ở máy cục bộ
```bash
git clone https://github.com/quannguyen240505-ai/Recruiment_Management_BE.git
cd Recruiment_Management_BE
dotnet restore
dotnet run --project src/Recruitment.Api --launch-profile https
```
- Swagger: https://localhost:7080/swagger (môi trường Development)
- Kiểm tra sống: `GET https://localhost:7080/api/health` và `GET https://localhost:7080/health`

Lần đầu chạy https, nếu trình duyệt cảnh báo chứng chỉ: `dotnet dev-certs https --trust`.

## Cấu hình và secrets
Không commit secrets. `appsettings.json` chỉ chứa khóa rỗng làm mẫu. Giá trị thật đặt bằng User Secrets (máy cá nhân):
```bash
cd src
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<chuỗi kết nối của bạn>"
dotnet user-secrets set "Jwt:Key" "<khóa tối thiểu 32 ký tự>"
dotnet user-secrets set "Jwt:Issuer" "Recruitment.Api"
dotnet user-secrets set "Jwt:Audience" "Recruitment.Client"
```
Trên server hoặc CI dùng biến môi trường, ví dụ `ConnectionStrings__DefaultConnection`, `Jwt__Key`.

### Google Authentication (SCRUM-131)

Google Login sử dụng Google ID Token để xác thực người dùng.

**Cấu hình Google Client ID bằng User Secrets:**

Chạy từ thư mục gốc repository:

```powershell
dotnet user-secrets set "Google:ClientId" "<YOUR_GOOGLE_CLIENT_ID>" --project src/Recruitment.Api.csproj
```

Google Client ID được tạo tại Google Cloud Console:
APIs & Services > Credentials > OAuth 2.0 Client IDs.

Chọn loại Web application và cấu hình Authorized JavaScript origins
phù hợp với địa chỉ Frontend, ví dụ http://localhost:4200.

**Cấu hình bằng biến môi trường:**

```powershell
$env:Google__ClientId = "<YOUR_GOOGLE_CLIENT_ID>"
```

ASP.NET Core sử dụng Google:ClientId trong IConfiguration.

Không commit JWT Secret, mật khẩu Database hoặc Google Client Secret
vào GitHub. Google Client Secret không cần cho luồng xác minh ID Token này.

**Endpoint:**

POST /api/auth/google-login

Request JSON:

```json
{
  "idToken": "<GOOGLE_ID_TOKEN>"
}
```

Khi token hợp lệ và tài khoản được phép đăng nhập, API trả về JWT nội bộ.

## Cấu trúc thư mục
```
Recruitment.sln
src/Recruitment.Api/
  Controllers/   Controller mỏng, chỉ nhận request và gọi service
  Dtos/          Request, response, truy vấn phân trang
  Services/      Logic nghiệp vụ (interface và cài đặt)
  Entities/      Entity EF Core và enum
  Data/          AppDbContext, cấu hình EF, migrations
  Common/        Tiện ích dùng chung (claims, hằng số role)
  Program.cs     Cấu hình DI và middleware
```
Namespace dạng `Recruitment.Api.<Thư mục>`.

## Quy trình làm việc với Git và PR
1. Tạo nhánh từ `main`: `git checkout -b feature/SCRUM-xxx-mo-ta-ngan`
2. Commit theo mẫu: `SCRUM-xxx: nội dung ngắn gọn`
3. Push và mở Pull Request vào `main`, điền mẫu PR, chờ ít nhất một người review.
4. CI chạy `dotnet build`; PR chỉ merge khi build xanh.

## Việc tiếp theo
- SCRUM-128: Authentication schema và EF Core migrations
- SCRUM-129: JWT validation, CORS và API conventions
