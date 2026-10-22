using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class EmployeeUpdateRequest
{
    [Required, StringLength(100)] public string DisplayName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [StringLength(100)] public string Department { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Employee";
    [StringLength(100, MinimumLength = 8)] public string? Password { get; set; }
    [StringLength(1400000, MinimumLength = 100)] public string? AvatarBase64 { get; set; }
}
