namespace ChamCong.Api.Models;

public sealed record AttendanceRequest(
    double? Latitude = null,
    double? Longitude = null);
