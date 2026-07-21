using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// الخط الزمني الموحّد للموظف (نمط كيان «سجل الموظف»): يجمع أحداث كل المصادر —
/// تعيين/مباشرة، عقود، مخالفات، إنهاء خدمة وإعادة تعيين، حركات التحديث، طلبات
/// الخدمة الذاتية، سجلات الملف، وسجل التدقيق — بترتيب زمني واحد داخل تبويب
/// «الخط الزمني» بملف الموظف. القراءة UNION ALL بدفعة SQL واحدة.
/// </summary>
public partial class ProfileModel
{
    public sealed class TimelineEvent
    {
        public DateTime EventDate { get; set; }
        public string Kind { get; set; } = string.Empty;   // مفتاح النوع للأيقونة واللون
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;

        public string Icon => Kind switch
        {
            "hire" => "🟢",
            "contract" => "📄",
            "violation" => "⚠️",
            "end" => "🔴",
            "rehire" => "🔁",
            "update" => "✏️",
            "request" => "📨",
            "file" => "🗂️",
            _ => "•"
        };
    }

    public List<TimelineEvent> TimelineEvents { get; set; } = new();

    private async Task LoadTimelineAsync()
    {
        // كل مصدر يُسقط أحداثه على الشكل الموحد (تاريخ/نوع/عنوان/تفصيل) — top 60 مجمّعة.
        TimelineEvents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 60 EventDate, Kind, Title, Detail FROM
(
    SELECT CAST(e.HireDate AS datetime2) AS EventDate, 'hire' AS Kind,
           N'تعيين' AS Title, N'انضم الموظف للشركة' AS Detail
    FROM Employees e WHERE e.Id = @Id

    UNION ALL
    SELECT CAST(e.JoiningDate AS datetime2), 'hire', N'مباشرة فعلية', N''
    FROM Employees e WHERE e.Id = @Id AND e.JoiningDate IS NOT NULL AND e.JoiningDate <> e.HireDate

    UNION ALL
    SELECT CAST(c.FromDate AS datetime2), 'contract',
           N'عقد ' + ISNULL(c.ContractType, N''),
           ISNULL(N'رقم ' + c.ContractNo + N' · ', N'') +
           CASE WHEN c.ToDate IS NULL THEN N'غير محدد المدة'
                ELSE N'حتى ' + CONVERT(nvarchar(10), c.ToDate, 120) END
    FROM EmployeeContracts c WHERE c.EmployeeId = @Id AND ISNULL(c.IsDeleted, 0) = 0

    UNION ALL
    SELECT CAST(v.EventDate AS datetime2), 'violation',
           N'مخالفة: ' + ISNULL(v.ViolationTitle, N''),
           v.ReferenceNo + N' · ' + ISNULL(v.Status, N'')
    FROM EmployeeViolationCases v WHERE v.EmployeeId = @Id AND ISNULL(v.IsDeleted, 0) = 0

    UNION ALL
    SELECT CAST(s.LastWorkingDate AS datetime2), 'end',
           N'إنهاء خدمة: ' + ISNULL(s.EndServiceTypeText, s.EndServiceType),
           ISNULL(s.Reason, N'')
    FROM EmployeeEndServices s WHERE s.EmployeeId = @Id

    UNION ALL
    SELECT CAST(e.LastRehireDate AS datetime2), 'rehire', N'إعادة تعيين',
           ISNULL(e.RehireReason, N'')
    FROM Employees e WHERE e.Id = @Id AND e.LastRehireDate IS NOT NULL

    UNION ALL
    SELECT b.RequestedAt, 'update',
           N'حركة تحديث: ' + b.SectionName,
           N'الحالة ' + b.Status + ISNULL(N' · بواسطة ' + b.RequestedBy, N'')
    FROM EmployeeUpdateBatches b WHERE b.EmployeeId = @Id

    UNION ALL
    SELECT r.CreatedAt, 'request',
           N'طلب ' + ISNULL(r.RequestType, N''),
           N'الحالة ' + ISNULL(r.Status, N'')
    FROM SelfServiceRequests r WHERE r.EmployeeId = @Id

    UNION ALL
    SELECT fr.CreatedAt, 'file',
           N'سجل ملف: ' + fr.Title,
           N''
    FROM EmployeeFileRecords fr WHERE fr.EmployeeId = @Id AND ISNULL(fr.IsDeleted, 0) = 0

    UNION ALL
    SELECT a.CreatedAt, 'update',
           N'تدقيق: ' + ISNULL(a.Action, N''),
           ISNULL(N'بواسطة ' + a.UserName, N'')
    FROM AuditLogs a
    WHERE a.EntityName = 'Employee' AND a.EntityId = CAST(@Id AS nvarchar(80))
) AS merged
WHERE EventDate IS NOT NULL
ORDER BY EventDate DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", Id),
            reader => new TimelineEvent
            {
                EventDate = HrmsDatabase.GetDateTime(reader, "EventDate") ?? DateTime.MinValue,
                Kind = HrmsDatabase.GetString(reader, "Kind"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Detail = HrmsDatabase.GetString(reader, "Detail")
            });
    }
}
