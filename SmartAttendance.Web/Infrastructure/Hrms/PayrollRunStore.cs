using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرك المسير (نمط كيان «حساب الرواتب» + ZenHR Salary Batches): دفعة شهرية لها
/// دورة حياة Draft ← Calculated ← Locked ← Issued ← PayslipSent. «الاحتساب» يبني
/// لكل موظف سطراً: الأساسي (من الملف المالي) + العلاوات النشطة، مُنسّباً حسب أيام
/// الحضور من الاعتماد الشهري (EmployeeMonthAttendance)، مطروحاً منه الضريبة
/// التصاعدية والضمان (PayrollConfigStore) وخصومات المخالفات (DeductionAmount +
/// أثر لائحة الجزاءات أيام/ساعات). كل سطر يحمل بنوده التفصيلية للقسيمة.
/// </summary>
public static class PayrollRunStore
{
    public static readonly string[] Lifecycle = { "Draft", "Calculated", "Locked", "Issued", "PayslipSent" };

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "مسودة",
        "Calculated" => "محتسب",
        "Locked" => "مقفل",
        "Issued" => "معتمد للصرف",
        "PayslipSent" => "أُرسلت القسائم",
        _ => status
    };

    public sealed class PayrollRun
    {
        public int Id { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public string Status { get; set; } = "Draft";
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalGosiCompany { get; set; }
        public string? CalculatedBy { get; set; }
        public DateTime? CalculatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string StatusLabelText => StatusLabel(Status);
        public string PeriodText => $"{Month:00}/{Year}";
    }

    public sealed class PayrollLine
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public decimal BasicSalary { get; set; }
        public decimal TotalAllowances { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal GosiEmployee { get; set; }
        public decimal GosiCompany { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal NetSalary { get; set; }
        public int WorkDays { get; set; }
        public int AbsentDays { get; set; }
        public List<Component> Components { get; set; } = new();

        public decimal TotalDeductions => TaxAmount + GosiEmployee + OtherDeductions;
        public decimal EmployerCost => GrossSalary + GosiCompany;
        public IEnumerable<Component> Earnings => Components.Where(c => c.IsAddition);
        public IEnumerable<Component> Deductions => Components.Where(c => !c.IsAddition);
    }

    public sealed class Component
    {
        public string ItemName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsAddition { get; set; }
        public string Kind { get; set; } = string.Empty;   // Basic|Allowance|Income|Overtime|SalaryDays|LeaveEncashment|Formula|Deduction|Leave|Tax|Gosi|Penalty
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('PayrollRuns', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRuns
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BatchNo nvarchar(30) NOT NULL,
        [Year] int NOT NULL,
        [Month] int NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT(N'Draft'),
        EmployeeCount int NOT NULL DEFAULT(0),
        TotalGross decimal(18,2) NOT NULL DEFAULT(0),
        TotalNet decimal(18,2) NOT NULL DEFAULT(0),
        TotalTax decimal(18,2) NOT NULL DEFAULT(0),
        TotalGosiCompany decimal(18,2) NOT NULL DEFAULT(0),
        CalculatedBy nvarchar(150) NULL,
        CalculatedAt datetime2 NULL,
        LockedAt datetime2 NULL,
        IssuedAt datetime2 NULL,
        PayslipSentAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('PayrollRunLines', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunLines
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId int NOT NULL,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NOT NULL DEFAULT(0),
        TotalAllowances decimal(18,2) NOT NULL DEFAULT(0),
        GrossSalary decimal(18,2) NOT NULL DEFAULT(0),
        TaxAmount decimal(18,2) NOT NULL DEFAULT(0),
        GosiEmployee decimal(18,2) NOT NULL DEFAULT(0),
        GosiCompany decimal(18,2) NOT NULL DEFAULT(0),
        OtherDeductions decimal(18,2) NOT NULL DEFAULT(0),
        NetSalary decimal(18,2) NOT NULL DEFAULT(0),
        WorkDays int NOT NULL DEFAULT(0),
        AbsentDays int NOT NULL DEFAULT(0)
    );
    CREATE INDEX IX_PayrollRunLines_Run ON PayrollRunLines (RunId);
END;

IF OBJECT_ID('PayrollRunLineComponents', 'U') IS NULL
BEGIN
    CREATE TABLE PayrollRunLineComponents
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LineId int NOT NULL,
        ItemName nvarchar(200) NOT NULL,
        Amount decimal(18,2) NOT NULL DEFAULT(0),
        IsAddition bit NOT NULL DEFAULT(1),
        Kind nvarchar(30) NOT NULL DEFAULT(N'Allowance')
    );
    CREATE INDEX IX_PayrollRunLineComponents_Line ON PayrollRunLineComponents (LineId);
END;
""");
    }

    public static async Task<List<PayrollRun>> ListRunsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM PayrollRuns ORDER BY [Year] DESC, [Month] DESC, Id DESC;",
            command => { },
            ReadRun);
    }

    public static async Task<PayrollRun?> GetRunAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        return (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM PayrollRuns WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            ReadRun)).FirstOrDefault();
    }

    /// <summary>إنشاء دفعة مسير جديدة لشهر (رقم دفعة yyyy-M-seq نمط كيان).</summary>
    public static async Task<(bool Ok, string Message, int RunId)> CreateRunAsync(
        ApplicationDbContext dbContext, int year, int month)
    {
        await EnsureAsync(dbContext);
        if (year < 2000 || month is < 1 or > 12) return (false, "شهر غير صالح.", 0);

        var seq = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(1) FROM PayrollRuns WHERE [Year] = @Y AND [Month] = @M;",
            command => { HrmsDatabase.AddParameter(command, "@Y", year); HrmsDatabase.AddParameter(command, "@M", month); }) + 1;

        var batchNo = $"{year}-{month}-{seq}";
        var id = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "INSERT INTO PayrollRuns (BatchNo, [Year], [Month], Status) VALUES (@Batch, @Y, @M, N'Draft'); SELECT CAST(SCOPE_IDENTITY() AS int);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Batch", batchNo);
                HrmsDatabase.AddParameter(command, "@Y", year);
                HrmsDatabase.AddParameter(command, "@M", month);
            });
        return (true, $"أُنشئت دفعة {batchNo} — شغّل «الاحتساب».", id);
    }

    /// <summary>حساب المسير: يبني السطور والبنود. مسموح فقط على Draft/Calculated.</summary>
    public static async Task<(bool Ok, string Message)> CalculateAsync(
        ApplicationDbContext dbContext, int runId, string userName)
    {
        var run = await GetRunAsync(dbContext, runId);
        if (run == null) return (false, "الدفعة غير موجودة.");
        if (run.Status is "Locked" or "Issued" or "PayslipSent")
            return (false, "لا يمكن إعادة احتساب دفعة مقفلة/معتمدة.");

        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);
        await EmployeeAllowanceSchema.EnsureAsync(dbContext);
        await MonthAttendanceStore.EnsureAsync(dbContext);
        await ViolationCaseSchema.EnsureAsync(dbContext);

        var taxProfile = await PayrollConfigStore.ActiveTaxProfileAsync(dbContext);
        var gosiProfile = await PayrollConfigStore.ActiveGosiProfileAsync(dbContext);

        var periodStart = new DateOnly(run.Year, run.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        // --- مدخلات: موظفون + ملف مالي + علاوات + حضور شهري + خصومات مخالفات ---
        var employees = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY EmployeeNo;",
            command => { },
            reader => new { Id = HrmsDatabase.GetInt(reader, "Id"), No = HrmsDatabase.GetString(reader, "EmployeeNo"), Name = HrmsDatabase.GetString(reader, "FullName") });

        var financial = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ISNULL(BasicSalary,0) AS BasicSalary, ISNULL(StopSalaryCalc,0) AS StopSalaryCalc FROM EmployeeFinancialInfos WHERE ISNULL(IsDeleted,0)=0;",
            command => { },
            reader => new { EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"), Basic = reader["BasicSalary"] is decimal b ? b : 0, Stop = HrmsDatabase.GetBool(reader, "StopSalaryCalc") }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var allowances = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ItemName, ISNULL(Amount,0) AS Amount, FromDate, ToDate, ISNULL(EndAfterDate,0) AS EndAfterDate FROM EmployeeAllowances WHERE ISNULL(IsDeleted,0)=0;",
            command => { },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                Amount = reader["Amount"] is decimal a ? a : 0,
                From = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                To = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                EndAfter = HrmsDatabase.GetBool(reader, "EndAfterDate")
            }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var months = (await MonthAttendanceStore.ListAsync(dbContext, run.Year, run.Month))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var penalties = (await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT EmployeeId, ISNULL(DeductionAmount,0) AS DeductionAmount, ISNULL(FinancialImpactType,N'None') AS FinancialImpactType, ISNULL(FinancialImpactValue,0) AS FinancialImpactValue FROM EmployeeViolationCases WHERE ISNULL(IsDeleted,0)=0 AND EventDate >= @From AND EventDate <= @To;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@From", periodStart.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", periodEnd.ToDateTime(TimeOnly.MaxValue));
            },
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                Direct = reader["DeductionAmount"] is decimal da ? da : 0,
                Type = HrmsDatabase.GetString(reader, "FinancialImpactType"),
                Value = reader["FinancialImpactValue"] is decimal v ? v : 0
            }))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // حركات الدخل/الاقتطاع للفترة (شاشة «الحركات») — بنود إضافية/خصم بالقسيمة
        var income = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Income))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var deductionTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Deduction))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var overtimeTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.Overtime))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var salaryDaysTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.SalaryDays))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var leaveEncashTx = (await PayrollTransactionStore.ForPeriodAsync(dbContext, run.Year, run.Month, PayrollTransactionStore.LeaveEncashment))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // عناصر الراتب ذات الصيغة (غير النظامية النشطة) — تُقيَّم لكل موظف بمحرك الصيغ
        // وتُضاف بنوداً للقسيمة (استحقاق يدخل الإجمالي/الوعاء الخاضع، أو اقتطاع). عناصر
        // النظام (الأساسي/الضريبة/الضمان) مستثناة — يعالجها المحرك مباشرةً.
        var formulaItems = (await SalaryItemStore.ListAsync(dbContext))
            .Where(x => x.IsActive && !x.IsSystem && x.ValueKind == "Formula" && !string.IsNullOrWhiteSpace(x.Formula))
            .OrderBy(x => x.SortOrder).ToList();

        // --- بناء السطور ---
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE c FROM PayrollRunLineComponents c INNER JOIN PayrollRunLines l ON l.Id = c.LineId WHERE l.RunId = @RunId; DELETE FROM PayrollRunLines WHERE RunId = @RunId;",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId));

        int count = 0;
        decimal totalGross = 0, totalNet = 0, totalTax = 0, totalGosiCo = 0;

        foreach (var emp in employees)
        {
            financial.TryGetValue(emp.Id, out var fin);
            if (fin?.Stop == true) continue;                 // مستبعَد من الاحتساب
            var basic = fin?.Basic ?? 0;
            if (basic <= 0 && !allowances.ContainsKey(emp.Id) && !income.ContainsKey(emp.Id)
                && !overtimeTx.ContainsKey(emp.Id) && !salaryDaysTx.ContainsKey(emp.Id)
                && !leaveEncashTx.ContainsKey(emp.Id))
                continue; // لا راتب ولا علاوات ولا حركات دخل/عمل إضافي/أيام/بدل إجازة

            // تنسيب الأساسي حسب أيام الحضور من الاعتماد الشهري
            months.TryGetValue(emp.Id, out var month);
            var workDays = month?.WorkDays ?? 0;
            var absentDays = month?.AbsentDays ?? 0;
            var unpaidLeaveDays = month?.UnpaidLeaveDays ?? 0;
            var factor = workDays > 0 ? Math.Max(0m, (workDays - absentDays)) / workDays : 1m;
            var proratedBasic = Math.Round(basic * factor, 2);

            var dailyRate = basic > 0 ? Math.Round(basic / 30m, 4) : 0;
            var hourlyRate = dailyRate > 0 ? Math.Round(dailyRate / 8m, 4) : 0;

            var comps = new List<Component>();
            if (proratedBasic != 0)
                comps.Add(new Component { ItemName = "الراتب الأساسي", Amount = proratedBasic, IsAddition = true, Kind = "Basic" });

            decimal allowancesTotal = 0;
            if (allowances.TryGetValue(emp.Id, out var empAllow))
            {
                foreach (var al in empAllow)
                {
                    var active = (al.From == null || al.From <= periodEnd)
                        && (al.To == null || !al.EndAfter || al.To >= periodStart);
                    if (!active || al.Amount == 0) continue;
                    allowancesTotal += al.Amount;
                    comps.Add(new Component { ItemName = al.ItemName, Amount = al.Amount, IsAddition = true, Kind = "Allowance" });
                }
            }

            // حركات الدخل (مكافآت/حوافز/بدلات لمرة) — بنود إضافية، بعضها غير خاضع للضريبة
            decimal incomeTotal = 0, taxableIncome = 0;
            if (income.TryGetValue(emp.Id, out var empIncome))
            {
                foreach (var t in empIncome)
                {
                    if (t.Amount == 0) continue;
                    incomeTotal += t.Amount;
                    if (t.Taxable) taxableIncome += t.Amount;
                    comps.Add(new Component { ItemName = t.ItemName, Amount = t.Amount, IsAddition = true, Kind = "Income" });
                }
            }

            // العمل الإضافي (شاشة «العمل الإضافي») — ساعات × الأجر الساعي × معامل البدل.
            // إن لم تُحدَّد ساعات (إدخال مبلغ يدوي) يُستخدم المبلغ المخزّن كما هو.
            decimal overtimeTotal = 0, taxableOvertime = 0;
            if (overtimeTx.TryGetValue(emp.Id, out var empOt))
            {
                foreach (var t in empOt)
                {
                    var amt = t.Hours is > 0
                        ? Math.Round(t.Hours.Value * hourlyRate * (t.RateFactor ?? PayrollTransactionStore.DefaultRateFactor), 2)
                        : t.Amount;
                    if (amt == 0) continue;
                    overtimeTotal += amt;
                    if (t.Taxable) taxableOvertime += amt;
                    var label = t.Hours is > 0
                        ? $"{t.ItemName} ({t.Hours:0.##}س × {t.RateFactor ?? PayrollTransactionStore.DefaultRateFactor:0.##})"
                        : t.ItemName;
                    comps.Add(new Component { ItemName = label, Amount = amt, IsAddition = true, Kind = "Overtime" });
                }
            }

            // تعديل أيام الراتب (شاشة «تعديل أيام الراتب») — أيام موقّعة × الأجر اليومي.
            // موجب = إضافة أيام (استحقاق يدخل الإجمالي والوعاء الخاضع)، سالب = خصم أيام
            // (استقطاع بعد الإجمالي كنمط حركات الاقتطاع). إن لم تُحدَّد أيام يُستخدم المبلغ
            // المخزّن موقّعاً. الأجر اليومي = الأساسي ÷ 30.
            decimal salaryDaysAdd = 0, salaryDaysDeduct = 0;
            if (salaryDaysTx.TryGetValue(emp.Id, out var empDays))
            {
                foreach (var t in empDays)
                {
                    var signed = t.Days.HasValue && t.Days.Value != 0
                        ? Math.Round(t.Days.Value * dailyRate, 2)
                        : t.Amount;
                    if (signed == 0) continue;
                    var abs = Math.Abs(signed);
                    var label = t.Days.HasValue && t.Days.Value != 0
                        ? $"{t.ItemName} ({t.Days:+0.##;-0.##} يوم)"
                        : t.ItemName;
                    if (signed > 0) salaryDaysAdd += abs; else salaryDaysDeduct += abs;
                    comps.Add(new Component { ItemName = label, Amount = abs, IsAddition = signed > 0, Kind = "SalaryDays" });
                }
            }

            // بدل الإجازة (شاشة «بدل إجازة») — صرف أيام رصيد إجازة نقداً: أيام × الأجر
            // اليومي (أساسي÷30) كاستحقاق يدخل الإجمالي والوعاء الخاضع. Amount يدوي بديل.
            decimal leaveEncashTotal = 0, taxableLeaveEncash = 0;
            if (leaveEncashTx.TryGetValue(emp.Id, out var empEnc))
            {
                foreach (var t in empEnc)
                {
                    var amt = t.Days is > 0 ? Math.Round(t.Days.Value * dailyRate, 2) : t.Amount;
                    if (amt == 0) continue;
                    leaveEncashTotal += amt;
                    if (t.Taxable) taxableLeaveEncash += amt;
                    var label = t.Days is > 0 ? $"{t.ItemName} ({t.Days:0.##} يوم)" : t.ItemName;
                    comps.Add(new Component { ItemName = label, Amount = amt, IsAddition = true, Kind = "LeaveEncashment" });
                }
            }

            // محرك الصيغ: كل عنصر معادلة يُقيَّم بمتغيّرات الموظف. الاستحقاق يدخل
            // الإجمالي (والوعاء الخاضع إن كان خاضعاً)، والاقتطاع بعد الإجمالي. النسبي
            // يُضرب بمعامل الحضور. الصيغة المعطوبة تُتخطّى بلا إسقاط المسير.
            decimal formulaAddTotal = 0, formulaTaxableAdd = 0, formulaDeductTotal = 0;
            if (formulaItems.Count > 0)
            {
                var formulaVars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Basic"] = basic,
                    ["Allowances"] = allowancesTotal,
                    ["Gross"] = basic + allowancesTotal,
                    ["DailyRate"] = dailyRate,
                    ["HourlyRate"] = hourlyRate,
                    ["Hours"] = 0,
                    ["Days"] = 0
                };
                foreach (var item in formulaItems)
                {
                    if (!SalaryFormulaEvaluator.TryEvaluate(item.Formula, formulaVars, out var raw, out _)) continue;
                    var value = Math.Round(item.Prorated ? raw * factor : raw, 2);
                    if (value == 0) continue;
                    if (item.IsAddition)
                    {
                        formulaAddTotal += value;
                        if (item.Taxable) formulaTaxableAdd += value;
                        comps.Add(new Component { ItemName = item.Name, Amount = value, IsAddition = true, Kind = "Formula" });
                    }
                    else
                    {
                        formulaDeductTotal += value;
                        comps.Add(new Component { ItemName = item.Name, Amount = value, IsAddition = false, Kind = "Formula" });
                    }
                }
            }

            var gross = Math.Round(proratedBasic + allowancesTotal + incomeTotal + overtimeTotal + salaryDaysAdd + leaveEncashTotal + formulaAddTotal, 2);
            var taxableBase = Math.Round(proratedBasic + allowancesTotal + taxableIncome + taxableOvertime + salaryDaysAdd + taxableLeaveEncash + formulaTaxableAdd, 2);
            var tax = PayrollConfigStore.ComputeTax(taxableBase, taxProfile);
            var (gosiEmp, gosiCo) = PayrollConfigStore.ComputeGosi(gross, gosiProfile);

            // خصومات المخالفات: مباشر بالدينار + أيام×يومي + ساعات×ساعي
            decimal penaltyTotal = 0;
            if (penalties.TryGetValue(emp.Id, out var empPen))
            {
                foreach (var p in empPen)
                {
                    var amt = p.Direct;
                    if (p.Type == "Days") amt += Math.Round(p.Value * dailyRate, 2);
                    else if (p.Type == "Hours") amt += Math.Round(p.Value * hourlyRate, 2);
                    else if (p.Type == "Amount") amt += p.Value;
                    penaltyTotal += amt;
                }
                if (penaltyTotal > 0)
                    comps.Add(new Component { ItemName = "خصومات المخالفات", Amount = penaltyTotal, IsAddition = false, Kind = "Penalty" });
            }

            if (tax > 0) comps.Add(new Component { ItemName = "ضريبة الدخل", Amount = tax, IsAddition = false, Kind = "Tax" });
            if (gosiEmp > 0) comps.Add(new Component { ItemName = "الضمان الاجتماعي (حصة الموظف)", Amount = gosiEmp, IsAddition = false, Kind = "Gosi" });

            // حركات الاقتطاع (خصومات مُدخلة يدوياً بشاشة الحركات)
            decimal deductionTxTotal = 0;
            if (deductionTx.TryGetValue(emp.Id, out var empDed))
            {
                foreach (var t in empDed)
                {
                    if (t.Amount == 0) continue;
                    deductionTxTotal += t.Amount;
                    comps.Add(new Component { ItemName = t.ItemName, Amount = t.Amount, IsAddition = false, Kind = "Deduction" });
                }
            }

            // الإجازة غير المدفوعة (ربط الإجازات بالمسير): يوم الإجازة غير المدفوعة يُعدّ
            // يوم عمل بالحضور (فيُدفع ضمن الأساسي) فنخصمه هنا يوماً×الأجر اليومي (أساسي÷30)
            // كخصم post-gross — نفس نمط تعديل الأيام والمخالفات. الوعاء الخاضع لا يتأثر.
            decimal unpaidLeaveDeduct = 0;
            if (unpaidLeaveDays > 0 && dailyRate > 0)
            {
                unpaidLeaveDeduct = Math.Round(unpaidLeaveDays * dailyRate, 2);
                comps.Add(new Component
                {
                    ItemName = $"إجازة بدون راتب ({unpaidLeaveDays} يوم)",
                    Amount = unpaidLeaveDeduct,
                    IsAddition = false,
                    Kind = "Leave"
                });
            }

            var otherDeductions = penaltyTotal + deductionTxTotal + salaryDaysDeduct + unpaidLeaveDeduct + formulaDeductTotal;
            var net = Math.Round(gross - tax - gosiEmp - otherDeductions, 2);

            var lineId = await HrmsDatabase.ScalarAsync<int>(
                dbContext,
                """
INSERT INTO PayrollRunLines
  (RunId, EmployeeId, BasicSalary, TotalAllowances, GrossSalary, TaxAmount, GosiEmployee, GosiCompany, OtherDeductions, NetSalary, WorkDays, AbsentDays)
VALUES
  (@RunId, @Emp, @Basic, @Allow, @Gross, @Tax, @GosiEmp, @GosiCo, @Other, @Net, @WorkDays, @AbsentDays);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RunId", runId);
                    HrmsDatabase.AddParameter(command, "@Emp", emp.Id);
                    HrmsDatabase.AddParameter(command, "@Basic", proratedBasic);
                    HrmsDatabase.AddParameter(command, "@Allow", allowancesTotal);
                    HrmsDatabase.AddParameter(command, "@Gross", gross);
                    HrmsDatabase.AddParameter(command, "@Tax", tax);
                    HrmsDatabase.AddParameter(command, "@GosiEmp", gosiEmp);
                    HrmsDatabase.AddParameter(command, "@GosiCo", gosiCo);
                    HrmsDatabase.AddParameter(command, "@Other", otherDeductions);
                    HrmsDatabase.AddParameter(command, "@Net", net);
                    HrmsDatabase.AddParameter(command, "@WorkDays", workDays);
                    HrmsDatabase.AddParameter(command, "@AbsentDays", absentDays);
                });

            foreach (var c in comps)
            {
                var current = c;
                await HrmsDatabase.ExecuteAsync(
                    dbContext,
                    "INSERT INTO PayrollRunLineComponents (LineId, ItemName, Amount, IsAddition, Kind) VALUES (@Line, @Name, @Amount, @Add, @Kind);",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Line", lineId);
                        HrmsDatabase.AddParameter(command, "@Name", current.ItemName);
                        HrmsDatabase.AddParameter(command, "@Amount", current.Amount);
                        HrmsDatabase.AddParameter(command, "@Add", current.IsAddition ? 1 : 0);
                        HrmsDatabase.AddParameter(command, "@Kind", current.Kind);
                    });
            }

            count++;
            totalGross += gross; totalNet += net; totalTax += tax; totalGosiCo += gosiCo;
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE PayrollRuns
SET Status = N'Calculated', EmployeeCount = @Count, TotalGross = @Gross, TotalNet = @Net,
    TotalTax = @Tax, TotalGosiCompany = @GosiCo, CalculatedBy = @By, CalculatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", runId);
                HrmsDatabase.AddParameter(command, "@Count", count);
                HrmsDatabase.AddParameter(command, "@Gross", totalGross);
                HrmsDatabase.AddParameter(command, "@Net", totalNet);
                HrmsDatabase.AddParameter(command, "@Tax", totalTax);
                HrmsDatabase.AddParameter(command, "@GosiCo", totalGosiCo);
                HrmsDatabase.AddParameter(command, "@By", userName);
            });

        await transaction.CommitAsync();
        return (true, count == 0
            ? "لا موظفين بأرصدة قابلة للاحتساب — تأكد من الرواتب الأساسية بالملف المالي."
            : $"احتُسب {count} موظفاً — إجمالي {totalGross:0.##}، صافي {totalNet:0.##}.");
    }

    public static async Task<List<PayrollLine>> ListLinesAsync(ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        var lines = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT l.*, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(e.Position, N'') AS Position, ISNULL(d.Name, N'') AS DepartmentName
FROM PayrollRunLines l
INNER JOIN Employees e ON e.Id = l.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
WHERE l.RunId = @RunId
ORDER BY e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new PayrollLine
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RunId = HrmsDatabase.GetInt(reader, "RunId"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                BasicSalary = reader["BasicSalary"] is decimal b ? b : 0,
                TotalAllowances = reader["TotalAllowances"] is decimal a ? a : 0,
                GrossSalary = reader["GrossSalary"] is decimal g ? g : 0,
                TaxAmount = reader["TaxAmount"] is decimal t ? t : 0,
                GosiEmployee = reader["GosiEmployee"] is decimal ge ? ge : 0,
                GosiCompany = reader["GosiCompany"] is decimal gc ? gc : 0,
                OtherDeductions = reader["OtherDeductions"] is decimal o ? o : 0,
                NetSalary = reader["NetSalary"] is decimal n ? n : 0,
                WorkDays = HrmsDatabase.GetInt(reader, "WorkDays"),
                AbsentDays = HrmsDatabase.GetInt(reader, "AbsentDays")
            });

        if (lines.Count > 0)
        {
            var comps = await HrmsDatabase.QueryAsync(
                dbContext,
                """
SELECT c.LineId, c.ItemName, c.Amount, c.IsAddition, c.Kind
FROM PayrollRunLineComponents c
INNER JOIN PayrollRunLines l ON l.Id = c.LineId
WHERE l.RunId = @RunId
ORDER BY c.IsAddition DESC, c.Id;
""",
                command => HrmsDatabase.AddParameter(command, "@RunId", runId),
                reader => new
                {
                    LineId = HrmsDatabase.GetInt(reader, "LineId"),
                    Comp = new Component
                    {
                        ItemName = HrmsDatabase.GetString(reader, "ItemName"),
                        Amount = reader["Amount"] is decimal a ? a : 0,
                        IsAddition = HrmsDatabase.GetBool(reader, "IsAddition"),
                        Kind = HrmsDatabase.GetString(reader, "Kind")
                    }
                });
            var byLine = comps.GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.Select(x => x.Comp).ToList());
            foreach (var l in lines)
                l.Components = byLine.TryGetValue(l.Id, out var list) ? list : new();
        }
        return lines;
    }

    /// <summary>صف ملف البنك: بيانات الدفع للموظف + صافي راتبه بالدفعة.</summary>
    public sealed class BankFileRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string BankBranch { get; set; } = string.Empty;
        public string Iban { get; set; } = string.Empty;
        public string CardNo { get; set; } = string.Empty;
        public decimal NetSalary { get; set; }

        /// <summary>لا آيبان ولا رقم بطاقة ⟹ الصف لا يُحوَّل، ويُعلَّم ليصححه المستخدم.</summary>
        public bool IsPayable => !string.IsNullOrWhiteSpace(Iban) || !string.IsNullOrWhiteSpace(CardNo);
    }

    /// <summary>
    /// صفوف ملف البنك لدفعة: سطور المسير + بيانات الدفع من الملف المالي للموظف.
    /// الصفوف بلا آيبان/بطاقة تُرجَع أيضاً (معلَّمة) بدل أن تختفي بصمت.
    /// </summary>
    public static async Task<List<BankFileRow>> BankFileRowsAsync(
        ApplicationDbContext dbContext, int runId)
    {
        await EnsureAsync(dbContext);
        await EmployeeFinancialInfoSchema.EnsureAsync(dbContext);

        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
       ISNULL(f.PaymentMethod, N'') AS PaymentMethod, ISNULL(f.BankName, N'') AS BankName,
       ISNULL(f.BankBranch, N'') AS BankBranch, ISNULL(f.Iban, N'') AS Iban,
       ISNULL(f.CardNo, N'') AS CardNo, l.NetSalary
FROM PayrollRunLines l
INNER JOIN Employees e ON e.Id = l.EmployeeId
OUTER APPLY (
    SELECT TOP 1 * FROM EmployeeFinancialInfos fi
    WHERE fi.EmployeeId = l.EmployeeId AND ISNULL(fi.IsDeleted, 0) = 0
    ORDER BY fi.Id DESC
) f
WHERE l.RunId = @RunId
ORDER BY e.EmployeeNo;
""",
            command => HrmsDatabase.AddParameter(command, "@RunId", runId),
            reader => new BankFileRow
            {
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                PaymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                BankName = HrmsDatabase.GetString(reader, "BankName"),
                BankBranch = HrmsDatabase.GetString(reader, "BankBranch"),
                Iban = HrmsDatabase.GetString(reader, "Iban"),
                CardNo = HrmsDatabase.GetString(reader, "CardNo"),
                NetSalary = reader["NetSalary"] is decimal n ? n : 0
            });
    }

    public static async Task<PayrollLine?> GetLineAsync(ApplicationDbContext dbContext, int runId, int employeeId) =>
        (await ListLinesAsync(dbContext, runId)).FirstOrDefault(x => x.EmployeeId == employeeId);

    // ---------------- دورة الحياة ----------------
    public static async Task<(bool, string)> LockAsync(ApplicationDbContext dbContext, int runId)
    {
        var run = await GetRunAsync(dbContext, runId);
        var res = await TransitionAsync(dbContext, runId, from: "Calculated", to: "Locked", "LockedAt", "قُفلت الدفعة.");
        // قفل حركات الدفعة (لكل حركة) — الحركات الجديدة بعدها تبقى غير مقفلة
        if (res.Item1 && run != null)
            await PayrollTransactionStore.LockForRunAsync(dbContext, runId, run.Year, run.Month);
        return res;
    }

    public static Task<(bool, string)> IssueAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Locked", to: "Issued", "IssuedAt", "اعتُمدت للصرف.");

    public static Task<(bool, string)> SendPayslipsAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Issued", to: "PayslipSent", "PayslipSentAt", "أُرسلت القسائم.");

    public static Task<(bool, string)> ReopenAsync(ApplicationDbContext dbContext, int runId) =>
        TransitionAsync(dbContext, runId, from: "Calculated", to: "Draft", null, "أُعيدت للمسودة.");

    public static async Task<(bool, string)> DeleteRunAsync(ApplicationDbContext dbContext, int runId)
    {
        var run = await GetRunAsync(dbContext, runId);
        if (run == null) return (false, "غير موجودة.");
        if (run.Status is not ("Draft" or "Calculated")) return (false, "لا تُحذف دفعة مقفلة/معتمدة.");
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE c FROM PayrollRunLineComponents c INNER JOIN PayrollRunLines l ON l.Id = c.LineId WHERE l.RunId = @Id; DELETE FROM PayrollRunLines WHERE RunId = @Id; DELETE FROM PayrollRuns WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", runId));
        return (true, "حُذفت الدفعة.");
    }

    private static async Task<(bool, string)> TransitionAsync(
        ApplicationDbContext dbContext, int runId, string from, string to, string? stampColumn, string okMessage)
    {
        await EnsureAsync(dbContext);
        var extra = stampColumn == null ? "" : $", {stampColumn} = SYSUTCDATETIME()";
        var affected = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            $"UPDATE PayrollRuns SET Status = @To{extra} WHERE Id = @Id AND Status = @From; SELECT @@ROWCOUNT;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", runId);
                HrmsDatabase.AddParameter(command, "@To", to);
                HrmsDatabase.AddParameter(command, "@From", from);
            });
        return affected > 0 ? (true, okMessage) : (false, "الحالة الحالية لا تسمح بهذا الانتقال.");
    }

    private static PayrollRun ReadRun(System.Data.Common.DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        BatchNo = HrmsDatabase.GetString(reader, "BatchNo"),
        Year = HrmsDatabase.GetInt(reader, "Year"),
        Month = HrmsDatabase.GetInt(reader, "Month"),
        Status = HrmsDatabase.GetString(reader, "Status") is { Length: > 0 } s ? s : "Draft",
        EmployeeCount = HrmsDatabase.GetInt(reader, "EmployeeCount"),
        TotalGross = reader["TotalGross"] is decimal g ? g : 0,
        TotalNet = reader["TotalNet"] is decimal n ? n : 0,
        TotalTax = reader["TotalTax"] is decimal t ? t : 0,
        TotalGosiCompany = reader["TotalGosiCompany"] is decimal gc ? gc : 0,
        CalculatedBy = HrmsDatabase.GetString(reader, "CalculatedBy") is { Length: > 0 } by ? by : null,
        CalculatedAt = HrmsDatabase.GetDateTime(reader, "CalculatedAt"),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? default
    };
}
