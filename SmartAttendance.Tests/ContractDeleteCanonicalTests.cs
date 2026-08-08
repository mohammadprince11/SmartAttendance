using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// توجيه حذف العقود عبر المسار القانونيّ (People — إغلاق توحيد كتابة العقود).
///
/// <para>كان <c>Profile.Panels.OnPostDeleteContractAsync</c> يحذف العقد بـEF مباشرةً
/// (<c>IsDeleted = true</c> ثمّ <c>SaveChangesAsync</c>) — محرّك حذفٍ ثانٍ يتخطّى
/// <see cref="ContractRegisterStore"/>: بلا نطاق، بلا ثابت العقد الحاليّ الواحد، بلا
/// مزامنة الحقول المسطّحة. الآن الإضافة والتعديل والحذف كلها تمرّ بالمتجر القانونيّ.</para>
///
/// حرّاسٌ نصّيّون/انعكاسيّون يعملون بكل بناء (القاعدة مشتركة تطوير/إنتاج، فحذفٌ حيّ
/// كان سيقلب العقد الحاليّ لموظفين حقيقيين — لا يُختبَر بالطفرة).
/// </summary>
public class ContractDeleteCanonicalTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "SmartAttendance.Web" }.Concat(parts).ToArray()));

    private static string StoreSource() =>
        ReadWeb("Infrastructure", "Hrms", "ContractRegisterStore.cs");

    /// <summary>جسم <c>DeleteContractAsync</c> وحده — من توقيعه حتى «── الحركات ──».</summary>
    private static string DeleteMethodBody()
    {
        var src = StoreSource();
        var at = src.IndexOf("public static async Task<bool> DeleteContractAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "التوقيع القانونيّ لـDeleteContractAsync غير موجود.");
        var end = src.IndexOf("── الحركات", at, StringComparison.Ordinal);
        return src[at..(end > at ? end : src.Length)];
    }

    private static string ProfileDeleteHandler()
    {
        var src = ReadWeb("Pages", "Employees", "Profile.Panels.cshtml.cs");
        var at = src.IndexOf("OnPostDeleteContractAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "معالج OnPostDeleteContractAsync غير موجود.");
        // نهاية الجسم = بداية العضو التالي (class ContractInput أو دالة تالية).
        var end = src.IndexOf("public class ContractInput", at, StringComparison.Ordinal);
        return src[at..(end > at ? end : Math.Min(src.Length, at + 1400))];
    }

    // ═══ توقيع المتجر القانونيّ ═══

    [Fact]
    public void DeleteContract_HasCanonicalSignature_ScopeReturnsBool_WithExpectedEmployee()
    {
        var method = typeof(ContractRegisterStore).GetMethod(
            "DeleteContractAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method!.ReturnType);

        var byName = method.GetParameters().ToDictionary(p => p.Name!, p => p);
        Assert.Contains("scope", byName.Keys);                 // النطاق يُفرَض بالمتجر
        Assert.Contains("expectedEmployeeId", byName.Keys);    // تحقّق ملكية العقد للموظف
        Assert.True(byName["expectedEmployeeId"].IsOptional);  // اختياريّ (سجلّ العقود لا يمرّره)
    }

    // ═══ سلوك المتجر: نطاق · حذف ناعم · idempotency · معاملة · ثابت الحاليّ ═══

    [Fact]
    public void DeleteContract_EnforcesScope_BeforeMutation()
    {
        var body = DeleteMethodBody();
        var guard = body.IndexOf("CanAccessOwnedRowAsync", StringComparison.Ordinal);
        var mutate = body.IndexOf("SET IsDeleted = 1", StringComparison.Ordinal);
        Assert.True(guard > 0, "لا فحص نطاق قبل الحذف.");
        Assert.True(mutate > guard, "الحذف يقع قبل فحص النطاق.");
    }

    [Fact]
    public void DeleteContract_IsSoftDelete_NeverPhysical()
    {
        var body = DeleteMethodBody();
        Assert.Contains("SET IsDeleted = 1", body);                 // ناعم: الصفّ يبقى تاريخاً
        Assert.DoesNotContain("DELETE FROM EmployeeContracts", body); // لا محوٌ فعليّ
    }

    [Fact]
    public void DeleteContract_IsIdempotent_OnAlreadyDeleted()
    {
        var body = DeleteMethodBody();
        Assert.Contains("if (info.IsDeleted) return true;", body);
    }

    [Fact]
    public void DeleteContract_WrapsWritesInTransaction()
    {
        var body = DeleteMethodBody();
        Assert.Contains("BeginTransactionAsync", body);
        Assert.Contains("CommitAsync", body);
    }

    /// <summary>
    /// عند حذف الحاليّ: يُرقَّى أحدثُ عقدٍ باقٍ (ORDER BY FromDate DESC) وتُزامَن الحقول
    /// المسطّحة؛ وإن لم يبقَ عقدٌ تُصفَّر (NULL) — كلاهما مشروطٌ بـ<c>info.IsCurrent</c>.
    /// </summary>
    [Fact]
    public void DeleteContract_HandlesCurrentInvariant_PromoteOrClearFlattened()
    {
        var body = DeleteMethodBody();
        Assert.Contains("if (info.IsCurrent)", body);
        Assert.Contains("ORDER BY FromDate DESC, Id DESC", body);          // اختيار الحاليّ التالي
        Assert.Contains("UPDATE EmployeeContracts SET IsCurrent = 1", body); // ترقيته
        Assert.Contains("UPDATE Employees SET ContractType = @ContractType", body); // مزامنة
        Assert.Contains("ContractType = NULL, ContractEndDate = NULL", body);       // أو تصفير
    }

    // ═══ معالج الملف: يوجّه ولا يكتب ═══

    [Fact]
    public void ProfileDelete_DelegatesToCanonicalStore_WithEmployeeOwnership()
    {
        var handler = ProfileDeleteHandler();
        Assert.Contains("ContractRegisterStore.DeleteContractAsync", handler);
        Assert.Contains("expectedEmployeeId: Id", handler);   // يتحقّق أن العقد لهذا الموظف
    }

    [Theory]
    [InlineData("SaveChangesAsync")]        // لا حفظ EF لحذف العقد
    [InlineData("IsDeleted = true")]        // لا وسمٌ مباشر
    [InlineData("EmployeeContracts.Remove")]
    public void ProfileDelete_HasNoDirectContractWrite(string forbidden)
    {
        Assert.DoesNotContain(forbidden, ProfileDeleteHandler());
    }

    // ═══ سجلّ العقود يبقى على المسار نفسه ═══

    [Fact]
    public void ContractsRegisterPage_StillDeletesThroughTheStore()
    {
        var src = ReadWeb("Pages", "Contracts", "Index.cshtml.cs");
        Assert.Contains("ContractRegisterStore.DeleteContractAsync", src);
    }
}
