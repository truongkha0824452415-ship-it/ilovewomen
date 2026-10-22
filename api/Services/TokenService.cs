using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChamCong.Api.Models;

namespace ChamCong.Api.Services;

public sealed class TokenService(IConfiguration configuration)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key."));

    public string Create(EmployeeAccount user)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { sub = user.Id, name = user.Username, role = user.Role, exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds() }));
        var unsigned = $"{header}.{payload}";
        return $"{unsigned}.{Sign(unsigned)}";
    }

    public ClaimsPrincipal? Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        var unsigned = $"{parts[0]}.{parts[1]}";
        var expected = Encoding.ASCII.GetBytes(Sign(unsigned));
        var supplied = Encoding.ASCII.GetBytes(parts[2]);
        if (expected.Length != supplied.Length || !CryptographicOperations.FixedTimeEquals(expected, supplied)) return null;
        try
        {
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var root = payload.RootElement;
            if (!root.TryGetProperty("exp", out var expiry) || expiry.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, root.GetProperty("sub").GetString()!),
                new Claim(ClaimTypes.Name, root.GetProperty("name").GetString()!),
                new Claim(ClaimTypes.Role, root.GetProperty("role").GetString()!)
            };
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }
        catch (Exception error) when (error is FormatException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private string Sign(string value) => Encode(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(value)));
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
