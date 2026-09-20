using SmartAttendance.Application.PeopleAi;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class CvIntelligenceParserTests
{
    [Fact]
    public void Parse_EnglishCv_ExtractsProfileAndStructuredRecords()
    {
        var result = CvIntelligenceParser.Parse(
        [
            L("Mohammed Ali Zaidan"),
            L("+964 770 123 4567"),
            L("candidate@example.com"),
            L("Address: Baghdad, Iraq"),
            L("Nationality: Iraqi"),
            L("Experience"),
            L("HR Specialist at ZYNORA"),
            L("2022 - Present"),
            L("Payroll Officer at Example Group"),
            L("2019 - 2022"),
            L("Education"),
            L("Bachelor of Business Administration"),
            L("University of Baghdad"),
            L("2015 - 2019"),
            L("Certificates"),
            L("SHRM-CP Certificate"),
            L("Skills"),
            L("Payroll, Attendance, Power BI"),
            L("Languages"),
            L("Arabic, English")
        ]);

        Assert.Equal("Mohammed Ali Zaidan", result.FullName);
        Assert.Equal("Baghdad, Iraq", result.Address);
        Assert.Equal("Iraqi", result.Nationality);
        Assert.Contains("Payroll", result.Skills);
        Assert.Contains("Power BI", result.Skills);
        Assert.Contains("Arabic", result.Languages);
        Assert.Contains("English", result.Languages);

        var experience = result.Records
            .Where(x => x.RecordType == "Experience")
            .ToList();
        Assert.Equal(2, experience.Count);
        Assert.Equal("ZYNORA", experience[0].Title);
        Assert.Equal("HR Specialist", experience[0].Subtitle);
        Assert.True(experience[0].IsCurrent);
        Assert.Equal(new DateOnly(2022, 1, 1), experience[0].FromDate);

        var education = Assert.Single(
            result.Records.Where(x => x.RecordType == "Education"));
        Assert.Equal("University of Baghdad", education.Title);
        Assert.Equal(
            "Bachelor of Business Administration",
            education.Subtitle);

        var certificate = Assert.Single(
            result.Records.Where(x => x.RecordType == "Certificate"));
        Assert.Contains("SHRM", certificate.Title);
    }

    [Fact]
    public void Parse_ArabicCv_RecognizesSectionsAndLabels()
    {
        var result = CvIntelligenceParser.Parse(
        [
            L("محمد علي زيدان"),
            L("العنوان: بغداد - العراق"),
            L("الجنسية: عراقي"),
            L("الخبرات العملية"),
            L("أخصائي موارد بشرية - شركة زينورا"),
            L("2021 - 2025"),
            L("المؤهلات العلمية"),
            L("بكالوريوس إدارة أعمال"),
            L("جامعة بغداد"),
            L("2016 - 2020"),
            L("المهارات"),
            L("الرواتب، الحضور، التقارير"),
            L("اللغات"),
            L("العربية، الإنجليزية")
        ]);

        Assert.Equal("محمد علي زيدان", result.FullName);
        Assert.Equal("بغداد - العراق", result.Address);
        Assert.Equal("عراقي", result.Nationality);
        Assert.NotEmpty(result.Records);
        Assert.Contains(
            result.Records,
            x => x.RecordType == "Experience");
        Assert.Contains(
            result.Records,
            x => x.RecordType == "Education");
    }

    [Fact]
    public void Parse_DoesNotTreatContactLinesAsName()
    {
        var result = CvIntelligenceParser.Parse(
        [
            L("candidate@example.com"),
            L("+9647701234567"),
            L("www.example.com"),
            L("Synthetic Candidate")
        ]);

        Assert.Equal("Synthetic Candidate", result.FullName);
    }

    [Fact]
    public void Parse_CommonResumeLayout_ExtractsReusableSections()
    {
        var result = CvIntelligenceParser.Parse(
        [
            L("SYNTHETIC CANDIDATE"),
            L("C o n t a c t"),
            L("+964 770 000 0000"),
            L("candidate@example.com"),
            L("Baghdad"),
            L("1990-01-15"),
            L("Iraq"),
            L("P r o f e s s i o n a l S u m m a r y"),
            L("HR operations professional with payroll and analytics experience."),
            L("S k i l l s"),
            L("Payroll • Attendance • Power BI"),
            L("E x p e r i e n c e"),
            L("HR OPERATIONS SUPERVISOR"),
            L("EXAMPLE GROUP — Baghdad"),
            L("Feb 2025 - Present"),
            L("Managed payroll and attendance operations."),
            L("HR OFFICER"),
            L("SECOND COMPANY — Baghdad"),
            L("Apr 2022 - Feb 2024"),
            L("Processed employee records."),
            L("E d u c a t i o n"),
            L("BACHELOR'S"),
            L("Sep 2018 | EXAMPLE UNIVERSITY COLLEGE"),
            L("L a n g u a g e s"),
            L("Arabic"),
            L("Native"),
            L("English"),
            L("Intermediate")
        ]);

        Assert.Equal("Baghdad, Iraq", result.Address);
        Assert.Contains("payroll and analytics", result.ProfessionalSummary);
        Assert.Contains("Power BI", result.Skills);
        Assert.Contains("Arabic (Native)", result.Languages);
        Assert.Contains("English (Intermediate)", result.Languages);

        var experience = result.Records
            .Where(x => x.RecordType == "Experience")
            .ToList();
        Assert.Equal(2, experience.Count);
        Assert.Equal("EXAMPLE GROUP", experience[0].Title);
        Assert.Equal("HR OPERATIONS SUPERVISOR", experience[0].Subtitle);
        Assert.Equal(new DateOnly(2025, 2, 1), experience[0].FromDate);
        Assert.True(experience[0].IsCurrent);
        Assert.Contains("Managed payroll", experience[0].Note);

        var education = Assert.Single(
            result.Records.Where(x => x.RecordType == "Education"));
        Assert.Equal("EXAMPLE UNIVERSITY COLLEGE", education.Title);
        Assert.Equal("BACHELOR'S", education.Subtitle);
        Assert.Equal(new DateOnly(2018, 9, 1), education.FromDate);
    }

    private static CvIntelligenceLine L(string text) =>
        new(text, 0.98);
}
