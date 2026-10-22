using System.Net;
using System.Net.Mail;

namespace ChamCong.Api.Services;

public sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
{
    public async Task SendOtpAsync(string address, string displayName, string code, string purpose)
    {
        var host = configuration["Email:Smtp:Host"];
        var from = configuration["Email:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("Chưa cấu hình SMTP. Hãy thiết lập Email:Smtp:Host và Email:Smtp:From trước khi đăng ký hoặc đăng nhập.");
        }

        var subject = purpose switch
        {
            EmailOtpService.VerifyEmailPurpose => "Xác minh email đăng ký",
            EmailOtpService.PasswordResetPurpose => "Mã OTP đặt lại mật khẩu",
            EmailOtpService.PasswordChangePurpose => "Mã OTP đổi mật khẩu",
            _ => "Mã OTP đăng nhập chấm công"
        };
        var body = $"Xin chào {displayName},\n\nMã OTP của bạn là: {code}\nMã có hiệu lực trong 5 phút và chỉ sử dụng một lần.\n\nNếu bạn không thực hiện yêu cầu này, hãy bỏ qua email.\n\nỨng dụng Chấm công nhân viên";
        using var message = new MailMessage(from, address, subject, body);
        var port = configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;
#pragma warning disable SYSLIB0014
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };
        var username = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];
        if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Cần cấu hình đồng thời Email:Smtp:Username và Email:Smtp:Password.");
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        try
        {
            await client.SendMailAsync(message);
            logger.LogInformation("OTP email sent for {Purpose} to {Address}.", purpose, address);
        }
        catch (Exception error) when (error is SmtpException or InvalidOperationException or IOException or OperationCanceledException)
        {
            logger.LogError(error, "Failed to send OTP email for {Purpose} to {Address}.", purpose, address);
            throw new InvalidOperationException("Không gửi được email OTP. Kiểm tra cấu hình SMTP, mật khẩu ứng dụng và kết nối mạng rồi thử lại.", error);
        }
#pragma warning restore SYSLIB0014
    }
}
