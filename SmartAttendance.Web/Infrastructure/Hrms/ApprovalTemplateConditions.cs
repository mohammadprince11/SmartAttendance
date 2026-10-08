using System.Globalization;
using System.Text.Json;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Allowlisted, typed request conditions. Missing values never match, including !=.</summary>
public static class ApprovalTemplateConditions
{
    public sealed record Option(string Value, string Label);
    public sealed record Field(string Key, string Label, string Kind, Option[]? Options = null);
    public sealed record Condition(string Field, string Operator, string Value);
    public static readonly string[] Operators = ["eq", "ne", "gt", "ge", "lt", "le"];

    public static IReadOnlyList<Field> Fields(string type) => type switch
    {
        "LeaveRequest" => [new("RequestTypeId", "نوع الإجازة", "select"), new("DaysCount", "عدد أيام الإجازة", "number"), new("FromDate", "تاريخ بدء الإجازة", "date"), new("ToDate", "تاريخ انتهاء الإجازة", "date")],
        "Overtime" => [new("RequestTypeId", "نوع العمل الإضافي", "select"), new("Hours", "عدد ساعات العمل الإضافي", "number"), new("FromDate", "تاريخ العمل الإضافي", "date")],
        "ExitPermission" => [new("RequestTypeId", "نوع المغادرة", "select"), new("Hours", "عدد ساعات المغادرة", "number"), new("FromDate", "تاريخ المغادرة", "date")],
        "Loan" => [new("Amount", "مبلغ القرض / السلفة", "number"), new("InstallmentCount", "عدد الأقساط", "number"), new("FinancialKind", "نوع الطلب المالي", "select", [new("1", "قرض"), new("2", "سلفة")])],
        "SalaryIncrease" => [new("Amount", "قيمة الزيادة المقدمة (مبلغ أو نسبة حسب الطلب)", "number"), new("RaiseType", "طريقة الزيادة", "select", [new("1", "مبلغ"), new("2", "نسبة مئوية")])],
        "FinancialClaim" => [new("Amount", "مبلغ المطالبة", "number"), new("FinancialKind", "نوع المطالبة", "select", [new("3", "بدل مالي"), new("4", "استرداد نفقات")])],
        _ => []
    };

