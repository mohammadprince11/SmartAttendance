using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// أسعار صرف مؤرّخة ومعزولة بالشركة. لا يُستعمل «آخر سعر اليوم» لمسيرٍ تاريخي؛
/// الحسم دائماً بأحدث سعر فعّال في تاريخ الفترة أو قبله، مع دعم الاتجاه العكسي.
/// </summary>
public static class CurrencyExchangeRateStore
{
    public sealed class RateRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string FromCurrency { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public DateOnly EffectiveDate { get; set; }
        public decimal Rate { get; set; }
        public string? Note { get; set; }
        public bool IsActive { get; set; } = true;
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed record ResolvedRate(
        string SourceCurrency, string TargetCurrency, decimal Rate, DateOnly? EffectiveDate, bool IsInverse)
    {
        public decimal Convert(decimal amount) =>
            Math.Round(amount * Rate, 2, MidpointRounding.AwayFromZero);
    }

    public sealed record PayrollContext(
        bool Ok,
        string Message,
        string PayrollCurrency,
        IReadOnlyDictionary<int, ResolvedRate> ByEmployee);

    public static string? NormalizeCurrency(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is { Length: 3 } && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized : null;
    }

    public static ResolvedRate? Resolve(
        IEnumerable<RateRow> rows, string? fromCurrency, string? toCurrency, DateOnly asOf)
    {
        var from = NormalizeCurrency(fromCurrency);
        var to = NormalizeCurrency(toCurrency);
        if (from is null || to is null) return null;
        if (from == to) return new ResolvedRate(from, to, 1m, null, false);

        var eligible = rows.Where(row => row.IsActive && row.Rate > 0 && row.EffectiveDate <= asOf);
        var direct = eligible
            .Where(row => NormalizeCurrency(row.FromCurrency) == from && NormalizeCurrency(row.ToCurrency) == to)
            .OrderByDescending(row => row.EffectiveDate).ThenByDescending(row => row.Id)
            .FirstOrDefault();
        if (direct is not null)
            return new ResolvedRate(from, to, direct.Rate, direct.EffectiveDate, false);

        var inverse = eligible
            .Where(row => NormalizeCurrency(row.FromCurrency) == to && NormalizeCurrency(row.ToCurrency) == from)
            .OrderByDescending(row => row.EffectiveDate).ThenByDescending(row => row.Id)
            .FirstOrDefault();
        return inverse is null
            ? null
            : new ResolvedRate(from, to, decimal.Divide(1m, inverse.Rate), inverse.EffectiveDate, true);
    }

