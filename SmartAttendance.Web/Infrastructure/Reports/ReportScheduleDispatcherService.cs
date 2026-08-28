using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Reports;

public sealed class ReportScheduleDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailSender _sender;
    private readonly ILogger<ReportScheduleDispatcherService> _logger;

    public ReportScheduleDispatcherService(
        IServiceScopeFactory scopeFactory, IEmailSender sender,
        ILogger<ReportScheduleDispatcherService> logger)
    {
        _scopeFactory = scopeFactory; _sender = sender; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sender.IsEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await SqlDistributedLock.TryRunAsync(db, "ZYNORA.ReportScheduleDispatcher",
                    () => DispatchAsync(db, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Report schedule cycle failed."); }
        }
    }

    private async Task DispatchAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        foreach (var schedule in await ReportScheduleStore.ClaimDueAsync(db, 20))
        {
            try
            {
                var user = await db.SystemUsers.AsNoTracking()
                    .Include(item => item.Employee)
                    .SingleOrDefaultAsync(item => item.Id == schedule.OwnerUserId && !item.IsDeleted && item.IsActive,
                        cancellationToken);
                if (user is null || !string.Equals(user.UserName, schedule.OwnerUser, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Schedule owner is inactive or changed.");
                if (user.Role != SystemUserRole.Admin && user.Employee?.CompanyId != schedule.CompanyId)
                    throw new UnauthorizedAccessException("Schedule owner no longer belongs to the report company.");

                var reportScope = CompanyScope.ForCompanies(new[] { schedule.CompanyId });
                var report = await PeopleReportsStore.GetAsync(db, reportScope, schedule.ReportId);
                if (report is null || report.CompanyId != schedule.CompanyId || report.IsSystem ||
                    !string.Equals(report.OwnerUser, schedule.OwnerUser, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Saved report is no longer owned and accessible.");

                var dataset = PeopleReportCatalog.GetDataset(report.DatasetKey)
                    ?? throw new InvalidOperationException("Report dataset no longer exists.");
                var reportGroup = dataset.Module.Equals("attendance", StringComparison.OrdinalIgnoreCase) ? "Attendance" :
                    dataset.Module.Equals("payroll", StringComparison.OrdinalIgnoreCase) ? "Payroll" :
                    dataset.Key.Equals("leaves", StringComparison.OrdinalIgnoreCase) ? "Leaves" : "Employees";
                if (user.Role != SystemUserRole.Admin &&
                    !await AccessRoleStore.IsGrantedOrUnrestrictedAsync(db, user.Id, AccessRoleStore.TypeReports, reportGroup))
                    throw new UnauthorizedAccessException("Report permission was revoked after scheduling.");

                var columns = report.Columns.Select(key => dataset.Columns.FirstOrDefault(column =>
                        column.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    .Where(column => column is not null).Select(column => column!).ToList();
                if (columns.Count == 0) columns = dataset.Columns.ToList();
                var rows = await PeopleReportCatalog.LoadAsync(db, report.DatasetKey, report.FilterKey,
                    new PeopleReportCatalog.ReportFilters { Scope = reportScope, CompanyId = schedule.CompanyId });
                var export = ReportExportService.Build("csv", report.Name,
                    columns.Select(column => new ReportExportService.Column(column.Key, column.Label)).ToList(), rows);
                var attachment = new EmailAttachment($"report-{report.Id}-{DateTime.UtcNow:yyyyMMdd}.csv", export.ContentType, export.Bytes);

                var recipients = schedule.RecipientsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var allowedRecipients = (await db.SystemUsers.AsNoTracking()
                        .Where(item => !item.IsDeleted && item.IsActive && item.EmployeeId != null &&
                                       item.Employee != null && item.Employee.CompanyId == schedule.CompanyId && item.Email != null)
                        .Select(item => item.Email!).ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (recipients.Length == 0 || recipients.Any(recipient => !allowedRecipients.Contains(recipient)))
                    throw new UnauthorizedAccessException("One or more recipients are no longer active in the report company.");

                foreach (var recipient in recipients)
                {
                    if (await ReportScheduleStore.DeliveryExistsAsync(db, schedule.Id, schedule.NextRunAt, recipient) > 0)
                        continue;
                    var result = await _sender.SendAsync(new EmailMessage(recipient,
                        $"تقرير مجدول: {report.Name}", $"تم إنشاء التقرير تلقائياً. عدد السجلات: {rows.Count}.",
                        new[] { attachment }), cancellationToken);
                    if (!result.Sent) throw new InvalidOperationException(result.Error ?? "Email delivery failed.");
                    await ReportScheduleStore.RecordDeliveryAsync(db, schedule.Id, schedule.NextRunAt, recipient);
                }
                await ReportScheduleStore.MarkAsync(db, schedule, true, null);
            }
            catch (Exception exception)
            {
                await ReportScheduleStore.MarkAsync(db, schedule, false, exception.Message);
                _logger.LogWarning(exception, "Scheduled report {ScheduleId} failed.", schedule.Id);
            }
        }
    }
}
