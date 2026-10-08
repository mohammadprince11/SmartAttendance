using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.DisciplinaryRules;

namespace SmartAttendance.Tests;

public sealed class DisciplinaryArticlesTests
{
    [Fact]
    public void EmptyDocument_IsEmptyWithoutAnyPreset() => Assert.Empty(DisciplinaryArticles.Parse(null).Articles);

    [Fact]
    public void EmptyRows_AreIgnoredAndTextIsTrimmed()
    {
        var rows = DisciplinaryArticles.Validate([" 1 ", " "], [" عنوان ", ""], [" نص \nثانٍ ", ""]);
        Assert.Equal(new DisciplinaryArticles.Article("1", "عنوان", "نص \nثانٍ"), Assert.Single(rows));
    }

    [Theory]
    [InlineData("", "عنوان", "نص")]
    [InlineData("1", "", "نص")]
    [InlineData("1", "عنوان", "")]
    public void PartialRows_AreRejected(string number, string title, string text) =>
        Assert.Throws<ArgumentException>(() => DisciplinaryArticles.Validate([number], [title], [text]));

    [Fact]
    public void MismatchedArrays_AreRejected() =>
        Assert.Throws<ArgumentException>(() => DisciplinaryArticles.Validate(["1"], [], []));

    [Fact]
    public void DuplicatedNumbers_AreRejected() =>
        Assert.Throws<ArgumentException>(() => DisciplinaryArticles.Validate(["1", " 1 "], ["أ", "ب"], ["أ", "ب"]));

    [Theory]
    [InlineData(61, 1, 1)]
    [InlineData(1, 181, 1)]
    [InlineData(1, 1, 4001)]
    public void LengthLimits_AreEnforced(int n, int t, int text) =>
        Assert.Throws<ArgumentException>(() => DisciplinaryArticles.Validate([new string('n', n)], [new string('t', t)], [new string('x', text)]));

    [Fact]
    public void RowLimit_IsEnforced() => Assert.Throws<ArgumentException>(() =>
        DisciplinaryArticles.Validate(Enumerable.Range(1, 101).Select(x => x.ToString()).ToArray(),
            Enumerable.Repeat("عنوان", 101).ToArray(), Enumerable.Repeat("نص", 101).ToArray()));

    [Fact]
    public void FullDocument_WithExtraBlankInputRow_IsAccepted()
    {
        var rows = DisciplinaryArticles.Validate(Enumerable.Range(1, 100).Select(x => x.ToString()).Append("").ToArray(),
            Enumerable.Repeat("عنوان", 100).Append("").ToArray(), Enumerable.Repeat("نص", 100).Append("").ToArray());
        Assert.Equal(100, rows.Count);
    }

    [Fact]
    public void TenantKeys_AreDistinctAndNoGlobalFallbackExists()
    {
        Assert.NotEqual(DisciplinaryArticles.SettingKey(1), DisciplinaryArticles.SettingKey(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisciplinaryArticles.SettingKey(0));
    }

    [Fact]
    public void ContentVersion_DetectsChanges()
    {
        Assert.Equal(DisciplinaryArticles.Version(null), DisciplinaryArticles.Version(""));
        Assert.NotEqual(DisciplinaryArticles.Version("[]"), DisciplinaryArticles.Version("[ ]"));
        Assert.Equal(64, DisciplinaryArticles.Version("[]").Length);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void CorruptStoredData_IsNotSilentlyReplaced(string value) =>
        Assert.ThrowsAny<Exception>(() => DisciplinaryArticles.Parse(value));

    [Fact]
    public void Html_RemainsPlainTextForRazorEncoding()
    {
        var row = Assert.Single(DisciplinaryArticles.Validate(["1"], ["<b>عنوان</b>"], ["<script>ignored()</script>"]));
        Assert.Equal("<script>ignored()</script>", row.Text);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task DeniedRequests_DoNotTouchDatabase(bool authenticated, bool admin, bool tenant)
    {
        var claims = new List<Claim>();
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        if (tenant) claims.Add(new Claim(TenantContext.TenantIdClaimType, "1"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null)) };
        var model = new IndexModel(null!, null!, new DeniedScope()) { PageContext = new PageContext { HttpContext = context } };
        var result = await model.OnPostSaveArticlesAsync([], [], [], "");
        if (authenticated) Assert.IsType<ForbidResult>(result);
        else Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public void OldSeedingEndpoints_AreGoneWithoutAnyWrite()
    {
        var model = new IndexModel(null!, null!, null!);
        Assert.Equal(410, Assert.IsType<StatusCodeResult>(model.OnPostAddSample()).StatusCode);
        Assert.Equal(410, Assert.IsType<StatusCodeResult>(model.OnPostSeedLibrary()).StatusCode);
        Assert.Equal(410, Assert.IsType<StatusCodeResult>(model.OnPostMergeDuplicates()).StatusCode);
    }

    private sealed class DeniedScope : ICompanyScopeProvider
    {
        public Task<CompanyScope> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(CompanyScope.DeniedAll());
    }
}
