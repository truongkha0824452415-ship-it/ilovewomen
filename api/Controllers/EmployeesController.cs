using System.Security.Claims;
using ChamCong.Api.Models;
using ChamCong.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamCong.Api.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public sealed class EmployeesController(AttendanceDatabase database) : ControllerBase
{
    [HttpGet("me")]
    public IActionResult GetCurrent() => FindCurrent() is { } user ? Ok(EmployeeResponse.From(user)) : NotFound();

    [HttpGet, Authorize(Policy = "AdminOnly")]
    public IActionResult GetAll() => Ok(database.GetEmployees().Select(EmployeeResponse.From));

    [HttpGet("{id}"), Authorize(Policy = "AdminOnly")]
    public IActionResult GetById(string id) => database.FindEmployee(id) is { } user ? Ok(EmployeeResponse.From(user)) : NotFound(new { message = "Không tìm thấy nhân viên." });

    [HttpPost, Authorize(Policy = "AdminOnly")]
    public IActionResult Create(EmployeeRequest request)
    {
        if (!ValidRole(request.Role)) return BadRequest(new { message = "Role phải là Admin hoặc Employee." });
        if (!ValidAvatar(request.AvatarBase64)) return BadRequest(new { message = "Ảnh đại diện không hợp lệ." });
        if (database.FindByUsername(request.Username.Trim()) is not null) return Conflict(new { message = "Tên đăng nhập đã tồn tại." });
        if (database.FindByEmail(request.Email.Trim()) is not null) return Conflict(new { message = "Email đã được sử dụng." });
        try
        {
            // Accounts provisioned by an administrator are trusted and can sign in
            // immediately; self-service registrations still require email OTP.
            var user = database.AddEmployee(request.Username, request.Email, request.DisplayName, request.Department, request.Role, request.Password, emailVerified: true, avatarBase64: request.AvatarBase64);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, EmployeeResponse.From(user));
        }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
    }

    [HttpPut("{id}"), Authorize(Policy = "AdminOnly")]
    public IActionResult Update(string id, EmployeeUpdateRequest request)
    {
        if (!ValidRole(request.Role)) return BadRequest(new { message = "Role phải là Admin hoặc Employee." });
        var existing = database.FindEmployee(id);
        if (existing is null) return NotFound(new { message = "Không tìm thấy nhân viên." });
        if (request.AvatarBase64 is not null && !ValidAvatar(request.AvatarBase64))
            return BadRequest(new { message = "Ảnh đại diện không hợp lệ." });
        if (existing.AvatarBase64 is null && request.AvatarBase64 is null)
            return BadRequest(new { message = "Hãy thêm ảnh đại diện cho tài khoản." });
        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id.Equals(currentId, StringComparison.OrdinalIgnoreCase) && request.Role != "Admin" &&
            !database.GetEmployees().Any(employee => employee.Id != id && employee.Role == "Admin"))
            return BadRequest(new { message = "Hãy cấp quyền Admin cho tài khoản khác trước khi tự chuyển sang Nhân viên." });
        EmployeeAccount? updated;
        try { updated = database.UpdateEmployee(id, request.Email, request.DisplayName, request.Department, request.Role, request.Password, request.AvatarBase64); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        return updated is null ? NotFound(new { message = "Không tìm thấy nhân viên." }) : Ok(EmployeeResponse.From(updated));
    }

    [HttpDelete("{id}"), Authorize(Policy = "AdminOnly")]
    public IActionResult Delete(string id)
    {
        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id.Equals(currentId, StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Không thể xóa tài khoản Admin đang đăng nhập." });
        return database.DeleteEmployee(id) ? NoContent() : NotFound(new { message = "Không tìm thấy nhân viên." });
    }

    private EmployeeAccount? FindCurrent() => database.FindEmployee(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
    private static bool ValidRole(string role) => role is "Admin" or "Employee";
    private static bool ValidAvatar(string value)
    {
        if (value.Length is < 100 or > 1_400_000) return false;
        try { return Convert.FromBase64String(value).Length <= 1_000_000; }
        catch (FormatException) { return false; }
    }
}
