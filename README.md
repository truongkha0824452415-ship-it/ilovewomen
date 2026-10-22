# Backend chấm công nhân viên

Backend ASP.NET Core Web API (.NET 8), dùng SQL Server qua Entity Framework Core.

## Mở và chạy bằng Visual Studio

1. Mở `api/ChamCong.Api.csproj` trong Visual Studio 2022.
2. Chọn `ChamCong.Api` làm Startup Project.
3. Chờ NuGet restore rồi nhấn F5. Swagger mở tại `https://localhost:54511/swagger`.

API HTTP chạy ở `http://localhost:54512`. File `api/Properties/launchSettings.json` định nghĩa các cổng local.

## Cấu hình local

Không commit `api/appsettings.json` hoặc thông tin SMTP/JWT vào Git. Dùng User Secrets trong terminal tại thư mục `api`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=ChamCongNhanVienDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
dotnet user-secrets set "Jwt:Key" "THAY_BANG_CHUOI_BI_MAT_DAI_TU_32_KY_TU"
dotnet user-secrets set "AdminAccess:Code" "CHON_MA_6_CHU_SO"
```

Để gửi OTP email, cấu hình SMTP trong User Secrets. Với Gmail, dùng App Password:

```powershell
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Smtp:Port" "587"
dotnet user-secrets set "Email:Smtp:Username" "email-gui@example.com"
dotnet user-secrets set "Email:Smtp:Password" "GOOGLE_APP_PASSWORD"
dotnet user-secrets set "Email:Smtp:From" "email-gui@example.com"
```

Sau khi đổi cấu hình, khởi động lại API. Mẫu cấu hình không chứa mật khẩu nằm ở `api/appsettings.example.json`.

## Mã nguồn

- `api/Controllers/`: endpoint xác thực, nhân viên và chấm công.
- `api/Models/`: request, response và entity.
- `api/Data/AttendanceDbContext.cs`: cấu hình schema và cập nhật schema cũ.
- `api/Services/`: truy cập dữ liệu, OTP email, SMTP và JWT.
- `api/Security/`: xác thực và phân quyền.
- `api/Program.cs`: đăng ký dịch vụ và khởi chạy backend.
