using ChamCong.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChamCong.Api.Data;

public sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : DbContext(options)
{
    public DbSet<EmployeeAccount> Employees => Set<EmployeeAccount>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public void RemoveLegacyLoginOrVersionColumn()
    {
        Database.ExecuteSqlRaw("""
            DECLARE @EmployeesObjectId int = OBJECT_ID(N'dbo.Employees');
            DECLARE @ColumnId int;

            SELECT @ColumnId = c.column_id
            FROM sys.columns c
            WHERE c.object_id = @EmployeesObjectId
              AND c.name = N'LoginOrVersion';

            IF @EmployeesObjectId IS NOT NULL AND @ColumnId IS NOT NULL
            BEGIN
                -- Remove any default constraint first so SQL Server permits
                -- dropping this unused legacy column.
                DECLARE @DropDefaults nvarchar(max) = N'';
                SELECT @DropDefaults = @DropDefaults +
                    N'ALTER TABLE dbo.Employees DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
                FROM sys.default_constraints dc
                WHERE dc.parent_object_id = @EmployeesObjectId
                  AND dc.parent_column_id = @ColumnId;

                IF LEN(@DropDefaults) > 0 EXEC sys.sp_executesql @DropDefaults;

                -- Drop ordinary indexes that refer to the obsolete column.
                DECLARE @DropIndexes nvarchar(max) = N'';
                SELECT @DropIndexes = @DropIndexes +
                    N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Employees;'
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.object_id = @EmployeesObjectId
                  AND ic.column_id = @ColumnId
                  AND i.name IS NOT NULL
                  AND i.is_primary_key = 0
                  AND i.is_unique_constraint = 0;

                IF LEN(@DropIndexes) > 0 EXEC sys.sp_executesql @DropIndexes;

                ALTER TABLE dbo.Employees DROP COLUMN [LoginOrVersion];
            END;
            """);
    }

    public void EnsureEmailOtpColumns()
    {
        Database.ExecuteSqlRaw("""
            IF COL_LENGTH('dbo.Employees', 'Email') IS NULL
                ALTER TABLE dbo.Employees ADD Email nvarchar(256) NOT NULL CONSTRAINT DF_Employees_Email DEFAULT('');
            IF COL_LENGTH('dbo.Employees', 'EmailVerified') IS NULL
                ALTER TABLE dbo.Employees ADD EmailVerified bit NOT NULL CONSTRAINT DF_Employees_EmailVerified DEFAULT(0);
            IF COL_LENGTH('dbo.Employees', 'EmailOtpHash') IS NULL ALTER TABLE dbo.Employees ADD EmailOtpHash nvarchar(64) NULL;
            IF COL_LENGTH('dbo.Employees', 'EmailOtpSalt') IS NULL ALTER TABLE dbo.Employees ADD EmailOtpSalt nvarchar(64) NULL;
            IF COL_LENGTH('dbo.Employees', 'EmailOtpPurpose') IS NULL ALTER TABLE dbo.Employees ADD EmailOtpPurpose nvarchar(30) NULL;
            IF COL_LENGTH('dbo.Employees', 'EmailOtpExpiresAt') IS NULL ALTER TABLE dbo.Employees ADD EmailOtpExpiresAt datetimeoffset NULL;
            IF COL_LENGTH('dbo.Employees', 'EmailOtpLastSentAt') IS NULL ALTER TABLE dbo.Employees ADD EmailOtpLastSentAt datetimeoffset NULL;
            IF COL_LENGTH('dbo.Employees', 'EmailOtpAttempts') IS NULL
                ALTER TABLE dbo.Employees ADD EmailOtpAttempts int NOT NULL CONSTRAINT DF_Employees_EmailOtpAttempts DEFAULT(0);
            IF COL_LENGTH('dbo.Employees', 'AvatarBase64') IS NULL ALTER TABLE dbo.Employees ADD AvatarBase64 nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Employees', 'AvatarBase64') IS NOT NULL AND EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'dbo.Employees')
                  AND name = N'AvatarBase64'
                  AND (system_type_id <> 231 OR max_length <> -1 OR is_nullable = 0)
            )
                ALTER TABLE dbo.Employees ALTER COLUMN AvatarBase64 nvarchar(max) NULL;
            EXEC(N'UPDATE dbo.Employees SET Email = LOWER(Username) + ''@legacy.local'' WHERE Email IS NULL OR Email = '''';');
            EXEC(N'IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ''IX_Employees_Email'' AND object_id = OBJECT_ID(''dbo.Employees''))
                CREATE UNIQUE INDEX IX_Employees_Email ON dbo.Employees(Email);');
            """);
    }

    public void EnsureAttendanceLocationColumns()
    {
        Database.ExecuteSqlRaw("""
            IF COL_LENGTH('dbo.AttendanceRecords', 'CheckInLatitude') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD CheckInLatitude float NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'CheckInLongitude') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD CheckInLongitude float NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'CheckOutLatitude') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD CheckOutLatitude float NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'CheckOutLongitude') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD CheckOutLongitude float NULL;
            """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeAccount>(entity =>
        {
            entity.HasKey(employee => employee.Id);
            entity.Property(employee => employee.Id).HasMaxLength(20);
            entity.Property(employee => employee.Username).HasMaxLength(40).IsRequired();
            entity.HasIndex(employee => employee.Username).IsUnique();
            entity.Property(employee => employee.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(employee => employee.Email).IsUnique();
            entity.Property(employee => employee.EmailOtpHash).HasMaxLength(64);
            entity.Property(employee => employee.EmailOtpSalt).HasMaxLength(64);
            entity.Property(employee => employee.EmailOtpPurpose).HasMaxLength(30);
            entity.Property(employee => employee.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(employee => employee.Department).HasMaxLength(100);
            entity.Property(employee => employee.Role).HasMaxLength(20).IsRequired();
            entity.Property(employee => employee.AvatarBase64).HasColumnType("nvarchar(max)");
            entity.Property(employee => employee.PasswordHash).HasMaxLength(100).IsRequired();
            entity.Property(employee => employee.PasswordSalt).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).HasMaxLength(40);
            entity.Property(record => record.EmployeeId).HasMaxLength(20).IsRequired();
            entity.HasIndex(record => new { record.EmployeeId, record.Date });
            entity.HasOne(record => record.Employee).WithMany(employee => employee.AttendanceRecords)
                .HasForeignKey(record => record.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
