using System.Security.Claims;
using System.Text.Encodings.Web;
using ChamCong.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ChamCong.Api.Security;

public sealed class JwtAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TokenService tokens,
    AttendanceDatabase database) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var principal = tokens.Validate(header[7..].Trim());
        if (principal is null) return AuthenticateResult.Fail("Token không hợp lệ hoặc đã hết hạn.");

        // Roles in a JWT are a snapshot. Re-read the account so deleted users,
        // unverified accounts, and users whose roles changed lose access at once.
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return AuthenticateResult.Fail("Token thiếu mã tài khoản.");
        var account = await database.FindEmployeeAsync(userId, Context.RequestAborted);
        if (account is null || !account.EmailVerified)
            return AuthenticateResult.Fail("Tài khoản không tồn tại hoặc email chưa xác minh.");

        var identity = principal.Identities.FirstOrDefault();
        if (identity is null) return AuthenticateResult.Fail("Token không hợp lệ.");
        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray()) identity.RemoveClaim(claim);
        foreach (var claim in identity.FindAll(ClaimTypes.Name).ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.Role, account.Role));
        identity.AddClaim(new Claim(ClaimTypes.Name, account.Username));

        var ticket = new AuthenticationTicket(principal, "Bearer");
        return AuthenticateResult.Success(ticket);
    }
}
