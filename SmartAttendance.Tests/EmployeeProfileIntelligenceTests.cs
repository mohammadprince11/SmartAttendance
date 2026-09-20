using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Domain.Enums;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeProfileIntelligenceTests
{
    [Fact]
    public void Evaluate_CompleteProfile_ReturnsFullScoreAndFactsOnlySummary()
    {
        var result = EmployeeProfileIntelligence.Evaluate(
            new EmployeeProfileIntelligenceInput(
                "E-100",
                "Synthetic Candidate",
                true,
                "199276728473",
                null,
                new DateOnly(1990, 1, 2),
                "Iraqi",
                "Male",
                "ZYNORA",
                "HQ",
                "Human Resources",
                "HR Specialist",
                new DateOnly(2022, 1, 1),
                "Full Time",
                "Active",
                "+9647701234567",
                "work@example.test",
                "personal@example.test",
                [
                    R(EmployeeRecordType.Address, "Primary Address", "Baghdad"),
                    R(EmployeeRecordType.Experience, "ZYNORA", "HR Specialist"),
                    R(EmployeeRecordType.Education, "University of Baghdad", "BBA"),
                    R(EmployeeRecordType.Skill, "Payroll"),
                    R(EmployeeRecordType.Skill, "Attendance"),
                    R(EmployeeRecordType.Language, "Arabic"),
                    R(EmployeeRecordType.Language, "English")
                ]));

        Assert.Equal(100, result.CompletenessScore);
        Assert.Equal(20, result.CompletedItems);
        Assert.Empty(result.MissingItems);
        Assert.Contains("Synthetic Candidate", result.SmartSummary);
        Assert.Contains("HR Specialist", result.SmartSummary);
        Assert.Contains("Payroll", result.SmartSummary);
        Assert.Contains("Arabic", result.SmartSummary);
    }

    [Fact]
    public void Evaluate_MissingProfile_ReturnsTransparentMissingItems()
    {
        var result = EmployeeProfileIntelligence.Evaluate(
            new EmployeeProfileIntelligenceInput(
                "E-200",
                "Incomplete Candidate",
                false,
                null,
                null,
                null,
                null,
                null,
                "ZYNORA",
                "HQ",
                null,
                null,
                new DateOnly(2026, 1, 1),
                null,
                "Active",
                null,
                null,
                null,
                []));

        Assert.True(result.CompletenessScore < 100);
        Assert.Contains(
            result.MissingItems,
            item => item.Contains("جواز"));
        Assert.Contains(
            result.MissingItems,
            item => item.Contains("المهارات"));
        Assert.Contains(
            result.MissingItems,
            item => item.Contains("اللغات"));
    }

    [Fact]
    public void Evaluate_DeduplicatesSkillsAndLanguages()
    {
        var result = EmployeeProfileIntelligence.Evaluate(
            new EmployeeProfileIntelligenceInput(
                "E-300",
                "Candidate",
                true,
                "N-1",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [
                    R(EmployeeRecordType.Skill, "Payroll"),
                    R(EmployeeRecordType.Skill, "payroll"),
                    R(EmployeeRecordType.Language, "Arabic"),
                    R(EmployeeRecordType.Language, "arabic")
                ]));

        Assert.Single(result.Skills);
        Assert.Single(result.Languages);
    }

    private static EmployeeProfileRecordFact R(
        EmployeeRecordType type,
        string title,
        string? subtitle = null) =>
        new(
            type,
            title,
            subtitle,
            null,
            null,
            true);
}
