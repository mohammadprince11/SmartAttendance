using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// الموجة 5 — عزل المتاجر الباقية (القروض · الطلبات المالية · البصمة المفقودة ·
/// العقود · الإقرارات · الروستر). تحرس <b>الشرط المولَّد</b> و<b>حارس الملكية</b>
/// (الأجزاء النقيّة) — كلها تمرّ عبر <see cref="EmployeeCompanyGuard.ListFilter"/>
/// و<see cref="CompanyScope"/>، فما ثبت لواحد ثبت للبقية.
/// </summary>
public class RemainingStoresCompanyScopeTests
{
    [Fact]
    public void سرد_المقيَّد_يحصر_بشركاته()
    {
        Assert.Equal(
            "e.CompanyId IN (2, 3)",
            EmployeeCompanyGuard.ListFilter(CompanyScope.ForCompanies(new[] { 3, 2 }), "e.CompanyId"));
    }

    [Fact]
    public void سرد_المحروم_يمنع_كل_شيء()
    {
        // DeniedAll ⟹ المتاجر تعيد قائمة فارغة قبل الاستعلام، والشرط 1=0 احتياطاً.
        Assert.Equal("1 = 0", EmployeeCompanyGuard.ListFilter(CompanyScope.DeniedAll(), "e.CompanyId"));
        Assert.True(CompanyScope.DeniedAll().IsDeniedAll);
    }

    [Fact]
    public void سرد_غير_المقيَّد_لا_يقيّد()
    {
        // الأدمن: 1=1 فلا يتغيّر أي استعلام سرد قائم.
        Assert.Equal("1 = 1", EmployeeCompanyGuard.ListFilter(CompanyScope.Unrestricted(), "e.CompanyId"));
    }

    /// <summary>
    /// جداول حرّاس الملكية المضافة بهذه الموجة موجودة بالكتالوج الثابت — أي خطأ
    /// إملائي باسم جدول كان سيُرفَع استثناءً وقت التشغيل لا يُكتشف إلا متأخراً.
    /// </summary>
    [Theory]
    [InlineData(EmployeeCompanyGuard.Tables.MissingPunchRequests)]
    [InlineData(EmployeeCompanyGuard.Tables.EmployeeContracts)]
    [InlineData(EmployeeCompanyGuard.Tables.EmployeeLoans)]
    [InlineData(EmployeeCompanyGuard.Tables.SelfServiceRequests)]
    public void أسماء_جداول_الحرّاس_معرّفات_صالحة(string table)
    {
        // لا يرمي ⟹ معرّف SQL صالح (حروف/أرقام/شرطة سفلية فقط).
        EmployeeCompanyGuard.GuardIdentifier(table);
    }

    /// <summary>
    /// جمهور الإقرارات: «أرسل للجميع» = جميعُ نطاق المُرسِل لا جميعُ النظام —
    /// نفس درس إشعارات الحضور (P0-5). صفٌّ بشركةٍ خارج النطاق لا يدخل الجمهور.
    /// </summary>
    [Fact]
    public void جمهور_الإقرار_محصور_بالنطاق()
    {
        var scope = CompanyScope.ForCompanies(new[] { 1 });

        Assert.True(scope.Allows(1));
        Assert.False(scope.Allows(2));
        Assert.False(scope.Allows(null));   // صفّ بلا شركة لا يُرسَل له بالمقيَّد
    }
}
