using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// حاجز عزل المستأجرين على مستوى الحفظ: أي موظف يُكتب — من الشاشات أو
    /// الاستيراد أو أي مسار مستقبلي — تُشتقّ شركته من فرعه قبل الحفظ.
    ///
    /// وضعه هنا لا بكل مسار على حدة مقصود: مساران للاستيراد كانا يضبطان الفرع
    /// والقسم دون الشركة، وأي مسار جديد سينسى مثلهما. مركزية القاعدة تجعل النسيان
    /// مستحيلاً بدل أن تجعله مكلفاً — وهي شرط تشديد العمود إلى NOT NULL.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await FillEmployeeCompanyFromBranchAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        FillEmployeeCompanyFromBranchAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges();
    }

    private async Task FillEmployeeCompanyFromBranchAsync(CancellationToken cancellationToken)
    {
        var pending = ChangeTracker.Entries<Employee>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Where(entry => entry.Entity.BranchId > 0)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var branchIds = pending.Select(entry => entry.Entity.BranchId).Distinct().ToList();

        var companyByBranch = await Branches
            .AsNoTracking()
            .Where(branch => branchIds.Contains(branch.Id))
            .Select(branch => new { branch.Id, branch.CompanyId })
            .ToDictionaryAsync(x => x.Id, x => x.CompanyId, cancellationToken);

        foreach (var entry in pending)
        {
            // فرع غير موجود ⟹ لا نخمّن شركة؛ تُترك كما هي ويفشل قيد المفتاح
            // الأجنبي بوضوح بدل كتابة انتماء مخترَع.
            if (companyByBranch.TryGetValue(entry.Entity.BranchId, out var companyId) &&
                companyId > 0 &&
                entry.Entity.CompanyId != companyId)
            {
                entry.Entity.CompanyId = companyId;
            }
        }
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

    public DbSet<EmployeeDependent> EmployeeDependents => Set<EmployeeDependent>();

    public DbSet<EmployeeFileRecord> EmployeeFileRecords => Set<EmployeeFileRecord>();

    public DbSet<EmployeeFinancialInfo> EmployeeFinancialInfos => Set<EmployeeFinancialInfo>();

    public DbSet<EmployeeAllowance> EmployeeAllowances => Set<EmployeeAllowance>();

    public DbSet<EmployeeContract> EmployeeContracts => Set<EmployeeContract>();

    public DbSet<HrTaskTemplate> HrTaskTemplates => Set<HrTaskTemplate>();

    public DbSet<EmployeeTask> EmployeeTasks => Set<EmployeeTask>();

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