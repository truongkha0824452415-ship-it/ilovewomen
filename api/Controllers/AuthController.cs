using ChamCong.Api.Models;
using ChamCong.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ChamCong.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AttendanceDatabase database, TokenService tokens, EmailOtpService emailOtp, IConfiguration configuration, ILogger<AuthController> logger, IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        if (request.Role is not ("Employee" or "Admin"))
            return BadRequest(new { message = "Vai trò phải là Nhân viên hoặc Admin." });
        if (request.Role == "Admin")
        {
            var configuredCode = configuration["AdminAccess:Code"]?.Trim() ?? string.Empty;
            if (!IsConfiguredAdminCode(configuredCode) && environment.IsDevelopment())
                configuredCode = configuration["AdminAccess:DevelopmentCode"]?.Trim() ?? string.Empty;
            if (!IsConfiguredAdminCode(configuredCode))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Mã Admin chưa được cấu hình đúng 6 chữ số ở backend. Hãy sửa User Secrets rồi khởi động lại API." });
            var supplied = System.Text.Encoding.UTF8.GetBytes(request.AdminCode?.Trim() ?? string.Empty);
            var expected = System.Text.Encoding.UTF8.GetBytes(configuredCode);
            if (supplied.Length != expected.Length || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(supplied, expected))
                return Unauthorized(new { message = "Mã Admin không chính xác. Không thể đăng ký tài khoản Admin." });
        }
        if (!ValidAvatar(request.AvatarBase64)) return BadRequest(new { message = "Ảnh đại diện không hợp lệ." });
        if (database.FindByUsername(request.Username.Trim()) is not null) return Conflict(new { message = "Tên đăng nhập đã tồn tại." });
        if (database.FindByEmail(request.Email.Trim()) is not null) return Conflict(new { message = "Email đã được sử dụng." });
        try
        {
            var employee = database.AddEmployee(request.Username, request.Email, request.DisplayName,
                string.IsNullOrWhiteSpace(request.Department) ? "Chưa phân công" : request.Department,
                request.Role, request.Password, avatarBase64: request.AvatarBase64);
            var sent = await emailOtp.SendAsync(employee, EmailOtpService.VerifyEmailPurpose);
            if (!sent.Sent)
            {
                // Avoid leaving an unverified account that blocks the user from
                // retrying registration after SMTP is corrected.
                database.DeleteEmployee(employee.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = sent.Error });
            }
            return StatusCode(StatusCodes.Status202Accepted, new
            {
                requiresEmailVerification = true,
                email = EmailOtpService.MaskEmail(employee.Email),
                message = "Đã gửi mã xác minh đến email. Nhập mã để hoàn tất đăng ký."
            });
        }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (DbUpdateException error)
        {
            logger.LogError(error, "SQL Server failed to save a registration for username {Username}.", request.Username);
            var sqlError = error.GetBaseException() as SqlException;
            return sqlError?.Number switch
            {
                2601 or 2627 => Conflict(new { message = "Tên đăng nhập hoặc email đã được sử dụng. Hãy đăng nhập tài khoản cũ hoặc dùng thông tin khác." }),
                2628 or 8152 => BadRequest(new { message = "Một thông tin nhập vào dài quá giới hạn lưu trữ. Hãy kiểm tra tên, email và phòng ban." }),
                515 => BadRequest(new
                {
                    message = "SQL Server đang yêu cầu một cột bắt buộc mà thao tác đăng ký chưa cung cấp. Hãy xem tên cột trong chi tiết lỗi.",
                    details = environment.IsDevelopment() ? sqlError?.Message : null
                }),
                547 => BadRequest(new { message = "SQL Server từ chối lưu do ràng buộc khóa ngoại hoặc dữ liệu liên quan." }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "SQL Server không lưu được tài khoản. Xem chi tiết lỗi ở bên dưới hoặc cửa sổ Output của API.",
                    details = environment.IsDevelopment() ? error.GetBaseException().Message : null
                })
            };
        }
    }

    [AllowAnonymous]
    [HttpPost("continue-registration")]
    public async Task<IActionResult> ContinueRegistration(ContinueRegistrationRequest request)
    {
        if (request.Role is not ("Employee" or "Admin"))
            return BadRequest(new { message = "Vai trò phải là Nhân viên hoặc Admin." });
        if (request.Role == "Admin")
        {
            var configuredCode = configuration["AdminAccess:Code"]?.Trim() ?? string.Empty;
            if (!IsConfiguredAdminCode(configuredCode) && environment.IsDevelopment())
                configuredCode = configuration["AdminAccess:DevelopmentCode"]?.Trim() ?? string.Empty;
            if (!IsConfiguredAdminCode(configuredCode))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Mã Admin chưa được cấu hình đúng 6 chữ số ở backend." });
            var supplied = System.Text.Encoding.UTF8.GetBytes(request.AdminCode?.Trim() ?? string.Empty);
            var expected = System.Text.Encoding.UTF8.GetBytes(configuredCode);
            if (supplied.Length != expected.Length || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(supplied, expected))
                return Unauthorized(new { message = "Mã Admin không chính xác. Không thể tiếp tục với vai trò Admin." });
        }

        var employee = database.FindByEmail(request.Email.Trim());
        if (employee is null)
            return NotFound(new { message = "Không tìm thấy tài khoản với email này. Hãy thử đăng ký lại." });
        var purpose = $"registration-role:{request.Role}";
        var sent = await emailOtp.SendAsync(employee, purpose);
        if (!sent.Sent) return BadRequest(new { message = sent.Error });
        return Ok(new
        {
            requiresOtp = true,
            email = EmailOtpService.MaskEmail(employee.Email),
            message = $"Đã gửi mã OTP để xác nhận tiếp tục với vai trò {(request.Role == "Admin" ? "Admin" : "Nhân viên")}."
        });
    }

    [AllowAnonymous]
    [HttpPost("verify-registration-role")]
    public IActionResult VerifyRegistrationRole(EmailOtpRequest request)
    {
        var employee = FindAccount(request.Username);
        var purpose = employee?.EmailOtpPurpose;
        if (employee is null || purpose is null || !purpose.StartsWith("registration-role:", StringComparison.Ordinal) ||
            !emailOtp.Verify(employee, purpose, request.Code))
            return BadRequest(new { message = "Mã OTP không hợp lệ, đã hết hạn hoặc vượt số lần thử." });

        var role = purpose["registration-role:".Length..];
        if (role is not ("Admin" or "Employee"))
            return BadRequest(new { message = "Yêu cầu vai trò không hợp lệ." });
        database.SetRoleAndVerifyEmail(employee, role);
        database.ClearOtp(employee);
        return Ok(new { token = tokens.Create(employee), employee = EmployeeResponse.From(employee) });
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    public IActionResult VerifyEmail(EmailOtpRequest request)
    {
        var employee = FindAccount(request.Username);
        if (employee is null || employee.EmailVerified || !emailOtp.Verify(employee, EmailOtpService.VerifyEmailPurpose, request.Code))
            return BadRequest(new { message = "Mã xác minh không hợp lệ, đã hết hạn hoặc vượt số lần thử." });
        database.MarkEmailVerified(employee);
        return Ok(new
        {
            emailVerified = true,
            token = tokens.Create(employee),
            employee = EmployeeResponse.From(employee),
            message = "Email đã xác minh. Đăng nhập thành công."
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var employee = FindAccount(request.Username);
        if (employee is null || !database.VerifyPassword(employee, request.Password))
            return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
        var purpose = employee.EmailVerified ? EmailOtpService.LoginPurpose : EmailOtpService.VerifyEmailPurpose;
        var sent = await emailOtp.SendAsync(employee, purpose);
        if (!sent.Sent) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = sent.Error });
        return Ok(new
        {
            requiresOtp = true,
            requiresEmailVerification = !employee.EmailVerified,
            email = EmailOtpService.MaskEmail(employee.Email),
            message = employee.EmailVerified ? "Đã gửi OTP đăng nhập đến email." : "Email chưa xác minh. Mã xác minh đã được gửi lại."
        });
    }

    [AllowAnonymous]
    [HttpPost("verify-login-otp")]
    public IActionResult VerifyLoginOtp(EmailOtpRequest request)
    {
        var employee = FindAccount(request.Username);
        if (employee is null || !employee.EmailVerified || !emailOtp.Verify(employee, EmailOtpService.LoginPurpose, request.Code))
            return Unauthorized(new { message = "OTP không đúng, đã hết hạn hoặc vượt số lần thử." });
        database.ClearOtp(employee);
        return Ok(new { token = tokens.Create(employee), employee = EmployeeResponse.From(employee) });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var employee = database.FindByEmail(request.Email.Trim());
        if (employee is { EmailVerified: true })
            await emailOtp.SendAsync(employee, EmailOtpService.PasswordResetPurpose);

        // Return the same result whether or not the address belongs to an account.
        return Accepted(new
        {
            message = "Nếu email đã đăng ký và xác minh, mã OTP đặt lại mật khẩu sẽ được gửi đến hộp thư."
        });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public IActionResult ResetPassword(ResetPasswordRequest request)
    {
        var employee = database.FindByEmail(request.Email.Trim());
        if (employee is null || !employee.EmailVerified ||
            !emailOtp.Verify(employee, EmailOtpService.PasswordResetPurpose, request.Code))
            return BadRequest(new { message = "Email hoặc OTP không hợp lệ, mã đã hết hạn hoặc vượt số lần thử." });

        database.ResetPassword(employee, request.NewPassword);
        return Ok(new { passwordReset = true, message = "Đặt lại mật khẩu thành công. Hãy đăng nhập bằng mật khẩu mới." });
    }

    [Authorize]
    [HttpPost("request-password-change")]
    public async Task<IActionResult> RequestPasswordChange()
    {
        var employee = database.FindTrackedEmployee(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        if (employee is null) return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });
        if (!employee.EmailVerified) return BadRequest(new { message = "Hãy xác minh email trước khi đổi mật khẩu." });
        var sent = await emailOtp.SendAsync(employee, EmailOtpService.PasswordChangePurpose);
        return sent.Sent
            ? Ok(new { email = EmailOtpService.MaskEmail(employee.Email), message = "Đã gửi mã OTP đổi mật khẩu đến email của bạn." })
            : StatusCode(StatusCodes.Status429TooManyRequests, new { message = sent.Error });
    }

    [Authorize]
    [HttpPost("change-password")]
    public IActionResult ChangePassword(ChangePasswordRequest request)
    {
        var employee = database.FindTrackedEmployee(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
        if (employee is null) return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });
        if (!employee.EmailVerified || !emailOtp.Verify(employee, EmailOtpService.PasswordChangePurpose, request.Code))
            return BadRequest(new { message = "OTP không đúng, đã hết hạn hoặc vượt số lần thử." });
        database.ResetPassword(employee, request.NewPassword);
        return Ok(new { passwordChanged = true, message = "Đã đổi mật khẩu thành công." });
    }

    [AllowAnonymous]
    [HttpPost("resend-email-verification")]
    public async Task<IActionResult> ResendEmailVerification(LoginRequest request)
    {
        var employee = FindAccount(request.Username);
        if (employee is null || !database.VerifyPassword(employee, request.Password))
            return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
        if (employee.EmailVerified) return BadRequest(new { message = "Email của tài khoản đã được xác minh." });
        var sent = await emailOtp.SendAsync(employee, EmailOtpService.VerifyEmailPurpose);
        return sent.Sent
            ? Ok(new { message = "Đã gửi lại mã xác minh email." })
            : StatusCode(StatusCodes.Status429TooManyRequests, new { message = sent.Error });
    }

    private static bool ValidAvatar(string value)
    {
        if (value.Length is < 100 or > 1_400_000) return false;
        try { return Convert.FromBase64String(value).Length <= 1_000_000; }
        catch (FormatException) { return false; }
    }

    private EmployeeAccount? FindAccount(string identifier)
    {
        var value = identifier.Trim();
        return database.FindByUsername(value) ?? database.FindByEmail(value);
    }

    private static bool IsConfiguredAdminCode(string? code) =>
        code is { Length: 6 } && code.All(character => character is >= '0' and <= '9');
}
