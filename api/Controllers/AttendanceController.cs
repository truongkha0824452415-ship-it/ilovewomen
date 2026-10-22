using System.Security.Claims;
using ChamCong.Api.Models;
using ChamCong.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamCong.Api.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Roles = "Admin,Employee")]
public sealed class AttendanceController(AttendanceDatabase database) : ControllerBase
{
    private string CurrentEmployeeId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet("today")]
    public IActionResult GetToday() => Ok(database.GetToday(CurrentEmployeeId));

    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] int limit = 1000) => Ok(database.GetHistory(CurrentEmployeeId, Math.Clamp(limit, 1, 1000)));

    [HttpGet("employee/{employeeId}/history"), Authorize(Policy = "AdminOnly")]
    public IActionResult GetEmployeeHistory(string employeeId, [FromQuery] int limit = 30)
        => database.FindEmployee(employeeId) is null ? NotFound(new { message = "Không tìm thấy nhân viên." }) : Ok(database.GetHistory(employeeId, Math.Clamp(limit, 1, 100)));

    [HttpGet("admin/history"), Authorize(Policy = "AdminOnly")]
    public IActionResult GetAllHistory([FromQuery] int limit = 200)
        => Ok(database.GetAllAttendance(Math.Clamp(limit, 1, 1000)).Select(record => new
        {
            record.Id, record.EmployeeId, record.Date, record.CheckIn, record.CheckOut,
            record.CheckInLatitude, record.CheckInLongitude,
            record.CheckOutLatitude, record.CheckOutLongitude,
            employeeName = record.Employee?.DisplayName ?? record.EmployeeId,
            username = record.Employee?.Username ?? ""
        }));

    [HttpPut("{id}"), Authorize(Policy = "AdminOnly")]
    public IActionResult Update(string id, AttendanceUpdateRequest request)
    {
        if (request.CheckIn is null) return BadRequest(new { message = "Vui lòng nhập giờ chấm công vào." });
        try
        {
            var record = database.UpdateAttendance(id, request.CheckIn.Value, request.CheckOut);
            return record is null ? NotFound(new { message = "Không tìm thấy bản ghi chấm công." }) : Ok(record);
        }
        catch (InvalidOperationException error) { return BadRequest(new { message = error.Message }); }
    }

    [HttpDelete("{id}"), Authorize(Policy = "AdminOnly")]
    public IActionResult Delete(string id)
        => database.DeleteAttendance(id) ? NoContent() : NotFound(new { message = "Không tìm thấy bản ghi chấm công." });

    [HttpPost("check-in")]
    public IActionResult CheckIn(AttendanceRequest request)
    {
        if (!ValidCoordinates(request)) return BadRequest(new { message = "Tọa độ vị trí không hợp lệ." });
        var result = database.CheckIn(CurrentEmployeeId, request.Latitude, request.Longitude);
        return result.Error is not null ? BadRequest(new { message = result.Error }) : Ok(result.Record);
    }

    [HttpPost("check-out")]
    public IActionResult CheckOut(AttendanceRequest request)
    {
        if (!ValidCoordinates(request)) return BadRequest(new { message = "Tọa độ vị trí không hợp lệ." });
        var result = database.CheckOut(CurrentEmployeeId, request.Latitude, request.Longitude);
        return result.Error is not null ? BadRequest(new { message = result.Error }) : Ok(result.Record);
    }

    private static bool ValidCoordinates(AttendanceRequest request) =>
        (request.Latitude is null && request.Longitude is null) ||
        (request.Latitude is >= -90 and <= 90 && request.Longitude is >= -180 and <= 180);
}
