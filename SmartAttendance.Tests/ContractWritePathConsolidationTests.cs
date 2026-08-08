using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// توحيد مسار كتابة العقود (People — Task 1).
///
/// <para><b>ما كان</b>: <c>Employees/Profile.Panels.cshtml.cs</c> كان **محرّكاً ثانياً**
/// يكتب العقود بـEF مباشرةً: <c>EmployeeContracts.Add</c> + حلقةٌ تعيد تطبيق ثابت
/// «العقد الحالي الواحد» — لكن بلا <c>PreviousContractId</c>/<c>MovementKind</c>،
/// فكانت العقود تُفقد نسب التجديد/التمديد الذي يقرّر الخدمة المتصلة والمال.</para>
///
/// <para><b>القرار</b>: المحرّك الرسمي الوحيد للكتابة =
/// <see cref="ContractRegisterStore"/>؛ الشاشة سطح إدخالٍ يوجّه الكتابة إليه. هذه
/// الاختبارات حرّاسٌ نصّيّون/انعكاسيّون يمنعون عودة المحرّك الثاني — تعمل بكل بناء
/// بلا قاعدة بيانات (فالقاعدة مشتركة تطوير/إنتاج، ولا يُلمَس عقدٌ حقيقي).</para>
///
/// دلالة التجديد/التمديد نفسها محروسة نقيّاً بـ<see cref="ContractPolicyTests"/>.
/// </summary>
public class ContractWritePathConsolidationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>جسم <c>OnPostSaveContractAsync</c> وحده — من توقيعه حتى المعالج التالي.</summary>
    private static string SaveContractHandlerBody()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Pages", "Employees", "Profile.Panels.cshtml.cs"));

        var start = source.IndexOf("OnPostSaveContractAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "المعالج «OnPostSaveContractAsync» غير موجود — حُذف أو أُعيدت تسميته.");

        // نهاية الجسم = بداية المعالج التالي (حذف العقد).
        var end = source.IndexOf("OnPostDeleteContractAsync", start, StringComparison.Ordinal);
        Assert.True(end > start, "المعالج التالي «OnPostDeleteContractAsync» غير موجود.");

        return source[start..end];
    }

    // ═══ الحارس النصّيّ: الشاشة توجّه للمحرّك ولا تكتب العقود بنفسها ═══

    [Theory]
    [InlineData("ContractRegisterStore.AddContractAsync")]
    [InlineData("ContractRegisterStore.UpdateContractAsync")]
    public void SaveContract_DelegatesToTheOfficialStore(string call)
    {
        Assert.Contains(call, SaveContractHandlerBody());
    }

    /// <summary>
    /// المحرّك الثاني كان ينشئ صفّ العقد ويكتبه بـEF مباشرةً. عودة أيٍّ من هذه
    /// الأنماط = عودة محرّكٍ موازٍ يتخطّى ثوابت <see cref="ContractRegisterStore"/>.
    /// </summary>
    [Theory]
    [InlineData("EmployeeContracts.Add(")]      // إدراج صفّ عقد مباشر
    [InlineData("new EmployeeContract")]        // بناء كيان العقد للكتابة
    [InlineData("SaveChangesAsync")]            // حفظ EF لصفّ العقد
    public void SaveContract_HasNoSecondWriteEngine(string forbidden)
    {
        Assert.DoesNotContain(forbidden, SaveContractHandlerBody());
    }

    /// <summary>
    /// تعديلٌ بمعرّف عقدٍ من النموذج يجب أن يُفحَص أنه يخصّ هذا الموظف قبل الكتابة —
    /// وإلا عدّل مستخدمٌ عقد موظفٍ آخر بتزوير المعرّف (نظير عزل شركة A عن عقد B).
    /// </summary>
    [Fact]
    public void SaveContract_VerifiesContractBelongsToEmployee_BeforeUpdate()
    {
        var body = SaveContractHandlerBody();
        Assert.Contains("x.EmployeeId == Id", body);
    }

    // ═══ الحارس الانعكاسيّ: توقيع المحرّك يحمل ما تحتاجه الشاشة ═══

    private static MethodInfo StoreMethod(string name) =>
        typeof(ContractRegisterStore).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new Xunit.Sdk.XunitException($"«{name}» غير موجود على ContractRegisterStore.");

    /// <summary>
    /// <c>AddContractAsync</c> يقبل المرفق — فالشاشة توجّه رفع ملف العقد للمحرّك بدل
    /// كتابته بنفسها، ويعيد معرّف العقد (لحقول مخصّصة).
    /// </summary>
    [Fact]
    public void AddContract_AcceptsAttachment_AndReturnsContractId()
    {
        var method = StoreMethod("AddContractAsync");
        var parameters = method.GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("attachmentName", parameters);
        Assert.Contains("attachmentPath", parameters);
        Assert.Equal(typeof(Task<int>), method.ReturnType);
    }

    /// <summary>
    /// <c>UpdateContractAsync</c> يملك ثابت «العقد الحالي الواحد» (<c>makeCurrent</c>)
    /// والمرفق — فلا تعيد الشاشة تطبيقهما. و<c>makeCurrent</c> اختياريٌّ (<c>bool?</c>)
    /// كي يبقى سلوك سجلّ العقود (عدم تغيير الصفة) عند عدم تمريره.
    /// </summary>
    [Fact]
    public void UpdateContract_OwnsCurrentInvariant_AndAttachment_Optionally()
    {
        var method = StoreMethod("UpdateContractAsync");
        var byName = method.GetParameters().ToDictionary(p => p.Name!, p => p);

        Assert.True(byName.ContainsKey("makeCurrent"), "makeCurrent مفقود.");
        Assert.Equal(typeof(bool?), byName["makeCurrent"].ParameterType);
        Assert.True(byName["makeCurrent"].IsOptional, "makeCurrent يجب أن يكون اختيارياً (لا يكسر سجلّ العقود).");
        Assert.True(byName.ContainsKey("attachmentName"), "attachmentName مفقود.");
        Assert.True(byName.ContainsKey("attachmentPath"), "attachmentPath مفقود.");
    }

    /// <summary>
    /// التوسعة توافقيّة: مستهلك سجلّ العقود القائم (<c>Contracts/Index.OnPostSaveAsync</c>)
    /// ما زال ينادي المحرّك — لم يُكسَر توقيعٌ بإضافة المعطيات الاختيارية.
    /// </summary>
    [Theory]
    [InlineData("ContractRegisterStore.UpdateContractAsync")]
    [InlineData("ContractRegisterStore.AddContractAsync")]
    public void ContractsRegisterPage_StillUsesTheStore(string call)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Pages", "Contracts", "Index.cshtml.cs"));
        Assert.Contains(call, source);
    }
}
