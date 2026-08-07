using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceRecords;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Branch { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Position { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 30;

    public List<AttendanceRecordRow> Records { get; set; } = new();

    public List<string> Branches { get; set; } = new();

    public List<string> Departments { get; set; } = new();

    public List<string> Positions { get; set; } = new();

    public int TotalRows { get; set; }

    public int TotalPages { get; set; }

    public int StartRow { get; set; }

    public int EndRow { get; set; }

    public bool HasPositionField { get; set; }

    /// <summary>
    /// هل طلب المستخدم مدىً زمنياً صريحاً (ولو طرفاً واحداً)؟ يميّز «فتح الصفحة بلا
    /// فلتر» عن «توسيع المدى عمداً»، فلا يُفرَض الافتراضي على من وسّعه بنفسه.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public bool AllDates { get; set; }

    /// <summary>فترة الغلق المطبَّقة افتراضياً (للعرض بالواجهة) — فارغ إن اختار المستخدم مداه.</summary>
    public string? DefaultPeriodNote { get; private set; }

    public async Task OnGetAsync()
    {
        NormalizePaging();

        HasPositionField = _dbContext.Model
            .FindEntityType(typeof(Employee))
            ?.FindProperty("Position") != null;

        await ApplyDefaultDateWindowAsync();
        await LoadFilterListsAsync();
        await LoadRecordsAsync();
    }

    /// <summary>
    /// **حارس أداء**: بلا مدىً زمنيّ كانت الصفحة تعدّ وتفرز جدول الحضور كلّه
    /// (مئات الآلاف من السجلات) لتعرض 25 صفاً — قيس 7.8 ثانية بالخادم مقابل 0.35
    /// ثانية بمدى خمسة أيام (٢٢ ضعفاً).
    ///
    /// <para>فالفتح الأول يقتصر على **فترة غلق الحضور الجارية** — نفس الحدّ الذي
    /// تتبعه بقية شاشات الحضور والمسير، فالافتراضي مفهوم لا اعتباطيّ. ويبقى
    /// بيد المستخدم توسيعه (بتحديد تواريخه) أو رفعه كلياً بـ<c>AllDates</c>.</para>
    /// </summary>
    private async Task ApplyDefaultDateWindowAsync()
    {
        if (AllDates || FromDate.HasValue || ToDate.HasValue) return;

        var today = DateTime.Today;
        var (period, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(
            _dbContext, today.Year, today.Month);

        FromDate = period.From;
        ToDate = period.To;
        DefaultPeriodNote = $"{period.From:yyyy-MM-dd} → {period.To:yyyy-MM-dd}";
    }

    private void NormalizePaging()
    {
        // 25 استُبدلت بـ30 (طلب محمد) — والقيمة غير المعروفة تعود للافتراضي 30،
        // فرابطٌ قديم فيه PageSize=25 لا يفشل بل يُطبَّع.
        PageSize = PageSize switch
        {
            10 => 10,
            30 => 30,
            50 => 50,
            100 => 100,
            _ => 30
        };

        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value > ToDate.Value)
        {
            (FromDate, ToDate) = (ToDate, FromDate);
        }
    }

    private async Task LoadFilterListsAsync()
    {
        Branches = await _dbContext.Branches
            .AsNoTracking()
            .Select(x => x.Name)
            .Where(x => x != "")
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        Departments = await _dbContext.Departments
            .AsNoTracking()
            .Select(x => x.Name)
            .Where(x => x != "")
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        if (HasPositionField)
        {
            Positions = await _dbContext.Employees
                .AsNoTracking()
                .Select(x => EF.Property<string?>(x, "Position"))
                .Where(x => x != null && x != "")
                .Distinct()
                .OrderBy(x => x)
                .Select(x => x!)
                .ToListAsync();
        }
    }

    private async Task LoadRecordsAsync()
    {
        // 🔧 الفرع يُقرأ من **الموظف مباشرةً** (`Employee.Branch`) لا عبر
        // `Employee.Department.Branch`: القسم قد لا يحمل فرعاً فكان العمود يظهر فارغاً
        // دائماً، بينما `Employee.BranchId` مضبوط. (رُصد حيّاً 2026-08-07.)
        var query = _dbContext.AttendanceRecords
            .AsNoTracking()
            .Include(x => x.Employee)
            .ThenInclude(x => x.Branch)
            .Include(x => x.Device)
            .AsQueryable();

        if (FromDate.HasValue)
        {
            query = query.Where(x => x.AttendanceDate >= FromDate.Value);
        }

        if (ToDate.HasValue)
        {
            query = query.Where(x => x.AttendanceDate <= ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(EmployeeNo))
        {
            var value = EmployeeNo.Trim();
            query = query.Where(x => x.Employee.EmployeeNo.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(EmployeeName))
        {
            var value = EmployeeName.Trim();
            query = query.Where(x => x.Employee.FullName.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(Branch))
        {
            query = query.Where(x => x.Employee.Branch.Name == Branch);
        }

        // القسم والمنصب حُذفا من العرض والفلترة (طلب محمد) — الفرع يكفي للتصنيف هنا.

        if (!string.IsNullOrWhiteSpace(Status))
        {
            query = query.Where(x => x.Status.ToString() == Status);
        }

        if (!string.IsNullOrWhiteSpace(Source))
        {
            query = query.Where(x => x.Source.ToString() == Source);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var value = Search.Trim();

            query = query.Where(x =>
                x.Employee.EmployeeNo.Contains(value) ||
                x.Employee.FullName.Contains(value) ||
                x.Employee.Branch.Name.Contains(value) ||
                (x.Device != null && x.Device.Name.Contains(value)) ||
                (x.Notes != null && x.Notes.Contains(value)));
        }

        // فاصل التعادل كان `x.Employee.EmployeeNo` — عمودٌ **بجدول آخر**. وجوده
        // بالـORDER BY يفرض ضمّ جدول الموظفين **قبل** الفرز والاقتطاع، فيُبنى الصفّ
        // المضموم لكل سجلٍّ بالفترة (63 ألفاً بالقياس) ثم يُرمى منه كلُّ شيء عدا
        // خمسين صفاً. وبنفس السبب يمتنع الفهرس عن خدمة الترتيب: أعمدة الفرز ليست
        // كلها بجدول واحد.
        //
        // `x.EmployeeId` مفتاحٌ أجنبيّ **على الجدول نفسه** — يعطي حتميّةً مكافئة
        // (وهي المطلوب من فاصل التعادل: ألّا يتنقّل صفٌّ بين الصفحات) ويُخدَم من
        // الفهرس `IX_AttendanceRecords_AttendanceDate_CheckIn_EmployeeId` بلا ضمّ.
        //
        // الأثر المرئيّ الوحيد: عند تساوي التاريخ **ووقت الدخول بالثانية**، الترتيب
        // بين المتساوين يتبع معرّف الموظف لا رقمه الوظيفي.
        query = query
            .OrderByDescending(x => x.AttendanceDate)
            .ThenByDescending(x => x.CheckIn)
            .ThenBy(x => x.EmployeeId);

        TotalRows = await query.CountAsync();

        TotalPages = TotalRows <= 0
            ? 1
            : (int)Math.Ceiling(TotalRows / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var rows = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        Records = rows.Select(ToRow).ToList();

        StartRow = TotalRows == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
        EndRow = TotalRows == 0 ? 0 : StartRow + Records.Count - 1;
    }

    private AttendanceRecordRow ToRow(AttendanceRecord record)
    {
        return new AttendanceRecordRow
        {
            Id = record.Id,
            EmployeeNo = record.Employee?.EmployeeNo ?? string.Empty,
            EmployeeName = record.Employee?.FullName ?? string.Empty,
            Branch = record.Employee?.Branch?.Name ?? string.Empty,
            Date = record.AttendanceDate.ToString("yyyy-MM-dd"),
            CheckIn = record.CheckIn.ToString("HH:mm"),
            CheckOut = record.CheckOut?.ToString("HH:mm") ?? "-",
            Status = record.Status.ToString(),
            Source = record.Source.ToString(),
            Device = record.Device?.Name ?? "-",
            Notes = record.Notes ?? string.Empty
        };
    }

    private static string ReadStringProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return string.Empty;
        }

        var property = source.GetType().GetProperty(propertyName);

        if (property == null)
        {
            return string.Empty;
        }

        return property.GetValue(source)?.ToString() ?? string.Empty;
    }

    public class AttendanceRecordRow
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        public string Branch { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string Date { get; set; } = string.Empty;

        public string CheckIn { get; set; } = string.Empty;

        public string CheckOut { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Device { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
    }
}
