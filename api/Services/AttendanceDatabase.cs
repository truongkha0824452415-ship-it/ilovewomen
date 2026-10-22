using System.Security.Cryptography;
using System.Data;
using System.Text;
using ChamCong.Api.Data;
using ChamCong.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChamCong.Api.Services;

public sealed class AttendanceDatabase(AttendanceDbContext db)
{
    public void NormalizeLegacyRoles()
    {
        var legacyAccounts = db.Employees.Where(user => user.Role == "Customer").ToArray();
        if (legacyAccounts.Length == 0) return;
        foreach (var account in legacyAccounts) account.Role = "Employee";
        db.SaveChanges();
    }

    public IReadOnlyList<EmployeeAccount> GetEmployees() => db.Employees.AsNoTracking().OrderBy(user => user.Username).ToArray();
    public EmployeeAccount? FindEmployee(string id) => db.Employees.AsNoTracking().FirstOrDefault(user => user.Id == id);
    public EmployeeAccount? FindTrackedEmployee(string id) => db.Employees.FirstOrDefault(user => user.Id == id);
    public Task<EmployeeAccount?> FindEmployeeAsync(string id, CancellationToken cancellationToken = default) =>
        db.Employees.AsNoTracking().FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    public EmployeeAccount? FindByUsername(string username) => db.Employees.FirstOrDefault(user => user.Username.ToLower() == username.ToLower());
    public EmployeeAccount? FindByEmail(string email) => db.Employees.FirstOrDefault(user => user.Email.ToLower() == email.ToLower());

    public void SetRoleAndVerifyEmail(EmployeeAccount employee, string role)
    {
        employee.Role = role;
        employee.EmailVerified = true;
        db.SaveChanges();
    }

    public EmployeeAccount AddEmployee(string username, string email, string displayName, string department, string role, string password, bool emailVerified = false, string? avatarBase64 = null)
    {
        if (FindByUsername(username.Trim()) is not null) throw new InvalidOperationException("Tên đăng nhập đã tồn tại.");
        if (FindByEmail(email.Trim()) is not null) throw new InvalidOperationException("Email đã được sử dụng.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var employee = new EmployeeAccount
        {
            Id = "NV" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Username = username.Trim(), Email = email.Trim(), EmailVerified = emailVerified,
            DisplayName = displayName.Trim(), Department = department.Trim(), Role = role,
            AvatarBase64 = avatarBase64,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32)),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }

