using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

/// <summary>
/// GAP-02 — <b>PAYROLL_GOLDEN_DATASET</b>: عشرة موظفين، كلٌّ منهم محسوبٌ
/// <b>يدوياً وبشكلٍ مستقلّ</b> عن كود الإنتاج، ثم يُقارَن بمخرَج النظام.
///
/// <para><b>لماذا وُجد:</b> تدقيق 2026-08-11 وجد تغطيةً وحداتيّةً جيّدة للرواتب
/// (19 من 22 ملفّاً تنفّذ منطقاً حقيقيّاً)، لكن **بلا مجموعة ذهبيّة**: مصدر
/// «النتيجة المتوقَّعة» بكل اختبار كان فهمَ كاتبه للصيغة — وهو نفس المصدر الذي
/// كتب الصيغة. فخطأٌ بالفهم يُنتج اختباراً يوافق الكود ويخالف الواقع.</para>
///
/// <para><b>معيار الأوراكل (§180):</b> كل رقم متوقَّع أدناه مكتوبٌ بحسابه الكامل
/// بالتعليق — شريحةً شريحة — ولا يُستدعى كود الإنتاج لتوليده. من أراد التحقّق
/// يعيد الحساب بالورقة لا بقراءة الكود.</para>
///
/// <para><b>وما تكشفه ولا تكشفه الاختبارات المنفردة:</b> هذه تختبر
/// <b>السلسلة</b>: وعاء ⟶ ضريبة تصاعديّة ⟶ ضمان بسقف ⟶ صافٍ. عيبُ التركيب
/// (وعاءٌ صحيح وضريبةٌ صحيحة ونتيجةٌ خاطئة) لا يظهر إلا هنا.</para>
///
/// <para>الملف الضريبي: إعفاء 100,000 · شرائح 0–100k@10% · 100k–200k@20% ·
/// 200k+@30%. الضمان: موظّف 5% · شركة 12% · سقف 500,000.</para>
/// </summary>
public class PayrollGoldenDatasetTests
{
    private const decimal Exemption = 100_000m;
    private const decimal GosiEmployeeRate = 5m;
    private const decimal GosiCeiling = 500_000m;

    private static PayrollConfigStore.TaxProfile GoldenTaxProfile() => new()
    {
        ExemptionAmount = Exemption,
        Brackets = new List<PayrollConfigStore.TaxBracket>
        {
            new() { FromAmount = 0m,       ToAmount = 100_000m, Rate = 10m },
            new() { FromAmount = 100_000m, ToAmount = 200_000m, Rate = 20m },
            new() { FromAmount = 200_000m, ToAmount = null,     Rate = 30m }
        }
    };

    private static PayrollConfigStore.GosiProfile GoldenGosiProfile() => new()
    {
        EmployeeRate = GosiEmployeeRate,
        CompanyRate = 12m,
        Ceiling = GosiCeiling
    };

    /// <summary>موظّف ذهبيّ: مدخلاته، ونتائجه المحسوبة يدوياً.</summary>
    public sealed record GoldenEmployee(
        string Name,
        SalaryBaseComposer.Amounts Amounts,
        decimal ExpectedTaxBase,
        decimal ExpectedTax,
        decimal ExpectedEmployeeGosi,
        decimal ExpectedNet);

    private static SalaryBaseComposer.Amounts Money(
        decimal basic = 0m,
        decimal allowances = 0m,
        decimal taxableOvertime = 0m,
        decimal? gross = null) => new()
        {
            Basic = basic,
            Allowances = allowances,
            TaxableOvertime = taxableOvertime,
            Gross = gross ?? (basic + allowances + taxableOvertime)
        };

    public static IEnumerable<object[]> Golden() => GoldenSet().Select(e => new object[] { e });

