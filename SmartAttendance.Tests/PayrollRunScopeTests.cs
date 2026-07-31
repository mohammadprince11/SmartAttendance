using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نطاق تشغيل المسير.
///
/// أهمّها الأولان: **نطاق فارغ = الكل**. قبل هذه الميزة كان المحرك يحسب كل
/// النشطين إجبارياً، وأي انحراف يجعل الدفعات القائمة (بلا صفوف نطاق) تحسب صفر
/// موظفاً — أي راتب شهر يختفي بصمت.
///
/// والباقي يحرس مسار «لصق أكواد من إكسل»: كود مخطئ واحد = موظف يغيب عن راتبه،
/// فالمفقود يجب أن يُعاد بنصّه لا كعدد.
/// </summary>
public class PayrollRunScopeTests
{
    private static readonly Dictionary<string, int> Map = PayrollRunScope.BuildCodeMap(new[]
    {
        ("E-100", 1), ("E-200", 2), ("e-300", 3)
    });

    [Fact]
    public void EmptyScope_IncludesEveryone()
    {
        var scope = new List<int>();

        Assert.True(PayrollRunScope.Includes(scope, 1));
        Assert.True(PayrollRunScope.Includes(scope, 999));
    }

    [Fact]
    public void NonEmptyScope_IncludesOnlyItsMembers()
    {
        var scope = new List<int> { 2, 5 };

        Assert.True(PayrollRunScope.Includes(scope, 2));
        Assert.True(PayrollRunScope.Includes(scope, 5));
        Assert.False(PayrollRunScope.Includes(scope, 1));
    }

    [Fact]
    public void ParseCodes_SplitsExcelPasteAndDedupes()
    {
        // عمود إكسل ملصوق: أسطر + تاب + فواصل + فراغات + تكرار
        var codes = PayrollRunScope.ParseCodes("E-100\r\nE-200\tE-300 , E-100;\n\n E-400 ");

        Assert.Equal(new[] { "E-100", "E-200", "E-300", "E-400" }, codes);
    }

    [Fact]
    public void ParseCodes_EmptyInput_YieldsNothing()
    {
        Assert.Empty(PayrollRunScope.ParseCodes(null));
        Assert.Empty(PayrollRunScope.ParseCodes("  \r\n , ; \t "));
    }

    [Fact]
    public void MatchCodes_IsCaseAndWhitespaceInsensitive()
    {
        var (ids, missing) = PayrollRunScope.MatchCodes(new[] { " e-100 ", "E-300" }, Map);

        Assert.Equal(new[] { 1, 3 }, ids);
        Assert.Empty(missing);
    }

    [Fact]
    public void MatchCodes_ReturnsMissingCodesAsText_NotJustACount()
    {
        var (ids, missing) = PayrollRunScope.MatchCodes(
            PayrollRunScope.ParseCodes("E-100, E-999, E-200, X1"), Map);

        Assert.Equal(new[] { 1, 2 }, ids);
        Assert.Equal(new[] { "E-999", "X1" }, missing);
    }

    [Fact]
    public void MatchCodes_DoesNotRepeatAnEmployeeMatchedTwice()
    {
        var (ids, _) = PayrollRunScope.MatchCodes(new[] { "E-100", "e-100" }, Map);

        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public void OutsideCandidates_FlagsScopeMembersThatCannotBeCalculated()
    {
        // موظف بالنطاق لكنه غير نشط/محذوف ⟹ لن يظهر بأي قسيمة؛ يجب أن يُعلَن
        var outside = PayrollRunScope.OutsideCandidates(new[] { 1, 2, 7 }, new[] { 1, 2, 3 });

        Assert.Equal(new[] { 7 }, outside);
    }

    [Fact]
    public void OutsideCandidates_EmptyScope_FlagsNothing()
    {
        Assert.Empty(PayrollRunScope.OutsideCandidates(Array.Empty<int>(), new[] { 1, 2 }));
    }

    [Fact]
    public void UnknownMode_FallsBackToAll_TheWidestScope()
    {
        Assert.Equal(PayrollRunScope.ModeAll, PayrollRunScope.NormalizeMode(null));
        Assert.Equal(PayrollRunScope.ModeAll, PayrollRunScope.NormalizeMode(""));
        Assert.Equal(PayrollRunScope.ModeAll, PayrollRunScope.NormalizeMode("Nonsense"));
        Assert.Equal(PayrollRunScope.ModePaste, PayrollRunScope.NormalizeMode("Paste"));
    }

    [Fact]
    public void Describe_ZeroMembers_ReadsAsAll_RegardlessOfMode()
    {
        Assert.Equal("كل الموظفين", PayrollRunScope.Describe(PayrollRunScope.ModeManual, 0));
        Assert.Equal("كل الموظفين", PayrollRunScope.Describe(PayrollRunScope.ModeAll, 0));
        Assert.Contains("لصق أكواد", PayrollRunScope.Describe(PayrollRunScope.ModePaste, 12));
    }
}