    public static string? Validate(string type, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Length > 16000) return "بيانات الشروط أكبر من الحد المسموح.";
        try
        {
            var conditions = JsonSerializer.Deserialize<List<Condition>>(json);
            if (conditions is null || conditions.Count > 20) return "عدد الشروط غير صحيح (الحد الأقصى 20).";
            foreach (var condition in conditions)
            {
                if (condition is null) return "الشرط غير صحيح.";
                var field = Fields(type).FirstOrDefault(f => f.Key == condition.Field);
                if (field is null) return "حقل الشرط غير متاح لهذا النوع من الطلبات.";
                if (!Operators.Contains(condition.Operator)) return "عملية مقارنة الشرط غير صحيحة.";
                if (field.Kind == "select" && condition.Operator is not ("eq" or "ne")) return "هذا الحقل يدعم يساوي أو لا يساوي فقط.";
                if (!TryValue(field, condition.Value, out _)) return "قيمة الشرط غير صحيحة؛ أدخل رقماً غير سالب أو تاريخاً صحيحاً.";
                if (field.Key == "InstallmentCount" && (!decimal.TryParse(condition.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var count) || count != decimal.Truncate(count)))
                    return "عدد الأقساط يجب أن يكون عدداً صحيحاً.";
            }
            return null;
        }
        catch (JsonException) { return "صيغة الشروط غير صحيحة."; }
    }

    private static bool TryValue(Field field, string? value, out decimal result)
    {
        result = 0;
        if (field.Kind == "select")
            return field.Key == "RequestTypeId"
                ? int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 && (result = id) > 0
                : field.Options!.Any(o => o.Value == value) && decimal.TryParse(value, out result);
        if (field.Kind == "date")
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
            result = date.DayNumber;
            return true;
        }
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result) && result >= 0;
    }

    public static bool Matches(string type, string? json, IReadOnlyDictionary<string, decimal?> values)
    {
        if (Validate(type, json) is not null) return false;
        if (string.IsNullOrWhiteSpace(json)) return true;
        foreach (var condition in JsonSerializer.Deserialize<List<Condition>>(json)!)
        {
            if (!values.TryGetValue(condition.Field, out var actual) || actual is null) return false;
            TryValue(Fields(type).Single(f => f.Key == condition.Field), condition.Value, out var expected);
            var matches = condition.Operator switch
            {
                "eq" => actual == expected, "ne" => actual != expected,
                "gt" => actual > expected, "ge" => actual >= expected,
                "lt" => actual < expected, "le" => actual <= expected, _ => false
            };
            if (!matches) return false;
        }
        return true;
    }

    public static async Task<IReadOnlyDictionary<string, decimal?>> LoadAsync(ApplicationDbContext db, int companyId, string type, int requestId)
    {
        var values = new Dictionary<string, decimal?>();
        // Company ownership is enforced in SQL before reading any employee/request data.
        var requests = await HrmsDatabase.QueryAsync(db, """
SELECT r.RequestTypeId,r.DaysCount,r.FromDate,r.ToDate,r.StartTime,r.EndTime
FROM SelfServiceRequests r INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE r.Id=@Id AND e.CompanyId=@CompanyId AND ISNULL(e.IsDeleted,0)=0;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", requestId);
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
        }, reader => new
        {
            Days = HrmsDatabase.GetNullableDecimal(reader, "DaysCount"),
            TypeId = HrmsDatabase.GetNullableInt(reader, "RequestTypeId"),
            From = HrmsDatabase.GetDateOnly(reader, "FromDate"), To = HrmsDatabase.GetDateOnly(reader, "ToDate"),
            Start = reader["StartTime"] is TimeSpan start ? (TimeSpan?)start : null,
            End = reader["EndTime"] is TimeSpan end ? (TimeSpan?)end : null
        });
        if (requests.Count != 1) return values;
        var request = requests[0];
        values["DaysCount"] = request.Days;
        values["RequestTypeId"] = request.TypeId;
        values["FromDate"] = request.From?.DayNumber;
        values["ToDate"] = request.To?.DayNumber;
        if (type is "Overtime" or "ExitPermission" && request.Start is { } start && request.End is { } end)
        {
            var duration = AttendanceRequestPolicy.Duration(start, end, await AttendanceRequestPolicy.GetCrossMidnightAsync(db));
            values["Hours"] = duration > TimeSpan.Zero ? Math.Round((decimal)duration.TotalHours, 2) : null;
        }
        if (type is "Loan" or "SalaryIncrease" or "FinancialClaim")
        {
            if (await HrmsDatabase.ScalarAsync<int>(db, "SELECT CASE WHEN OBJECT_ID('FinancialRequestDetails','U') IS NULL THEN 0 ELSE 1 END;") == 0)
                return values;
            var details = await HrmsDatabase.QueryAsync(db, """
SELECT f.Kind,f.Amount,f.InstallmentCount,f.RaiseType FROM FinancialRequestDetails f
INNER JOIN SelfServiceRequests r ON r.Id=f.RequestId INNER JOIN Employees e ON e.Id=r.EmployeeId
WHERE f.RequestId=@Id AND e.CompanyId=@CompanyId AND ISNULL(e.IsDeleted,0)=0;
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", requestId);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            }, reader => new { Kind = HrmsDatabase.GetString(reader, "Kind"), Amount = HrmsDatabase.GetNullableDecimal(reader, "Amount"), Count = HrmsDatabase.GetNullableInt(reader, "InstallmentCount"), RaiseType = HrmsDatabase.GetString(reader, "RaiseType") });
            if (details.Count == 1 && FinancialRequestStore.KindOf(details[0].Kind ?? "")?.TemplateKey == type)
            {
                values["Amount"] = details[0].Amount;
                values["InstallmentCount"] = details[0].Count;
                values["FinancialKind"] = details[0].Kind switch { "Loan" => 1, "Advance" => 2, "Allowance" => 3, "Reimbursement" => 4, _ => null };
                values["RaiseType"] = details[0].RaiseType switch { "Amount" => 1, "Percentage" => 2, _ => null };
            }
        }
        return values;
    }
}