    private static IEnumerable<GoldenEmployee> GoldenSet()
    {
        // ── A — راتبٌ عالٍ يعبر الشرائح الثلاث ويتجاوز سقف الضمان ──────────
        // الوعاء   = 800,000
        // الأساس   = 800,000 − 100,000 إعفاء = 700,000
        // الضريبة  = 100,000×10% =  10,000
        //          + 100,000×20% =  20,000
        //          + 500,000×30% = 150,000   ⟹ 180,000
        // الضمان   = min(800,000 ، سقف 500,000) × 5% = 25,000
        // الصافي   = 800,000 − 180,000 − 25,000 = 595,000
        yield return new("A — عابر الشرائح الثلاث", Money(basic: 800_000m),
            800_000m, 180_000m, 25_000m, 595_000m);

        // ── B — تحت الإعفاء تماماً ⟹ لا ضريبة إطلاقاً ──────────────────────
        // الأساس = max(0 ، 90,000 − 100,000) = 0 ⟹ ضريبة 0
        // الضمان = 90,000 × 5% = 4,500
        // الصافي = 90,000 − 0 − 4,500 = 85,500
        yield return new("B — تحت الإعفاء", Money(basic: 90_000m),
            90_000m, 0m, 4_500m, 85_500m);

        // ── C — على حدّ الإعفاء بالضبط (حدّ سفليّ) ─────────────────────────
        // الأساس = 100,000 − 100,000 = 0 ⟹ ضريبة 0 (وليس أوّل شريحة)
        // الضمان = 100,000 × 5% = 5,000 ⟹ الصافي 95,000
        yield return new("C — على حدّ الإعفاء", Money(basic: 100_000m),
            100_000m, 0m, 5_000m, 95_000m);

        // ── D — أوّل وحدةٍ فوق الإعفاء (حدّ + 1) ───────────────────────────
        // الأساس  = 100,001 − 100,000 = 1
        // الضريبة = 1 × 10% = 0.10
        // الضمان  = 100,001 × 5% = 5,000.05
        // الصافي  = 100,001 − 0.10 − 5,000.05 = 95,000.85
        yield return new("D — الإعفاء + 1", Money(basic: 100_001m),
            100_001m, 0.10m, 5_000.05m, 95_000.85m);

        // ── E — أساسيّ + بدلات خاضعة (تركيب الوعاء) ───────────────────────
        // الوعاء  = 200,000 + 50,000 = 250,000
        // الأساس  = 150,000
        // الضريبة = 100,000×10% + 50,000×20% = 10,000 + 10,000 = 20,000
        // الضمان  = 250,000 × 5% = 12,500
        // الصافي  = 250,000 − 20,000 − 12,500 = 217,500
        yield return new("E — أساسيّ + بدلات", Money(basic: 200_000m, allowances: 50_000m),
            250_000m, 20_000m, 12_500m, 217_500m);

        // ── F — على سقف الضمان بالضبط ─────────────────────────────────────
        // الأساس  = 400,000
        // الضريبة = 10,000 + 20,000 + 200,000×30% (60,000) = 90,000
        // الضمان  = min(500,000 ، 500,000) × 5% = 25,000
        // الصافي  = 500,000 − 90,000 − 25,000 = 385,000
        yield return new("F — على سقف الضمان", Money(basic: 500_000m),
            500_000m, 90_000m, 25_000m, 385_000m);

        // ── G — فوق السقف: الضمان **لا يزيد** عن حالة F ────────────────────
        // الأساس  = 800,000
        // الضريبة = 10,000 + 20,000 + 600,000×30% (180,000) = 210,000
        // الضمان  = min(900,000 ، 500,000) × 5% = 25,000  ← نفس F بالضبط
        // الصافي  = 900,000 − 210,000 − 25,000 = 665,000
        yield return new("G — فوق سقف الضمان", Money(basic: 900_000m),
            900_000m, 210_000m, 25_000m, 665_000m);

        // ── H — أجرٌ إضافيّ خاضع يدخل الوعاء ───────────────────────────────
        // الوعاء  = 150,000 + 30,000 = 180,000
        // الأساس  = 80,000 ⟹ الضريبة = 80,000 × 10% = 8,000
        // الضمان  = 180,000 × 5% = 9,000
        // الصافي  = 180,000 − 8,000 − 9,000 = 163,000
        yield return new("H — أجر إضافيّ خاضع",
            Money(basic: 150_000m, taxableOvertime: 30_000m),
            180_000m, 8_000m, 9_000m, 163_000m);

        // ── I — أصفار: لا ضريبة ولا ضمان ولا صافٍ سالب ────────────────────
        yield return new("I — أصفار", Money(), 0m, 0m, 0m, 0m);

        // ── J — كسور: يثبت التقريب لمنزلتين على كل خطوة ────────────────────
        // الوعاء  = 123,456.78
        // الأساس  = 23,456.78
        // الضريبة = 23,456.78 × 10% = 2,345.678 ⟶ 2,345.68
        // الضمان  = 123,456.78 × 5% = 6,172.839 ⟶ 6,172.84
        // الصافي  = 123,456.78 − 2,345.68 − 6,172.84 = 114,938.26
        yield return new("J — كسور وتقريب", Money(basic: 123_456.78m),
            123_456.78m, 2_345.68m, 6_172.84m, 114_938.26m);
    }

