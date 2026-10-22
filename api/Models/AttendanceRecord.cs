using System.ComponentModel.DataAnnotations.Schema;

namespace ChamCong.Api.Models;

public sealed class AttendanceRecord
{
    public AttendanceRecord() { }
    public AttendanceRecord(string id, string employeeId, DateOnly date, DateTimeOffset checkIn, DateTimeOffset? checkOut)
    { Id = id; EmployeeId = employeeId; Date = date; CheckIn = checkIn; CheckOut = checkOut; }
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTimeOffset CheckIn { get; set; }
    public DateTimeOffset? CheckOut { get; set; }
    public double? CheckInLatitude { get; set; }
    public double? CheckInLongitude { get; set; }
    public double? CheckOutLatitude { get; set; }
    public double? CheckOutLongitude { get; set; }
    public EmployeeAccount? Employee { get; set; }
    [NotMapped]
    public string Status => CheckOut is null ? "Đang làm việc" : "Đã hoàn thành";
    [NotMapped]
    public double? WorkedHours => CheckOut is null ? null : Math.Round((CheckOut.Value - CheckIn).TotalHours, 2);
}
