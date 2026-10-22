using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class RegisterRequest
{
    [Required, StringLength(40, MinimumLength = 3)] public string Username { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
    [Required, StringLength(100)] public string DisplayName { get; set; } = string.Empty;
    [StringLength(100)] public string Department { get; set; } = "Chưa phân công";
    public string Role { get; set; } = "Employee";
    public string AdminCode { get; set; } = string.Empty;
    [Required, StringLength(1400000, MinimumLength = 100)] public string AvatarBase64 { get; set; } = string.Empty;
}