    public static async Task<List<RateRow>> ListAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, bool activeOnly = false)
    {
        if (companyId <= 0 || !scope.Allows(companyId)) return new List<RateRow>();
        return await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id,CompanyId,FromCurrency,ToCurrency,EffectiveDate,Rate,Note,IsActive,
       CreatedBy,CreatedAt,UpdatedBy,UpdatedAt
FROM CurrencyExchangeRates
WHERE CompanyId=@CompanyId AND (@ActiveOnly=0 OR IsActive=1)
ORDER BY EffectiveDate DESC,FromCurrency,ToCurrency,Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@ActiveOnly", activeOnly ? 1 : 0);
            },
            Map);
    }

    public static async Task<(bool Ok, string Message, int Id)> SaveAsync(
        ApplicationDbContext db, CompanyScope scope, RateRow input, string? actor, string? ipAddress)
    {
        if (input.CompanyId <= 0 || !scope.Allows(input.CompanyId))
            return (false, "الشركة غير متاحة ضمن صلاحيتك.", 0);
        var from = NormalizeCurrency(input.FromCurrency);
        var to = NormalizeCurrency(input.ToCurrency);
        if (from is null || to is null) return (false, "رمز العملة يجب أن يكون ثلاثة أحرف ISO.", 0);
        if (from == to) return (false, "عملة المصدر والهدف يجب أن تكونا مختلفتين.", 0);
        if (input.Rate <= 0) return (false, "سعر الصرف يجب أن يكون أكبر من صفر.", 0);
        if (input.EffectiveDate == default) return (false, "تاريخ السريان إلزامي.", 0);

        if (input.Id > 0)
        {
            var changed = await HrmsDatabase.ScalarAsync<int>(db, """
UPDATE CurrencyExchangeRates
SET FromCurrency=@From,ToCurrency=@To,EffectiveDate=@Date,Rate=@Rate,Note=@Note,
    IsActive=@Active,UpdatedBy=@Actor,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId;
SELECT @@ROWCOUNT;
""", command => Bind(command, input, from, to, actor));
            if (changed != 1) return (false, "السعر غير موجود أو خارج نطاق صلاحيتك.", 0);
        }
        else
        {
            input.Id = await HrmsDatabase.ScalarAsync<int>(db, """
INSERT INTO CurrencyExchangeRates
 (CompanyId,FromCurrency,ToCurrency,EffectiveDate,Rate,Note,IsActive,CreatedBy)
OUTPUT INSERTED.Id
VALUES(@CompanyId,@From,@To,@Date,@Rate,@Note,@Active,@Actor);
""", command => Bind(command, input, from, to, actor));
        }

        await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO AuditLogs(EntityName,EntityId,Action,NewValues,UserName,IpAddress)
VALUES(N'CurrencyExchangeRate',CONVERT(nvarchar(30),@Id),N'Save Payroll Exchange Rate',@Values,@Actor,@Ip);
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", input.Id);
            HrmsDatabase.AddParameter(command, "@Values", HrmsDatabase.JsonLine(
                ("CompanyId", input.CompanyId.ToString()), ("From", from), ("To", to),
                ("EffectiveDate", input.EffectiveDate.ToString("yyyy-MM-dd")), ("Rate", input.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            HrmsDatabase.AddParameter(command, "@Actor", actor);
            HrmsDatabase.AddParameter(command, "@Ip", (object?)ipAddress ?? DBNull.Value);
        });
        return (true, "حُفظ سعر الصرف وأصبح متاحاً للفترات الواقعة في أو بعد تاريخ سريانه.", input.Id);
    }

    public static async Task<bool> DeactivateAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int id, string? actor)
    {
        if (companyId <= 0 || !scope.Allows(companyId)) return false;
        var changed = await HrmsDatabase.ScalarAsync<int>(db, $"""
DECLARE @Changed int;
UPDATE CurrencyExchangeRates SET IsActive=0,UpdatedBy=@Actor,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId AND IsActive=1 AND {scope.ToSqlPredicate("CompanyId")};
SET @Changed=@@ROWCOUNT;
IF @Changed=1
    INSERT INTO AuditLogs(EntityName,EntityId,Action,NewValues,UserName)
    VALUES(N'CurrencyExchangeRate',CONVERT(nvarchar(30),@Id),N'Deactivate Payroll Exchange Rate',
           CONCAT(NCHAR(123),N'"CompanyId":',@CompanyId,N',"IsActive":false',NCHAR(125)),@Actor);
SELECT @Changed;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@Id", id);
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Actor", actor);
        });
        return changed == 1;
    }

    public static async Task<PayrollContext> BuildPayrollContextAsync(
        ApplicationDbContext db, int companyId, int runId, DateOnly asOf)
    {
        if (companyId <= 0)
            return new PayrollContext(true, string.Empty, string.Empty, new Dictionary<int, ResolvedRate>());

        var payrollCurrency = NormalizeCurrency(await HrmsDatabase.ScalarAsync<string?>(
            db, "SELECT CurrencyCode FROM Companies WHERE Id=@Id AND IsDeleted=0;",
            command => HrmsDatabase.AddParameter(command, "@Id", companyId)));
        if (payrollCurrency is null)
            return new PayrollContext(false, "تعذّر الاحتساب: حدّد عملة الشركة الأساسية من إعداد الشركة أولاً.", string.Empty, new Dictionary<int, ResolvedRate>());

        var rows = await ListAsync(db, CompanyScope.ForCompanies(new[] { companyId }), companyId, activeOnly: true);
        var employees = await HrmsDatabase.QueryAsync(db, """
SELECT f.EmployeeId,ISNULL(NULLIF(LTRIM(RTRIM(f.Currency)),N''),@PayrollCurrency) Currency
FROM EmployeeFinancialInfos f
INNER JOIN Employees e ON e.Id=f.EmployeeId AND e.CompanyId=@CompanyId
WHERE ISNULL(f.IsDeleted,0)=0 AND ISNULL(e.IsDeleted,0)=0 AND ISNULL(e.IsActive,1)=1
 AND ISNULL(f.StopSalaryCalc,0)=0
 AND (ISNULL(f.BasicSalary,0)<>0 OR ISNULL(f.SocialSecuritySalary,0)<>0 OR ISNULL(f.CurrentTaxSalary,0)<>0
      OR EXISTS(SELECT 1 FROM EmployeeAllowances a WHERE a.EmployeeId=f.EmployeeId
         AND ISNULL(a.IsDeleted,0)=0 AND ISNULL(a.Amount,0)<>0 AND a.FromDate<=@AsOf
         AND (ISNULL(a.EndAfterDate,0)=0 OR a.ToDate IS NULL OR a.ToDate>=@AsOf)))
 AND (NOT EXISTS(SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId)
      OR EXISTS(SELECT 1 FROM PayrollRunScopeMembers s WHERE s.RunId=@RunId AND s.EmployeeId=f.EmployeeId));
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@RunId", runId);
            HrmsDatabase.AddParameter(command, "@PayrollCurrency", payrollCurrency);
            HrmsDatabase.AddParameter(command, "@AsOf", asOf.ToDateTime(TimeOnly.MinValue));
        }, reader => (EmployeeId: HrmsDatabase.GetInt(reader, "EmployeeId"), Currency: HrmsDatabase.GetString(reader, "Currency")));

        var result = new Dictionary<int, ResolvedRate>();
        foreach (var employee in employees)
        {
            var source = NormalizeCurrency(employee.Currency);
            if (source is null)
                return new PayrollContext(false, $"تعذّر الاحتساب: عملة الملف المالي للموظف رقم {employee.EmployeeId} غير صالحة.", payrollCurrency, result);
            var resolved = Resolve(rows, source, payrollCurrency, asOf);
            if (resolved is null)
                return new PayrollContext(false,
                    $"تعذّر الاحتساب: لا يوجد سعر صرف فعّال من {source} إلى {payrollCurrency} بتاريخ {asOf:yyyy-MM-dd} للموظف رقم {employee.EmployeeId}.",
                    payrollCurrency, result);
            result[employee.EmployeeId] = resolved;
        }
        return new PayrollContext(true, string.Empty, payrollCurrency, result);
    }

    private static RateRow Map(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"), CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
        FromCurrency = HrmsDatabase.GetString(reader, "FromCurrency"), ToCurrency = HrmsDatabase.GetString(reader, "ToCurrency"),
        EffectiveDate = HrmsDatabase.GetDateOnly(reader, "EffectiveDate") ?? default,
        Rate = reader["Rate"] is decimal rate ? rate : 0,
        Note = HrmsDatabase.GetString(reader, "Note"), IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy"), CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.MinValue,
        UpdatedBy = HrmsDatabase.GetString(reader, "UpdatedBy"), UpdatedAt = HrmsDatabase.GetDateTime(reader, "UpdatedAt")
    };

    private static void Bind(System.Data.Common.DbCommand command, RateRow input, string from, string to, string? actor)
    {
        if (input.Id > 0) HrmsDatabase.AddParameter(command, "@Id", input.Id);
        HrmsDatabase.AddParameter(command, "@CompanyId", input.CompanyId);
        HrmsDatabase.AddParameter(command, "@From", from); HrmsDatabase.AddParameter(command, "@To", to);
        HrmsDatabase.AddParameter(command, "@Date", input.EffectiveDate.ToDateTime(TimeOnly.MinValue));
        HrmsDatabase.AddParameter(command, "@Rate", input.Rate); HrmsDatabase.AddParameter(command, "@Note", input.Note);
        HrmsDatabase.AddParameter(command, "@Active", input.IsActive ? 1 : 0); HrmsDatabase.AddParameter(command, "@Actor", actor);
    }
}
