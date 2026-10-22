namespace ChamCong.Api.Models;

public sealed record TodayAttendanceResponse(string EmployeeId, DateOnly Date, bool CheckedIn, AttendanceRecord? Record);