    [Theory]
    [MemberData(nameof(Golden))]
    public void GoldenEmployee_MatchesHandCalculatedPayroll(GoldenEmployee employee)
    {
        // (١) الوعاء الضريبيّ — تركيب المكوّنات
        var taxBase = SalaryBaseComposer.TaxBase(employee.Amounts);
        Assert.Equal(employee.ExpectedTaxBase, taxBase);

        // (٢) الضريبة التصاعديّة بعد الإعفاء
        var tax = PayrollConfigStore.ComputeTax(taxBase, GoldenTaxProfile());
        Assert.Equal(employee.ExpectedTax, tax);

        // (٣) الضمان على الإجمالي بسقفه
        var gosiBase = SalaryBaseComposer.GosiBase(employee.Amounts);
        var (employeeGosi, _) = PayrollConfigStore.ComputeGosi(gosiBase, GoldenGosiProfile());
        Assert.Equal(employee.ExpectedEmployeeGosi, employeeGosi);

        // (٤) الصافي — **حلقة التركيب** التي لا يكشفها أي اختبارٍ منفرد
        var net = employee.Amounts.Gross - tax - employeeGosi;
        Assert.Equal(employee.ExpectedNet, net);
    }

    [Fact]
    public void GosiCeiling_MakesHighEarnersPayTheSameContribution()
    {
        // خاصيّة تحوّليّة (metamorphic): فوق السقف، مضاعفة الراتب **لا** تغيّر
        // اشتراك الضمان. تُمسك انزلاقاً بمنطق السقف لا يظهر بمثالٍ واحد.
        var profile = GoldenGosiProfile();

        var (atCeiling, _) = PayrollConfigStore.ComputeGosi(GosiCeiling, profile);
        var (aboveCeiling, _) = PayrollConfigStore.ComputeGosi(GosiCeiling * 4m, profile);

        Assert.Equal(atCeiling, aboveCeiling);
        Assert.Equal(25_000m, atCeiling); // 500,000 × 5% — محسوبة يدوياً
    }

    [Fact]
    public void TaxIsMonotonic_MoreIncomeNeverMeansLessTax()
    {
        // ثابتٌ نظاميّ (invariant): الضريبة التصاعدية لا تنقص أبداً بزيادة الدخل.
        // انعكاسٌ هنا يعني خطأً بحدود الشرائح — وهو عيبٌ يمرّ من الأمثلة المنفردة.
        var profile = GoldenTaxProfile();
        decimal previous = -1m;

        for (var income = 0m; income <= 1_000_000m; income += 25_000m)
        {
            var tax = PayrollConfigStore.ComputeTax(income, profile);
            Assert.True(tax >= previous, $"الضريبة نقصت عند دخل {income}: {tax} < {previous}");
            previous = tax;
        }
    }

    [Fact]
    public void NetSalary_NeverExceedsGross()
    {
        // ثابتٌ ماليّ: الصافي لا يتجاوز الإجمالي أبداً — أي اقتطاعٍ سالب يكسره.
        foreach (var employee in GoldenSet())
        {
            Assert.True(
                employee.ExpectedNet <= employee.Amounts.Gross,
                $"{employee.Name}: صافٍ ({employee.ExpectedNet}) أكبر من الإجمالي ({employee.Amounts.Gross}).");
        }
    }
}
