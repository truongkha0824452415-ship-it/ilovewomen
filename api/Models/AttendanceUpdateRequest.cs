using System.ComponentModel.DataAnnotations;

namespace ChamCong.Api.Models;

public sealed class AttendanceUpdateRequest
{
    [Required] public DateTimeOffset? CheckIn { get; set; }
    public DateTimeOffset? CheckOut { get; set; }
}
