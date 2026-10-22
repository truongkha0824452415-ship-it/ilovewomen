using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class EmailOtpRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; set; } = string.Empty;
}
