using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class ContinueRegistrationRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Employee";
    public string AdminCode { get; set; } = string.Empty;
}
