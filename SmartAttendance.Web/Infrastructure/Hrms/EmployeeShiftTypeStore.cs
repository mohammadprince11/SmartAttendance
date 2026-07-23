using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مناوبات العمل الثابتة (نمط كيان — قسم 13 بدراسة الحضور): تعيين موظف ← نوع
/// مناوبة (ShiftType الجديد). صف واحد لكل موظف؛ التعيين الجماعي يستبدل الحالي.
/// محلل اليومية يستخدم تعيين الموظف، ومن بلا تعيين يسقط للمناوبة الافتراضية
/// المختارة بشاشة التحليل (مفهوم «المناوبة الافتراضية» بتهيئة كيان).
/// الجدول القديم EmployeeShifts (المرتبط بـShifts القديم) لا يُمس — الترحيل لاحقاً.
/// </summary>
public static class EmployeeShiftTypeStore
{
    public sealed class AssignmentRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;   // الهيكلية
        public string Branch { get; set; } = string.Empty;       // الموقع
        public int? ShiftTypeId { get; set; }                    // التعيين اليدوي الصريح
        public string? ShiftName { get; set; }
        public string? ShiftColor { get; set; }

        // سمات الموظف — لتقييم معايير الاستحقاق (المناوبة الفعّالة عند غياب التعيين)
        public int DepartmentId { get; set; }
        public int BranchId { get; set; }
        public int? PositionId { get; set; }
        public string? ContractType { get; set; }
        public string? Nationality { get; set; }
        public string? MaritalStatus { get; set; }

        // المناوبة الفعّالة (تُحسب بالصفحة): يدوي ← استحقاق ← افتراضية
        public string EffectiveSource { get; set; } = "Default"; // Manual | Eligibility | Default
        public string? EffectiveShiftName { get; set; }
        public string? EffectiveShiftColor { get; set; }

        /// <summary>قاموس سمات الموظف بمفاتيح معايير الاستحقاق.</summary>
        public IReadOnlyDictionary<string, string?> EligibilityAttrs => new Dictionary<string, string?>
        {
            ["Department"] = DepartmentId.ToString(),
            ["Branch"] = BranchId.ToString(),
            ["Position"] = PositionId?.ToString(),
            ["ContractType"] = ContractType,
            ["Nationality"] = Nationality,
            ["MaritalStatus"] = MaritalStatus,
            ["Employee"] = EmployeeId.ToString()
        };
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('EmployeeShiftTypes', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeShiftTypes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        ShiftTypeId int NOT NULL,
        AssignedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_EmployeeShiftTypes_Employee ON EmployeeShiftTypes (EmployeeId);
END;
""");
    }

    /// <summary>خريطة موظف ← نوع مناوبة (للمحلل).</summary>
    public static async Task<Dictionary<int, int>> MapAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ShiftTypeId FROM EmployeeShiftTypes;",
            command => { },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ShiftTypeId = HrmsDatabase.GetInt(reader, "ShiftTypeId")
            });
        return rows.ToDictionary(r => r.EmployeeId, r => r.ShiftTypeId);
    }

    /// <summary>كل الموظفين النشطين مع تعيينهم الحالي (إن وجد).</summary>
    public static async Task<List<AssignmentRow>> ListAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT e.Id AS EmployeeId, e.EmployeeNo, e.FullName,
       ISNULL(hp.ArabicName, ISNULL(e.Position, N'')) AS PositionName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       e.DepartmentId, e.BranchId, e.PositionId, e.ContractType, e.Nationality, e.MaritalStatus,
       est.ShiftTypeId, s.Name AS ShiftName, s.ColorHex AS ShiftColor
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN HrJobPositions hp ON hp.Id = e.PositionId
LEFT JOIN EmployeeShiftTypes est ON est.EmployeeId = e.Id
LEFT JOIN ShiftTypes s ON s.Id = est.ShiftTypeId
WHERE e.IsActive = 1
ORDER BY e.EmployeeNo;
""",
            command => { },
            reader => new AssignmentRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "PositionName"),
                Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                Branch = HrmsDatabase.GetString(reader, "BranchName"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                PositionId = HrmsDatabase.GetNullableInt(reader, "PositionId"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType") is { Length: > 0 } ct ? ct : null,
                Nationality = HrmsDatabase.GetString(reader, "Nationality") is { Length: > 0 } nat ? nat : null,
                MaritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus") is { Length: > 0 } ms ? ms : null,
                ShiftTypeId = HrmsDatabase.GetNullableInt(reader, "ShiftTypeId"),
                ShiftName = HrmsDatabase.GetString(reader, "ShiftName") is { Length: > 0 } n ? n : null,
                ShiftColor = HrmsDatabase.GetString(reader, "ShiftColor") is { Length: > 0 } c ? c : null
            });
    }

    /// <summary>تعيين جماعي: يستبدل تعيين كل موظف محدد بالمناوبة المختارة.</summary>
    public static async Task<int> AssignAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> employeeIds, int shiftTypeId)
    {
        await EnsureAsync(dbContext);
        if (employeeIds.Count == 0) return 0;

        // دفعات 500 معرف (وسيطان لكل صف بالإدراج + قائمة IN للحذف)
        foreach (var chunk in employeeIds.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"""
DELETE FROM EmployeeShiftTypes WHERE EmployeeId IN ({inList});
INSERT INTO EmployeeShiftTypes (EmployeeId, ShiftTypeId)
SELECT Id, @ShiftTypeId FROM Employees WHERE Id IN ({inList});
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@ShiftTypeId", shiftTypeId);
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                    }
                });
        }
        return employeeIds.Count;
    }

    /// <summary>إلغاء تعيين موظفين (يرجعون للمناوبة الافتراضية).</summary>
    public static async Task UnassignAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<int> employeeIds)
    {
        await EnsureAsync(dbContext);
        foreach (var chunk in employeeIds.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"DELETE FROM EmployeeShiftTypes WHERE EmployeeId IN ({inList});",
                command =>
                {
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                    }
                });
        }
    }
}