    public EmployeeAccount? UpdateEmployee(string id, string email, string displayName, string department, string role, string? newPassword = null, string? avatarBase64 = null)
    {
        var employee = db.Employees.FirstOrDefault(user => user.Id == id);
        if (employee is null) return null;
        if (!employee.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase) && FindByEmail(email.Trim()) is not null)
            throw new InvalidOperationException("Email đã được sử dụng.");
        if (!employee.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            employee.Email = email.Trim(); employee.EmailVerified = false;
            employee.EmailOtpLastSentAt = null;
            ClearOtp(employee);
        }
        employee.DisplayName = displayName.Trim(); employee.Department = department.Trim(); employee.Role = role;
        if (!string.IsNullOrWhiteSpace(newPassword)) SetPassword(employee, newPassword);
        if (!string.IsNullOrWhiteSpace(avatarBase64)) employee.AvatarBase64 = avatarBase64;
        db.SaveChanges();
        return employee;
    }

    public bool DeleteEmployee(string id)
    {
        var employee = db.Employees.FirstOrDefault(user => user.Id == id);
        if (employee is null) return false;
        using var transaction = db.Database.BeginTransaction();
        db.AttendanceRecords.Where(record => record.EmployeeId == id).ExecuteDelete();
        db.Employees.Remove(employee);
        db.SaveChanges();
        transaction.Commit();
        return true;
    }

    public bool VerifyPassword(EmployeeAccount employee, string password)
    {
        try
        {
            var expected = Convert.FromBase64String(employee.PasswordHash);
            var salt = Convert.FromBase64String(employee.PasswordSalt);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private static void SetPassword(EmployeeAccount employee, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        employee.PasswordSalt = Convert.ToBase64String(salt);
        employee.PasswordHash = Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32));
    }

    public void ResetPassword(EmployeeAccount employee, string newPassword)
    {
        SetPassword(employee, newPassword);
        ClearOtp(employee, resetSendLimit: true);
    }

    public bool SaveOtp(string employeeId, string purpose, string salt, string hash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var employee = db.Employees.FirstOrDefault(user => user.Id == employeeId);
        if (employee is null) return false;
        employee.EmailOtpPurpose = purpose; employee.EmailOtpSalt = salt; employee.EmailOtpHash = hash;
        employee.EmailOtpExpiresAt = expiresAt; employee.EmailOtpLastSentAt = now; employee.EmailOtpAttempts = 0;
        db.SaveChanges();
        return true;
    }

    public EmployeeAccount? FindOtpTarget(string username) => db.Employees.FirstOrDefault(user => user.Username.ToLower() == username.ToLower());

    public bool VerifyOtp(EmployeeAccount employee, string purpose, string code, string secret, DateTimeOffset now)
    {
        if (employee.EmailOtpPurpose != purpose || employee.EmailOtpHash is null || employee.EmailOtpSalt is null || employee.EmailOtpExpiresAt is null || employee.EmailOtpExpiresAt <= now || employee.EmailOtpAttempts >= 5)
        {
            employee.EmailOtpAttempts = Math.Min(employee.EmailOtpAttempts + 1, 5);
            db.SaveChanges(); return false;
        }
        var content = $"{employee.Id}|{purpose}|{employee.EmailOtpSalt}|{code}";
        var expected = Convert.FromBase64String(employee.EmailOtpHash);
        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(content));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            employee.EmailOtpAttempts++;
            db.SaveChanges(); return false;
        }
        return true;
    }

    public void MarkEmailVerified(EmployeeAccount employee)
    {
        employee.EmailVerified = true; ClearOtp(employee); employee.EmailOtpLastSentAt = null; db.SaveChanges();
    }

    public void ClearOtp(EmployeeAccount employee, bool resetSendLimit = false)
    {
        employee.EmailOtpPurpose = null; employee.EmailOtpSalt = null; employee.EmailOtpHash = null;
        employee.EmailOtpExpiresAt = null; employee.EmailOtpAttempts = 0;
        if (resetSendLimit) employee.EmailOtpLastSentAt = null;
        db.SaveChanges();
    }

    public TodayAttendanceResponse GetToday(string employeeId)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var record = db.AttendanceRecords.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId && item.CheckOut == null)
            .OrderByDescending(item => item.CheckIn).FirstOrDefault()
            ?? db.AttendanceRecords.AsNoTracking()
                .Where(item => item.EmployeeId == employeeId && item.Date == today)
                .OrderByDescending(item => item.CheckIn).FirstOrDefault();
        return new(employeeId, today, record is not null && record.CheckOut is null, record);
    }

    public IReadOnlyList<AttendanceRecord> GetHistory(string employeeId, int limit) => db.AttendanceRecords.AsNoTracking()
        .Where(item => item.EmployeeId == employeeId).OrderByDescending(item => item.CheckIn).Take(limit).ToArray();

    public IReadOnlyList<AttendanceRecord> GetAllAttendance(int limit) => db.AttendanceRecords.AsNoTracking()
        .Include(item => item.Employee).OrderByDescending(item => item.CheckIn).Take(limit).ToArray();

    public AttendanceRecord? UpdateAttendance(string id, DateTimeOffset checkIn, DateTimeOffset? checkOut)
    {
        if (checkOut is not null && checkOut <= checkIn) throw new InvalidOperationException("Giờ chấm công ra phải sau giờ chấm công vào.");
        var record = db.AttendanceRecords.FirstOrDefault(item => item.Id == id);
        if (record is null) return null;
        record.CheckIn = checkIn;
        record.CheckOut = checkOut;
        // Keep the date selected by the admin's device; converting through the
        // API host timezone can move the record to the previous or next day.
        record.Date = DateOnly.FromDateTime(checkIn.DateTime);
        db.SaveChanges();
        return record;
    }

    public bool DeleteAttendance(string id)
    {
        var record = db.AttendanceRecords.FirstOrDefault(item => item.Id == id);
        if (record is null) return false;
        db.AttendanceRecords.Remove(record);
        db.SaveChanges();
        return true;
    }

    public (AttendanceRecord? Record, string? Error) CheckIn(string employeeId, double? latitude, double? longitude)
    {
        if (!db.Employees.Any(user => user.Id == employeeId)) return (null, "Không tìm thấy nhân viên.");
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var openRecord = db.AttendanceRecords.Any(item => item.EmployeeId == employeeId && item.CheckOut == null);
        if (openRecord) return (null, "Bạn đã có ca làm chưa chấm công ra.");
        var record = new AttendanceRecord(Guid.NewGuid().ToString("N"), employeeId, today, DateTimeOffset.Now, null);
        record.CheckInLatitude = latitude;
        record.CheckInLongitude = longitude;
        db.AttendanceRecords.Add(record);
        db.SaveChanges();
        transaction.Commit();
        return (record, null);
    }

    public (AttendanceRecord? Record, string? Error) CheckOut(string employeeId, double? latitude, double? longitude)
    {
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var record = db.AttendanceRecords.Where(item => item.EmployeeId == employeeId && item.CheckOut == null).OrderByDescending(item => item.CheckIn).FirstOrDefault();
        if (record is null) return (null, "Bạn cần chấm công vào trước khi chấm công ra.");
        record.CheckOut = DateTimeOffset.Now;
        record.CheckOutLatitude = latitude;
        record.CheckOutLongitude = longitude;
        db.SaveChanges();
        transaction.Commit();
        return (record, null);
    }
}
