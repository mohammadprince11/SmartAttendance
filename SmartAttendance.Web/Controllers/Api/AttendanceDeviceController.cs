using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Integrations;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>قناة دفع البصمات الخام من أجهزة/بوابات الحضور إلى inbox موثوق.</summary>
[ApiController]
[Route("api/v1/attendance/device-punches")]
[AllowAnonymous]
public sealed class AttendanceDeviceController : ControllerBase
{
    private static readonly Regex ConnectorName = new("^[A-Za-z0-9_.-]{2,100}$", RegexOptions.Compiled);
    private readonly ApplicationDbContext _db;

    public AttendanceDeviceController(ApplicationDbContext db) => _db = db;

    public sealed record PunchRequest(
        string ExternalId, string EmployeeNo, DateTimeOffset PunchedAt,
        string? PunchType = null, string? DeviceCode = null);

    public sealed record BatchRequest(IReadOnlyList<PunchRequest> Punches);

    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Ingest(
        [FromBody] BatchRequest body,
        [FromHeader(Name = "X-Zynora-Key")] string apiKey,
        [FromHeader(Name = "X-Zynora-Connector")] string connectorKey)
    {
        var identity = await IntegrationApiKeyStore.ValidateAsync(_db, apiKey, "attendance.write");
        if (identity is null) return Unauthorized(new { message = "Integration key is invalid or lacks attendance.write." });
        var connector = connectorKey?.Trim() ?? string.Empty;
        if (!ConnectorName.IsMatch(connector))
            return BadRequest(new { message = "X-Zynora-Connector is required and invalid." });
        if (body?.Punches is null || body.Punches.Count is < 1 or > 1000)
            return BadRequest(new { message = "Punches must contain between 1 and 1000 records." });

        var punches = body.Punches.Select(item => new DevicePunchInboxStore.Punch(
            item.ExternalId ?? string.Empty, item.EmployeeNo ?? string.Empty,
            item.PunchedAt, item.PunchType, item.DeviceCode)).ToList();
        var result = await DevicePunchInboxStore.IngestAsync(_db, identity, connector, punches);
        return Accepted(new
        {
            result.Accepted,
            result.Duplicate,
            result.DeadLetter,
            connector,
            companyId = identity.CompanyId
        });
    }
}
