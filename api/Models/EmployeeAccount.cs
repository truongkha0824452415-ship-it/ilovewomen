namespace ChamCong.Api.Models;

public sealed class EmployeeAccount
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
    public string? AvatarBase64 { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string? EmailOtpHash { get; set; }
    public string? EmailOtpSalt { get; set; }
    public string? EmailOtpPurpose { get; set; }
    public DateTimeOffset? EmailOtpExpiresAt { get; set; }
    public DateTimeOffset? EmailOtpLastSentAt { get; set; }
    public int EmailOtpAttempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AttendanceRecord> AttendanceRecords { get; set; } = [];
}

public sealed record EmployeeResponse(string Id, string Username, string Email, bool EmailVerified, string DisplayName, string Department, string Role, DateTimeOffset CreatedAt, string? AvatarBase64)
{
    public static EmployeeResponse From(EmployeeAccount user) => new(user.Id, user.Username, user.Email, user.EmailVerified, user.DisplayName, user.Department, user.Role, user.CreatedAt, user.AvatarBase64);
}
