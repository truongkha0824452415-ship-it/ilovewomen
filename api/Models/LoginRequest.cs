using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class LoginRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}
