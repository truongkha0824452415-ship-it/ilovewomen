using System.Security.Cryptography;
using System.Net.Mail;
using System.Text;
using ChamCong.Api.Models;

namespace ChamCong.Api.Services;

public sealed class EmailOtpService(
    AttendanceDatabase database,
    SmtpEmailSender emailSender,
    IConfiguration configuration)
{
    public const string VerifyEmailPurpose = "verify-email";
    public const string LoginPurpose = "login";
    public const string PasswordResetPurpose = "password-reset";
    public const string PasswordChangePurpose = "password-change";

    public async Task<(bool Sent, string? Error)> SendAsync(EmployeeAccount employee, string purpose)
    {
        var now = DateTimeOffset.UtcNow;
        if (employee.EmailOtpLastSentAt is { } lastSent && now - lastSent < TimeSpan.FromMinutes(1))
            return (false, "Vui lòng chờ 1 phút trước khi yêu cầu mã mới.");

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var content = $"{employee.Id}|{purpose}|{salt}|{code}";
        var secret = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Thiếu cấu hình JWT.");
        var hash = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(content)));
        database.SaveOtp(employee.Id, purpose, salt, hash, now.AddMinutes(5), now);
        try
        {
            await emailSender.SendOtpAsync(employee.Email, employee.DisplayName, code, purpose);
            return (true, null);
        }
        catch (Exception error) when (error is SmtpException or InvalidOperationException)
        {
            database.ClearOtp(employee, resetSendLimit: true);
            return (false, error.Message);
        }
    }

    public bool Verify(EmployeeAccount employee, string purpose, string code)
    {
        var secret = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Thiếu cấu hình JWT.");
        return database.VerifyOtp(employee, purpose, code, secret, DateTimeOffset.UtcNow);
    }

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return $"{email[0]}{new string('*', Math.Min(at - 1, 6))}{email[at..]}";
    }
}
