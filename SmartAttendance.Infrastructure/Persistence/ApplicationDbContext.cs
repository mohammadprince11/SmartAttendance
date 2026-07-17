using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<EmployeeShift> EmployeeShifts => Set<EmployeeShift>();

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();

    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<SystemUserPermission> SystemUserPermissions => Set<SystemUserPermission>();

    public DbSet<EmployeeViolationCase> EmployeeViolationCases => Set<EmployeeViolationCase>();

    public DbSet<CompanyPayrollSetting> CompanyPayrollSettings => Set<CompanyPayrollSetting>();

    public DbSet<PayrollCutoffPolicy> PayrollCutoffPolicies => Set<PayrollCutoffPolicy>();

    public DbSet<PayrollCutoffPolicyType> PayrollCutoffPolicyTypes => Set<PayrollCutoffPolicyType>();

    public DbSet<AnnouncementGroup> AnnouncementGroups => Set<AnnouncementGroup>();

    public DbSet<AnnouncementContent> AnnouncementContents => Set<AnnouncementContent>();

    public DbSet<AnnouncementTemplate> AnnouncementTemplates => Set<AnnouncementTemplate>();

    public DbSet<AnnouncementSignature> AnnouncementSignatures => Set<AnnouncementSignature>();

    public DbSet<AnnouncementAudienceRule> AnnouncementAudienceRules => Set<AnnouncementAudienceRule>();

    public DbSet<HrJobPosition> HrJobPositions => Set<HrJobPosition>();

    public DbSet<AnnouncementRecipient> AnnouncementRecipients => Set<AnnouncementRecipient>();

    public DbSet<AnnouncementChannel> AnnouncementChannels => Set<AnnouncementChannel>();

    public DbSet<AnnouncementAttachment> AnnouncementAttachments => Set<AnnouncementAttachment>();

    public DbSet<AnnouncementReadReceipt> AnnouncementReadReceipts => Set<AnnouncementReadReceipt>();

    public DbSet<AnnouncementComment> AnnouncementComments => Set<AnnouncementComment>();

    public DbSet<AnnouncementReaction> AnnouncementReactions => Set<AnnouncementReaction>();

    public DbSet<AnnouncementAuditLog> AnnouncementAuditLogs => Set<AnnouncementAuditLog>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<UserNotificationRecipient> UserNotificationRecipients => Set<UserNotificationRecipient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}