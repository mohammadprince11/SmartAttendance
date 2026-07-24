using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Reports;

/// <summary>
/// Kayan-style people reports: every report is a dataset (source) + a fixed
/// filter + a set of columns. System reports are seeded rows; custom reports are
/// built by the user from the same datasets/columns, so the whole page stays
/// data-driven (no per-report code).
/// </summary>
public static class PeopleReportCatalog
{
    /// <summary>نوع مدخل المرشح بدرج «بحث وتصفية» (نمط كيان: type-aware).</summary>
    public enum FilterKind { Text, Select, DateRange }

    public sealed record ReportColumn(string Key, string Label, FilterKind Filter = FilterKind.Text);

    /// <summary>
    /// مصدر بيانات التقرير. <see cref="Module"/> يفصل مصادر الأشخاص عن الحضور
    /// (نمط كيان: محرك تقارير واحد بمصادر متعددة) فتعرض كل صفحة مصادرها فقط.
    /// </summary>
    public sealed record ReportDataset(string Key, string Label, IReadOnlyList<ReportColumn> Columns, string Module = "people");

    public sealed class ReportFilters
    {
        public int? CompanyId { get; set; }
        public string? Search { get; set; }
        public bool ActiveOnly { get; set; }

        /// <summary>مدى تاريخ مصادر الحضور (بارامتر تشغيل خادمي نمط كيان). null ⟶ الشهر الحالي.</summary>
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public static readonly IReadOnlyList<ReportDataset> Datasets = new[]
    {
        new ReportDataset("employees", "الموظفون", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("name", "اسم الموظف"),
            new ReportColumn("gender", "الجنس", FilterKind.Select),
            new ReportColumn("birthdate", "تاريخ الولادة", FilterKind.DateRange),
            new ReportColumn("nationality", "الجنسية", FilterKind.Select),
            new ReportColumn("country", "البلد", FilterKind.Select),
            new ReportColumn("maritalstatus", "الحالة الاجتماعية", FilterKind.Select),
            new ReportColumn("nationalid", "رقم الهوية"),
            new ReportColumn("phone", "الهاتف"),
            new ReportColumn("email", "البريد الإلكتروني"),
            new ReportColumn("branch", "الفرع", FilterKind.Select),
            new ReportColumn("department", "القسم", FilterKind.Select),
            new ReportColumn("position", "المنصب", FilterKind.Select),
            new ReportColumn("hiredate", "تاريخ التعيين", FilterKind.DateRange),
            new ReportColumn("contracttype", "نوع العقد", FilterKind.Select),
            new ReportColumn("contractend", "انتهاء العقد", FilterKind.DateRange),
            new ReportColumn("status", "حالة التوظيف", FilterKind.Select),
            new ReportColumn("manager", "المدير المباشر", FilterKind.Select),
            new ReportColumn("active", "فعال", FilterKind.Select),
            new ReportColumn("serviceend", "تاريخ انتهاء الخدمة", FilterKind.DateRange),
            new ReportColumn("serviceendtype", "نوع انتهاء الخدمة", FilterKind.Select)
        }),
        new ReportDataset("dependents", "العائلة والمعالون", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("employee", "اسم الموظف"),
            new ReportColumn("name", "الاسم"),
            new ReportColumn("relation", "صلة القرابة", FilterKind.Select),
            new ReportColumn("birthdate", "تاريخ الولادة", FilterKind.DateRange),
            new ReportColumn("nationality", "الجنسية", FilterKind.Select),
            new ReportColumn("nationalid", "رقم الهوية"),
            new ReportColumn("isdependent", "معال", FilterKind.Select),
            new ReportColumn("isemergency", "جهة طوارئ", FilterKind.Select),
            new ReportColumn("phone", "هاتف محمول"),
            new ReportColumn("maritalstatus", "الحالة الاجتماعية", FilterKind.Select)
        }),
        new ReportDataset("records", "سجلات ملف الموظف", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("employee", "اسم الموظف"),
            new ReportColumn("type", "نوع السجل", FilterKind.Select),
            new ReportColumn("title", "العنوان"),
            new ReportColumn("subtitle", "التفاصيل"),
            new ReportColumn("country", "البلد", FilterKind.Select),
            new ReportColumn("refno", "رقم مرجعي"),
            new ReportColumn("fromdate", "من تاريخ", FilterKind.DateRange),
            new ReportColumn("todate", "إلى تاريخ", FilterKind.DateRange),
            new ReportColumn("amount", "المبلغ"),
            new ReportColumn("iscurrent", "حالي", FilterKind.Select),
            new ReportColumn("attachment", "مرفق")
        }),
        new ReportDataset("documents", "وثائق الموظفين", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("employee", "اسم الموظف"),
            new ReportColumn("doctype", "نوع الوثيقة", FilterKind.Select),
            new ReportColumn("filename", "الملف"),
            new ReportColumn("expirydate", "تاريخ الانتهاء", FilterKind.DateRange),
            new ReportColumn("uploadedat", "تاريخ الرفع", FilterKind.DateRange),
            new ReportColumn("uploadedby", "رفع بواسطة", FilterKind.Select)
        }),
        new ReportDataset("leaves", "طلبات الإجازة", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("employee", "اسم الموظف"),
            new ReportColumn("type", "نوع الإجازة", FilterKind.Select),
            new ReportColumn("status", "الحالة", FilterKind.Select),
            new ReportColumn("fromdate", "من تاريخ", FilterKind.DateRange),
            new ReportColumn("todate", "إلى تاريخ", FilterKind.DateRange),
            new ReportColumn("days", "الأيام"),
            new ReportColumn("reason", "السبب")
        }),
        new ReportDataset("violations", "حالات المخالفات", new[]
        {
            new ReportColumn("refno", "رقم المرجع"),
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("employee", "اسم الموظف"),
            new ReportColumn("category", "الفئة", FilterKind.Select),
            new ReportColumn("title", "المخالفة"),
            new ReportColumn("eventdate", "تاريخ الحدث", FilterKind.DateRange),
            new ReportColumn("status", "الحالة", FilterKind.Select),
            new ReportColumn("action", "الإجراء")
        }),

        // ===== مصادر الحضور (نمط كيان: التقارير الجدولية فوق يوميات المحرك الرسمي) =====
        new ReportDataset("att_daily", "حضور الموظفين — التفاصيل اليومية", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("name", "اسم الموظف"),
            new ReportColumn("date", "تاريخ الحضور", FilterKind.DateRange),
            new ReportColumn("weekday", "اليوم", FilterKind.Select),
            new ReportColumn("shift", "المناوبة", FilterKind.Select),
            new ReportColumn("daykind", "نوع اليوم", FilterKind.Select),
            new ReportColumn("status", "الحالة", FilterKind.Select),
            new ReportColumn("checkin", "وقت الدخول"),
            new ReportColumn("checkout", "وقت الخروج"),
            new ReportColumn("late", "ساعات التأخير"),
            new ReportColumn("early", "ساعات الخروج المبكر"),
            new ReportColumn("worked", "ساعات العمل")
        }, "attendance"),
        new ReportDataset("att_summary", "ملخص الحضور الشهري للموظف", new[]
        {
            new ReportColumn("no", "الرقم الوظيفي"),
            new ReportColumn("name", "اسم الموظف"),
            new ReportColumn("workdays", "أيام العمل"),
            new ReportColumn("presentdays", "أيام الحضور"),
            new ReportColumn("latedays", "أيام التأخير"),
            new ReportColumn("absentdays", "أيام الغياب"),
            new ReportColumn("incompletedays", "أيام البصمة الناقصة"),
            new ReportColumn("leavedays", "أيام الإجازة"),
            new ReportColumn("holidaydays", "أيام العطل الرسمية"),
            new ReportColumn("latehours", "إجمالي ساعات التأخير"),
            new ReportColumn("earlyhours", "إجمالي ساعات الخروج المبكر"),
            new ReportColumn("workedhours", "إجمالي ساعات العمل")
        }, "attendance")
    };

    public static ReportDataset? GetDataset(string key) =>
        Datasets.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>مصادر البيانات لموديول محدّد (people / attendance) — تعرض كل صفحة مصادرها فقط.</summary>
    public static IReadOnlyList<ReportDataset> DatasetsFor(string? module) =>
        Datasets.Where(d => d.Module.Equals(module ?? "people", StringComparison.OrdinalIgnoreCase)).ToList();

    public static async Task<List<Dictionary<string, string>>> LoadAsync(
        ApplicationDbContext db,
        string datasetKey,
        string? filterKey,
        ReportFilters filters)
    {
        return datasetKey.ToLowerInvariant() switch
        {
            "employees" => await LoadEmployeesAsync(db, filterKey, filters),
            "dependents" => await LoadDependentsAsync(db, filterKey, filters),
            "records" => await LoadRecordsAsync(db, filterKey, filters),
            "documents" => await LoadDocumentsAsync(db, filters),
            "leaves" => await LoadLeavesAsync(db, filters),
            "violations" => await LoadViolationsAsync(db, filters),
            "att_daily" => await LoadAttendanceDailyAsync(db, filters),
            "att_summary" => await LoadAttendanceSummaryAsync(db, filters),
            _ => new List<Dictionary<string, string>>()
        };
    }

    /// <summary>اليوم بالعربية من التاريخ (لعمود «اليوم» بتقرير الحضور).</summary>
    private static string WeekdayText(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => "السبت",
        DayOfWeek.Sunday => "الأحد",
        DayOfWeek.Monday => "الإثنين",
        DayOfWeek.Tuesday => "الثلاثاء",
        DayOfWeek.Wednesday => "الأربعاء",
        DayOfWeek.Thursday => "الخميس",
        DayOfWeek.Friday => "الجمعة",
        _ => ""
    };

    private static string H(decimal hours) => hours == 0 ? "0" : hours.ToString("0.##");

    /// <summary>مدى تقرير الحضور: من/إلى المُمرَّرين، وإلا الشهر الحالي كاملاً.</summary>
    private static (DateOnly From, DateOnly To) AttendanceRange(ReportFilters f)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = f.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = f.To ?? from.AddMonths(1).AddDays(-1);
        if (to < from) to = from;
        return (from, to);
    }

    private static async Task<List<Dictionary<string, string>>> LoadAttendanceDailyAsync(
        ApplicationDbContext db, ReportFilters f)
    {
        var (from, to) = AttendanceRange(f);
        var rows = await DayAttendanceStore.ListRangeAsync(db, from, to, f.Search);

        return rows.Select(r => new Dictionary<string, string>
        {
            ["no"] = r.EmployeeNo,
            ["name"] = r.EmployeeName,
            ["date"] = r.WorkDate.ToString("yyyy-MM-dd"),
            ["weekday"] = WeekdayText(r.WorkDate),
            ["shift"] = r.ShiftName ?? "",
            ["daykind"] = r.DayKind switch { "Weekend" => "عطلة أسبوعية", "Rest" => "يوم راحة", _ => "عمل" },
            ["status"] = DayAttendanceStore.StatusLabel(r.Status),
            ["checkin"] = r.CheckIn?.ToString("HH:mm") ?? "",
            ["checkout"] = r.CheckOut?.ToString("HH:mm") ?? "",
            ["late"] = H(r.LateHours),
            ["early"] = H(r.EarlyLeaveHours),
            ["worked"] = H(r.WorkedHours)
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> LoadAttendanceSummaryAsync(
        ApplicationDbContext db, ReportFilters f)
    {
        var (from, to) = AttendanceRange(f);
        var rows = await DayAttendanceStore.ListRangeAsync(db, from, to, f.Search);

        return rows
            .GroupBy(r => (r.EmployeeId, r.EmployeeNo, r.EmployeeName))
            .OrderBy(g => g.Key.EmployeeNo)
            .Select(g => new Dictionary<string, string>
            {
                ["no"] = g.Key.EmployeeNo,
                ["name"] = g.Key.EmployeeName,
                ["workdays"] = g.Count(r => r.DayKind == "Work").ToString(),
                ["presentdays"] = g.Count(r => r.Status is "Present" or "Late").ToString(),
                ["latedays"] = g.Count(r => r.Status == "Late").ToString(),
                ["absentdays"] = g.Count(r => r.Status == "Absent").ToString(),
                ["incompletedays"] = g.Count(r => r.Status == "Incomplete").ToString(),
                ["leavedays"] = g.Count(r => r.Status == "Leave").ToString(),
                ["holidaydays"] = g.Count(r => r.Status == "Holiday").ToString(),
                ["latehours"] = H(g.Sum(r => r.LateHours)),
                ["earlyhours"] = H(g.Sum(r => r.EarlyLeaveHours)),
                ["workedhours"] = H(g.Sum(r => r.WorkedHours))
            }).ToList();
    }

    private static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd") ?? "";
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";
    private static string B(bool b) => b ? "نعم" : "لا";

    private static async Task<List<Dictionary<string, string>>> LoadEmployeesAsync(
        ApplicationDbContext db, string? filterKey, ReportFilters f)
    {
        var q = db.Employees.AsNoTracking().Where(e => !e.IsDeleted);
        if (f.ActiveOnly) q = q.Where(e => e.IsActive);
        if (f.CompanyId.HasValue) q = q.Where(e => e.Branch.CompanyId == f.CompanyId.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(e => e.FullName.Contains(s) || e.EmployeeNo.Contains(s));
        }

        q = (filterKey ?? "").ToLowerInvariant() switch
        {
            "terminated" => q.Where(e => e.ServiceEndDate != null || !e.IsActive),
            "contracts" => q.Where(e => e.ContractEndDate != null),
            "probation" => q.Where(e => e.EmploymentStatus != null && e.EmploymentStatus.Contains("تجربة")),
            _ => q
        };

        var rows = await q
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                e.Id, e.EmployeeNo, e.FullName, e.Gender, e.BirthDate, e.Nationality, e.Country,
                e.MaritalStatus, e.NationalId, e.Phone, e.Email,
                Branch = e.Branch.Name, Department = e.Department.Name,
                e.Position, e.HireDate, e.ContractType, e.ContractEndDate, e.EmploymentStatus,
                e.DirectManagerId, e.IsActive, e.ServiceEndDate, e.ServiceEndType
            })
            .ToListAsync();

        var managerIds = rows.Where(r => r.DirectManagerId.HasValue).Select(r => r.DirectManagerId!.Value).Distinct().ToList();
        var managers = managerIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Employees.AsNoTracking()
                .Where(e => managerIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName);

        return rows.Select(r => new Dictionary<string, string>
        {
            ["no"] = r.EmployeeNo,
            ["name"] = r.FullName,
            ["gender"] = r.Gender ?? "",
            ["birthdate"] = D(r.BirthDate),
            ["nationality"] = r.Nationality ?? "",
            ["country"] = r.Country ?? "",
            ["maritalstatus"] = r.MaritalStatus ?? "",
            ["nationalid"] = r.NationalId ?? "",
            ["phone"] = r.Phone ?? "",
            ["email"] = r.Email ?? "",
            ["branch"] = r.Branch,
            ["department"] = r.Department,
            ["position"] = r.Position ?? "",
            ["hiredate"] = D(r.HireDate),
            ["contracttype"] = r.ContractType ?? "",
            ["contractend"] = D(r.ContractEndDate),
            ["status"] = r.EmploymentStatus ?? "",
            ["manager"] = r.DirectManagerId.HasValue && managers.TryGetValue(r.DirectManagerId.Value, out var m) ? m : "",
            ["active"] = B(r.IsActive),
            ["serviceend"] = D(r.ServiceEndDate),
            ["serviceendtype"] = r.ServiceEndType ?? ""
        }).ToList();
    }

    private static string RelationText(DependentRelation relation) => relation switch
    {
        DependentRelation.Spouse => "شريك",
        DependentRelation.Son => "إبن",
        DependentRelation.Daughter => "بنت",
        DependentRelation.Relative => "قريب",
        _ => relation.ToString()
    };

    private static async Task<List<Dictionary<string, string>>> LoadDependentsAsync(
        ApplicationDbContext db, string? filterKey, ReportFilters f)
    {
        var q = db.EmployeeDependents.AsNoTracking().Where(d => !d.Employee.IsDeleted);
        if (f.ActiveOnly) q = q.Where(d => d.Employee.IsActive);
        if (f.CompanyId.HasValue) q = q.Where(d => d.Employee.Branch.CompanyId == f.CompanyId.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(d => d.Name.Contains(s) || d.Employee.FullName.Contains(s) || d.Employee.EmployeeNo.Contains(s));
        }

        q = (filterKey ?? "").ToLowerInvariant() switch
        {
            "spouse" => q.Where(d => d.Relation == DependentRelation.Spouse),
            "children" => q.Where(d => d.Relation == DependentRelation.Son || d.Relation == DependentRelation.Daughter),
            "relative" => q.Where(d => d.Relation == DependentRelation.Relative),
            "emergency" => q.Where(d => d.IsEmergencyContact),
            "dependents" => q.Where(d => d.IsDependent),
            _ => q
        };

        var rows = await q
            .OrderBy(d => d.Employee.FullName).ThenBy(d => d.Name)
            .Select(d => new
            {
                No = d.Employee.EmployeeNo, Employee = d.Employee.FullName,
                d.Name, d.Relation, d.BirthDate, d.Nationality, d.NationalId,
                d.IsDependent, d.IsEmergencyContact, d.MobilePhone, d.MaritalStatus
            })
            .ToListAsync();

        return rows.Select(r => new Dictionary<string, string>
        {
            ["no"] = r.No,
            ["employee"] = r.Employee,
            ["name"] = r.Name,
            ["relation"] = RelationText(r.Relation),
            ["birthdate"] = D(r.BirthDate),
            ["nationality"] = r.Nationality ?? "",
            ["nationalid"] = r.NationalId ?? "",
            ["isdependent"] = B(r.IsDependent),
            ["isemergency"] = B(r.IsEmergencyContact),
            ["phone"] = r.MobilePhone ?? "",
            ["maritalstatus"] = r.MaritalStatus ?? ""
        }).ToList();
    }

    public static string RecordTypeText(EmployeeRecordType type) => type switch
    {
        EmployeeRecordType.Education => "تعليم",
        EmployeeRecordType.Experience => "خبرة",
        EmployeeRecordType.Certificate => "شهادة",
        EmployeeRecordType.Training => "دورة تدريبية",
        EmployeeRecordType.Medical => "ملف طبي",
        EmployeeRecordType.Asset => "عهدة",
        EmployeeRecordType.Address => "عنوان",
        EmployeeRecordType.EmergencyContact => "جهة اتصال طوارئ",
        EmployeeRecordType.Residency => "إقامة",
        _ => type.ToString()
    };

    private static async Task<List<Dictionary<string, string>>> LoadRecordsAsync(
        ApplicationDbContext db, string? filterKey, ReportFilters f)
    {
        var q = db.EmployeeFileRecords.AsNoTracking().Where(r => !r.Employee.IsDeleted);
        if (f.ActiveOnly) q = q.Where(r => r.Employee.IsActive);
        if (f.CompanyId.HasValue) q = q.Where(r => r.Employee.Branch.CompanyId == f.CompanyId.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(r => r.Title.Contains(s) || r.Employee.FullName.Contains(s) || r.Employee.EmployeeNo.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(filterKey) &&
            Enum.TryParse<EmployeeRecordType>(filterKey, true, out var recordType))
        {
            q = q.Where(r => r.RecordType == recordType);
        }

        var rows = await q
            .OrderBy(r => r.Employee.FullName).ThenByDescending(r => r.ToDate)
            .Select(r => new
            {
                No = r.Employee.EmployeeNo, Employee = r.Employee.FullName,
                r.RecordType, r.Title, r.Subtitle, r.Country, r.RefNo,
                r.FromDate, r.ToDate, r.Amount, r.IsCurrent, r.AttachmentName
            })
            .ToListAsync();

        return rows.Select(r => new Dictionary<string, string>
        {
            ["no"] = r.No,
            ["employee"] = r.Employee,
            ["type"] = RecordTypeText(r.RecordType),
            ["title"] = r.Title,
            ["subtitle"] = r.Subtitle ?? "",
            ["country"] = r.Country ?? "",
            ["refno"] = r.RefNo ?? "",
            ["fromdate"] = D(r.FromDate),
            ["todate"] = D(r.ToDate),
            ["amount"] = r.Amount?.ToString("0.##") ?? "",
            ["iscurrent"] = B(r.IsCurrent),
            ["attachment"] = r.AttachmentName ?? ""
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> LoadDocumentsAsync(
        ApplicationDbContext db, ReportFilters f)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT e.EmployeeNo, e.FullName, d.DocumentType, d.FileName, d.ExpiryDate, d.UploadedAt, d.UploadedBy
FROM EmployeeDocuments d
INNER JOIN Employees e ON e.Id = d.EmployeeId
WHERE e.IsDeleted = 0
ORDER BY e.FullName, d.UploadedAt DESC;
""",
            command => { },
            reader => new Dictionary<string, string>
            {
                ["no"] = HrmsDatabase.GetString(reader, "EmployeeNo"),
                ["employee"] = HrmsDatabase.GetString(reader, "FullName"),
                ["doctype"] = HrmsDatabase.GetString(reader, "DocumentType"),
                ["filename"] = HrmsDatabase.GetString(reader, "FileName"),
                ["expirydate"] = D(HrmsDatabase.GetDateOnly(reader, "ExpiryDate")),
                ["uploadedat"] = D(HrmsDatabase.GetDateTime(reader, "UploadedAt")),
                ["uploadedby"] = HrmsDatabase.GetString(reader, "UploadedBy")
            });

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            rows = rows.Where(r =>
                r["employee"].Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r["no"].Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rows;
    }

    private static async Task<List<Dictionary<string, string>>> LoadLeavesAsync(
        ApplicationDbContext db, ReportFilters f)
    {
        var q = db.LeaveRequests.AsNoTracking().Where(l => !l.Employee.IsDeleted);
        if (f.ActiveOnly) q = q.Where(l => l.Employee.IsActive);
        if (f.CompanyId.HasValue) q = q.Where(l => l.Employee.Branch.CompanyId == f.CompanyId.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(l => l.Employee.FullName.Contains(s) || l.Employee.EmployeeNo.Contains(s));
        }

        var rows = await q
            .OrderByDescending(l => l.FromDate)
            .Select(l => new
            {
                No = l.Employee.EmployeeNo, Employee = l.Employee.FullName,
                l.LeaveType, l.Status, l.FromDate, l.ToDate, l.Reason
            })
            .ToListAsync();

        return rows.Select(r => new Dictionary<string, string>
        {
            ["no"] = r.No,
            ["employee"] = r.Employee,
            ["type"] = r.LeaveType switch
            {
                LeaveType.Annual => "سنوية",
                LeaveType.Sick => "مرضية",
                LeaveType.Unpaid => "بدون راتب",
                LeaveType.Emergency => "طارئة",
                LeaveType.Official => "رسمية",
                _ => r.LeaveType.ToString()
            },
            ["status"] = r.Status switch
            {
                LeaveStatus.Pending => "معلقة",
                LeaveStatus.Approved => "موافَق عليها",
                LeaveStatus.Rejected => "مرفوضة",
                _ => r.Status.ToString()
            },
            ["fromdate"] = D(r.FromDate),
            ["todate"] = D(r.ToDate),
            ["days"] = (r.ToDate.DayNumber - r.FromDate.DayNumber + 1).ToString(),
            ["reason"] = r.Reason ?? ""
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> LoadViolationsAsync(
        ApplicationDbContext db, ReportFilters f)
    {
        var q = db.EmployeeViolationCases.AsNoTracking().Where(v => !v.Employee.IsDeleted);
        if (f.ActiveOnly) q = q.Where(v => v.Employee.IsActive);
        if (f.CompanyId.HasValue) q = q.Where(v => v.Employee.Branch.CompanyId == f.CompanyId.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(v => v.Employee.FullName.Contains(s) || v.Employee.EmployeeNo.Contains(s) || v.ReferenceNo.Contains(s));
        }

        var rows = await q
            .OrderByDescending(v => v.EventDate)
            .Select(v => new
            {
                v.ReferenceNo, No = v.Employee.EmployeeNo, Employee = v.Employee.FullName,
                v.ViolationCategory, v.ViolationTitle, v.EventDate, v.Status, v.FinalAction, v.ProposedAction
            })
            .ToListAsync();

        return rows.Select(r => new Dictionary<string, string>
        {
            ["refno"] = r.ReferenceNo,
            ["no"] = r.No,
            ["employee"] = r.Employee,
            ["category"] = r.ViolationCategory,
            ["title"] = r.ViolationTitle,
            ["eventdate"] = D(r.EventDate),
            ["status"] = r.Status,
            ["action"] = string.IsNullOrWhiteSpace(r.FinalAction) ? (r.ProposedAction ?? "") : r.FinalAction
        }).ToList();
    }
}
