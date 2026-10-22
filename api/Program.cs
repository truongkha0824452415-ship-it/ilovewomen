using ChamCong.Api.Security;
using ChamCong.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using ChamCong.Api.Models;
using Microsoft.OpenApi.Models;
using ChamCong.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "API Chấm công nhân viên", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Description = "Nhập JWT nhận được từ POST /api/auth/login."
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services.AddScoped<AttendanceDatabase>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<EmailOtpService>();
builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Chấm công API v1"));
}
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api", () => Results.Ok(new
{
    app = "Chấm công nhân viên",
    endpoints = new[]
    {
        "POST /api/auth/register", "POST /api/auth/verify-email", "POST /api/auth/login",
        "POST /api/auth/verify-login-otp", "POST /api/auth/resend-email-verification",
        "GET /api/employees/me", "GET /api/employees (Admin)", "POST /api/employees (Admin)",
        "PUT /api/employees/{id} (Admin)", "DELETE /api/employees/{id} (Admin)",
        "GET /api/attendance/today", "GET /api/attendance/history?limit=30",
        "POST /api/attendance/check-in", "POST /api/attendance/check-out",
        "GET /api/attendance/admin/history (Admin)", "PUT /api/attendance/{id} (Admin)", "DELETE /api/attendance/{id} (Admin)"
    }
})).AllowAnonymous();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var databaseContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
    databaseContext.Database.EnsureCreated();
    databaseContext.EnsureLegacyLoginOrVersionDefault();
    databaseContext.EnsureEmailOtpColumns();
    databaseContext.EnsureAttendanceLocationColumns();
    var database = scope.ServiceProvider.GetRequiredService<AttendanceDatabase>();
    database.NormalizeLegacyRoles();
    var adminUsername = app.Configuration["SeedAdmin:Username"] ?? "admin";
    var adminEmail = app.Configuration["SeedAdmin:Email"] ?? "admin@example.com";
    var admin = database.FindByUsername(adminUsername);
    if (admin is null)
        database.AddEmployee(adminUsername, adminEmail, app.Configuration["SeedAdmin:DisplayName"] ?? "Quản trị viên", "Quản trị", "Admin", app.Configuration["SeedAdmin:Password"] ?? "Admin123!", emailVerified: true);
    else if (!admin.Email.Equals(adminEmail, StringComparison.OrdinalIgnoreCase))
        database.UpdateEmployee(admin.Id, adminEmail, admin.DisplayName, admin.Department, admin.Role);
}

app.Run();
