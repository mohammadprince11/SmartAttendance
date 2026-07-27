using SmartAttendance.Web.Infrastructure.Imports;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>اختبارات محلّل استيراد «بريد الموظفين فقط» — لصق Excel/CSV بعمودَين. نقي.</summary>
public class EmployeeEmailImportParserTests
{
    [Fact]
    public void Parse_TabSeparated_Valid()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("1001\tahmed@company.com\n1002\tsara@company.com");

        Assert.Equal(2, valid.Count);
        Assert.Empty(rejected);
        Assert.Equal("1001", valid[0].EmployeeNo);
        Assert.Equal("ahmed@company.com", valid[0].Email);
    }

    [Fact]
    public void Parse_CommaAndSemicolonSeparated_Valid()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("1001,a@b.com\n1002;c@d.org");

        Assert.Equal(2, valid.Count);
        Assert.Empty(rejected);
    }

    [Fact]
    public void Parse_HeaderRow_Skipped()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("رقم الموظف\tالبريد الإلكتروني\n1001\ta@b.com");

        Assert.Single(valid);
        Assert.Empty(rejected);
    }

    [Fact]
    public void Parse_InvalidEmail_Rejected()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("1001\tnot-an-email\n1002\tuser@host");

        Assert.Empty(valid);
        Assert.Equal(2, rejected.Count);
    }

    [Fact]
    public void Parse_MissingColumn_Rejected()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("1001");

        Assert.Empty(valid);
        Assert.Single(rejected);
    }

    [Fact]
    public void Parse_DuplicateEmployeeNo_LastWins()
    {
        var (valid, _) = EmployeeEmailImportParser.Parse("1001\told@company.com\n1001\tnew@company.com");

        Assert.Single(valid);
        Assert.Equal("new@company.com", valid[0].Email);
    }

    [Fact]
    public void Parse_EmptyInput_Empty()
    {
        var (valid, rejected) = EmployeeEmailImportParser.Parse("  \n ");

        Assert.Empty(valid);
        Assert.Empty(rejected);
    }

    [Theory]
    [InlineData("ahmed@company.com", true)]
    [InlineData("a.b+c@sub.domain.org", true)]
    [InlineData("user@host", false)]
    [InlineData("user with space@x.com", false)]
    [InlineData("", false)]
    public void IsValidEmail_Cases(string email, bool expected)
    {
        Assert.Equal(expected, EmployeeEmailImportParser.IsValidEmail(email));
    }
}
