namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// Actionable requirements shown on the employee 360 profile.
/// Every item is derived from live employee data and disappears automatically
/// once its underlying condition is resolved.
/// </summary>
public partial class ProfileModel
{
    public sealed class EmployeeRequirementItem
    {
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Severity { get; init; } = "warning";
        public int Count { get; init; } = 1;
        public string ActionKind { get; init; } = "tab";
        public string ActionLabel { get; init; } = "مراجعة";
        public string Target { get; init; } = string.Empty;
        public string? DocumentType { get; init; }

        public string SeverityLabel => Severity switch
        {
            "critical" => "حرج",
            "info" => "تنبيه",
            _ => "متابعة"
        };
    }

    public List<EmployeeRequirementItem> EmployeeRequirements { get; private set; } = new();

    public int EmployeeRequirementAffectedItems =>
        EmployeeRequirements.Sum(item => Math.Max(1, item.Count));

    private void BuildEmployeeRequirements()
    {
        EmployeeRequirements.Clear();
        if (Employee is null)
        {
            return;
        }

        bool HasDocument(params string[] aliases) =>
            DocumentRows.Any(document => aliases.Any(alias =>
                string.Equals(document.DocumentType, alias, StringComparison.OrdinalIgnoreCase) ||
                (document.DocumentType ?? string.Empty).Contains(alias, StringComparison.OrdinalIgnoreCase)));

        void Add(
            string code,
            string title,
            string description,
            string severity,
            int count,
            string actionKind,
            string actionLabel,
            string target,
            string? documentType = null)
        {
            EmployeeRequirements.Add(new EmployeeRequirementItem
            {
                Code = code,
                Title = title,
                Description = description,
                Severity = severity,
                Count = Math.Max(1, count),
                ActionKind = actionKind,
                ActionLabel = actionLabel,
                Target = target,
                DocumentType = documentType
            });
        }

        var employeeGeo = $"{Employee.Nationality} {Employee.Country}".ToLowerInvariant();
        var isExpat =
            !string.IsNullOrWhiteSpace(employeeGeo) &&
            !(employeeGeo.Contains("iraq") ||
              employeeGeo.Contains("iraqi") ||
              employeeGeo.Contains("العراق") ||
              employeeGeo.Contains("عراقي") ||
              employeeGeo.Contains("عراقية"));

        var canManageDocuments = CanOpenEmployeeDocuments;
        var documentActionKind = canManageDocuments ? "document-upload" : "tab";
        var documentTarget = canManageDocuments ? "employee-documents" : "documents";

        if (!HasDocument("ID", "هوية", "بطاقة"))
        {
            Add(
                "IDENTITY_MISSING",
                "هوية / بطاقة وطنية مفقودة",
                "هذا المستند مطلوب لاكتمال ملف الموظف.",
                "critical", 1, documentActionKind,
                canManageDocuments ? "رفع الهوية" : "عرض المستندات",
                documentTarget, "ID");
        }

        var hasStructuredContract = Contracts.Count > 0;
        var hasContractDocument = HasDocument("Contract", "عقد");

        if (!hasStructuredContract)
        {
            Add(
                "CONTRACT_MISSING",
                "عقد العمل غير مسجل",
                "لا يوجد سجل عقد وظيفي لهذا الموظف.",
                "critical", 1,
                CanEditEmployee ? "contract-add" : "tab",
                CanEditEmployee ? "إضافة عقد" : "عرض العقود",
                "profile-files");
        }
        else if (!hasContractDocument)
        {
            Add(
                "CONTRACT_ATTACHMENT_MISSING",
                "نسخة العقد الموقعة مفقودة",
                "العقد مسجل بالنظام لكن لا توجد نسخة عقد ضمن المستندات الرسمية.",
                "warning", 1, documentActionKind,
                canManageDocuments ? "رفع نسخة العقد" : "عرض المستندات",
                documentTarget, "Contract");
        }

        if (isExpat && !HasDocument("Passport", "جواز"))
        {
            Add(
                "PASSPORT_MISSING",
                "جواز السفر مفقود",
                "جواز السفر مطلوب للموظفين الوافدين.",
                "critical", 1, documentActionKind,
                canManageDocuments ? "رفع الجواز" : "عرض المستندات",
                documentTarget, "Passport");
        }

        if (isExpat && !HasDocument("Visa", "إقامة", "اقامة"))
        {
            Add(
                "VISA_MISSING",
                "الإقامة / الفيزا مفقودة",
                "الإقامة أو الفيزا مطلوبة للموظفين الوافدين.",
                "critical", 1, documentActionKind,
                canManageDocuments ? "رفع الإقامة" : "عرض المستندات",
                documentTarget, "Visa");
        }

        if (AttendanceExceptionsCount > 0)
        {
            Add(
                "ATTENDANCE_EXCEPTIONS",
                "استثناءات الحضور",
                $"يوجد {AttendanceExceptionsCount} حالة حضور تحتاج تدقيق.",
                "warning", AttendanceExceptionsCount,
                "tab", "مراجعة الحضور", "attendance");
        }

        if (PendingRequests > 0)
        {
            Add(
                "PENDING_REQUESTS",
                "طلبات معلقة",
                $"يوجد {PendingRequests} طلب بانتظار الإجراء.",
                "warning", PendingRequests,
                "tab", "فتح الطلبات", "requests");
        }

        if (ExpiredDocumentsCount > 0)
        {
            Add(
                "EXPIRED_DOCUMENTS",
                "مستندات منتهية",
                $"يوجد {ExpiredDocumentsCount} مستند منتهي يحتاج تحديث.",
                "critical", ExpiredDocumentsCount,
                canManageDocuments ? "documents-center" : "tab",
                canManageDocuments ? "تحديث المستندات" : "عرض المستندات",
                canManageDocuments ? "employee-documents" : "documents");
        }

        if (ExpiringDocumentsCount > 0)
        {
            Add(
                "EXPIRING_DOCUMENTS",
                "مستندات قرب الانتهاء",
                $"يوجد {ExpiringDocumentsCount} مستند سينتهي خلال 30 يوماً.",
                "warning", ExpiringDocumentsCount,
                canManageDocuments ? "documents-center" : "tab",
                canManageDocuments ? "مراجعة المستندات" : "عرض المستندات",
                canManageDocuments ? "employee-documents" : "documents");
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (Employee.ContractEndDate.HasValue &&
            Employee.ContractEndDate.Value <= today.AddDays(30))
        {
            var expired = Employee.ContractEndDate.Value < today;
            Add(
                expired ? "CONTRACT_EXPIRED" : "CONTRACT_REVIEW",
                expired ? "العقد منتهي" : "العقد يحتاج متابعة",
                expired
                    ? $"انتهى العقد بتاريخ {Employee.ContractEndDate:yyyy-MM-dd}."
                    : $"نهاية العقد: {Employee.ContractEndDate:yyyy-MM-dd}.",
                expired ? "critical" : "warning", 1,
                CanEditEmployee ? "contract-review" : "tab",
                CanEditEmployee ? "تحديث العقد" : "عرض العقود",
                "profile-files");
        }

        static int SeverityOrder(string severity) => severity switch
        {
            "critical" => 0,
            "warning" => 1,
            _ => 2
        };

        EmployeeRequirements = EmployeeRequirements
            .OrderBy(item => SeverityOrder(item.Severity))
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToList();
    }
}
