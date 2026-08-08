using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس فصل البيئات — الموجة 0 من إعادة هندسة العزل.
///
/// السلسلة التي أوقعت الحادث ومُثبتة بالكود:
/// <c>launchSettings.json</c> يضبط <c>ASPNETCORE_ENVIRONMENT=Development</c>،
/// و<c>appsettings.Development.json</c> كان يشير لـ<c>Database=SmartAttendance</c>
/// وهي قاعدة الإنتاج ⟹ كل <c>dotnet run</c> محلي يكتب على بيانات حقيقية ويشغّل
/// <see cref="SqlSchemaMigrator"/> عليها.
/// </summary>
public class EnvironmentDatabaseGuardTests
{
    private const string Prod = "Server=localhost;Database=SmartAttendance;Trusted_Connection=True";
    private const string Dev = "Server=localhost;Database=SmartAttendance_Dev;Trusted_Connection=True";

    /// <summary>🔴 الحالة التي وقعت فعلاً — يجب أن تُرفض.</summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData(null)]
    public void بيئة_غير_إنتاجية_على_قاعدة_الإنتاج_تُرفض(string? environment)
    {
        var refusal = EnvironmentDatabaseGuard.Validate(environment, Prod);

        Assert.NotNull(refusal);
        Assert.Contains("SmartAttendance", refusal);
    }

    /// <summary>الإنتاج على قاعدة الإنتاج هو الوضع الطبيعي — لا يُمسّ.</summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void الإنتاج_على_قاعدته_يمرّ(string environment)
    {
        Assert.Null(EnvironmentDatabaseGuard.Validate(environment, Prod));
    }

    /// <summary>التطوير على قاعدة تطوير — وهو الهدف من الموجة 0.</summary>
    [Fact]
    public void التطوير_على_قاعدة_منفصلة_يمرّ()
    {
        Assert.Null(EnvironmentDatabaseGuard.Validate("Development", Dev));
    }

    /// <summary>
    /// اسم يبدأ باسم الإنتاج لا يساويه ⟹ لا يُرفض. المطابقة كاملة لا بادئة،
    /// وإلا رُفضت كل قاعدة تطوير مشتقّة الاسم.
    /// </summary>
    [Fact]
    public void المطابقة_كاملة_لا_بادئة()
    {
        Assert.Null(EnvironmentDatabaseGuard.Validate(
            "Development", "Server=localhost;Database=SmartAttendanceDB;Trusted_Connection=True"));
    }

    /// <summary>«Initial Catalog» مرادف لـ«Database» بنصوص الاتصال — يجب أن يُلتقط.</summary>
    [Fact]
    public void يلتقط_Initial_Catalog()
    {
        Assert.NotNull(EnvironmentDatabaseGuard.Validate(
            "Development", "Server=localhost;Initial Catalog=SmartAttendance;Integrated Security=true"));
    }

    /// <summary>حالة الأحرف باسم القاعدة لا تُفلت الحارس.</summary>
    [Fact]
    public void اسم_القاعدة_غير_حسّاس_لحالة_الأحرف()
    {
        Assert.NotNull(EnvironmentDatabaseGuard.Validate(
            "Development", "Server=localhost;Database=smartattendance;Trusted_Connection=True"));
    }

    /// <summary>
    /// نصّ لا يُفهم أو بلا اسم قاعدة **لا يمنع الإقلاع**: الحارس يمنع خطراً
    /// مُثبَتاً، ولا يقفل النظام على نفسه بسبب صيغةٍ لم يتعرّف عليها.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Server=localhost;Trusted_Connection=True")]
    public void نصّ_غامض_لا_يمنع_الإقلاع(string? connectionString)
    {
        Assert.Null(EnvironmentDatabaseGuard.Validate("Development", connectionString));
    }

    [Fact]
    public void استخراج_اسم_القاعدة()
    {
        Assert.Equal("SmartAttendance_Dev", EnvironmentDatabaseGuard.DatabaseName(Dev));
        Assert.Null(EnvironmentDatabaseGuard.DatabaseName(null));
    }
}
