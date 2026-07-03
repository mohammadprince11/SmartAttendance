using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

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
    public int PageSize { get; set; } = 25;

    public List<AttendanceRecordRow> Records { get; set; } = new();

    public List<string> Branches { get; set; } = new();

    public List<string> Departments { get; set; } = new();

    public List<string> Positions { get; set; } = new();

    public int TotalRows { get; set; }

    public int TotalPages { get; set; }

    public int StartRow { get; set; }

    public int EndRow { get; set; }

    public bool HasPositionField { get; set; }

    public async Task OnGetAsync()
    {
        NormalizePaging();

        HasPositionField = _dbContext.Model
            .FindEntityType(typeof(Employee))
            ?.FindProperty("Position") != null;

        await LoadFilterListsAsync();
        await LoadRecordsAsync();
    }

    private void NormalizePaging()
    {
        PageSize = PageSize switch
        {
            10 => 10,
            25 => 25,
            50 => 50,
            100 => 100,
            _ => 25
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
        var query = _dbContext.AttendanceRecords
            .AsNoTracking()
            .Include(x => x.Employee)
            .ThenInclude(x => x.Department)
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
            query = query.Where(x => x.Employee.Department.Branch.Name == Branch);
        }

        if (!string.IsNullOrWhiteSpace(Department))
        {
            query = query.Where(x => x.Employee.Department.Name == Department);
        }

        if (!string.IsNullOrWhiteSpace(Position) && HasPositionField)
        {
            var value = Position.Trim();
            query = query.Where(x => EF.Property<string?>(x.Employee, "Position") == value);
        }

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
                x.Employee.Department.Name.Contains(value) ||
                x.Employee.Department.Branch.Name.Contains(value) ||
                (x.Device != null && x.Device.Name.Contains(value)) ||
                (x.Notes != null && x.Notes.Contains(value)));
        }

        query = query
            .OrderByDescending(x => x.AttendanceDate)
            .ThenByDescending(x => x.CheckIn)
            .ThenBy(x => x.Employee.EmployeeNo);

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
            Branch = record.Employee?.Department?.Branch?.Name ?? string.Empty,
            Department = record.Employee?.Department?.Name ?? string.Empty,
            Position = HasPositionField ? ReadStringProperty(record.Employee, "Position") : string.Empty,
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
