using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Pages.HrSettings;

namespace SmartAttendance.Tests;

public class HrLookupCatalogTests
{
    [Fact]
    public void PeopleReferenceListsExcludeLegacySalaryItems()
    {
        var page = new LookupsModel(null!);

        Assert.DoesNotContain(page.Categories, category => category.Key == "salaryitems");
        Assert.Equal(
            new[] { "religions", "worktypes", "grades", "sponsors", "assettypes", "nationalities", "contracttypes" },
            page.Categories.Select(category => category.Key));
        Assert.Same(HrLookups.Categories, page.Categories);
    }
}
