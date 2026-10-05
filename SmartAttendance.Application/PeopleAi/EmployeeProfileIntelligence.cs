using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.PeopleAi;

public sealed record EmployeeProfileRecordFact(
    EmployeeRecordType Type,
    string Title,
    string? Subtitle,
    DateOnly? FromDate,
    DateOnly? ToDate,
    bool IsCurrent);

public sealed record EmployeeProfileIntelligenceInput(
    string? EmployeeNo,
    string? FullName,
    bool IsCitizen,
    string? NationalId,
    string? PassportNo,
    DateOnly? BirthDate,
    string? Nationality,
    string? Gender,
    string? CompanyName,
    string? BranchName,
    string? DepartmentName,
    string? Position,
    DateOnly? HireDate,
    string? WorkType,
    string? EmploymentStatus,
    string? Phone,
    string? Email,
    string? PersonalEmail,
    IReadOnlyList<EmployeeProfileRecordFact> Records);

public sealed record EmployeeProfileIntelligenceResult(
    int CompletenessScore,
    int CompletedItems,
    int TotalItems,
    IReadOnlyList<string> MissingItems,
    string SmartSummary,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages);

public static class EmployeeProfileIntelligence
{
    private sealed record Check(
        string Label,
        bool Complete);

    public static EmployeeProfileIntelligenceResult Evaluate(
        EmployeeProfileIntelligenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var records = input.Records ?? [];
        var address = records.Any(x =>
            x.Type == EmployeeRecordType.Address &&
            HasText(x.Title, x.Subtitle));
        var experience = records.Any(x =>
            x.Type == EmployeeRecordType.Experience &&
            HasText(x.Title));
        var education = records.Any(x =>
            x.Type == EmployeeRecordType.Education &&
            HasText(x.Title));
        var skills = records
            .Where(x =>
                x.Type == EmployeeRecordType.Skill &&
                HasText(x.Title))
            .Select(x => x.Title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var languages = records
            .Where(x =>
                x.Type == EmployeeRecordType.Language &&
                HasText(x.Title))
            .Select(x => x.Title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var checks = new List<Check>
        {
            new("\u0627\u0644\u0627\u0633\u0645 \u0627\u0644\u0643\u0627\u0645\u0644", HasText(input.FullName)),
            new("\u0631\u0642\u0645 \u0627\u0644\u0645\u0648\u0638\u0641", HasText(input.EmployeeNo)),
            new(
                input.IsCitizen
                    ? "\u0627\u0644\u0631\u0642\u0645 \u0627\u0644\u0648\u0637\u0646\u064a"
                    : "\u0631\u0642\u0645 \u062c\u0648\u0627\u0632 \u0627\u0644\u0633\u0641\u0631",
                input.IsCitizen
                    ? HasText(input.NationalId)
                    : HasText(input.PassportNo)),
            new("\u062a\u0627\u0631\u064a\u062e \u0627\u0644\u0645\u064a\u0644\u0627\u062f", input.BirthDate.HasValue),
            new("\u0627\u0644\u062c\u0646\u0633\u064a\u0629", HasText(input.Nationality)),
            new("\u0627\u0644\u062c\u0646\u0633", HasText(input.Gender)),
            new("\u0627\u0644\u0634\u0631\u0643\u0629", HasText(input.CompanyName)),
            new("\u0627\u0644\u0641\u0631\u0639", HasText(input.BranchName)),
            new("\u0627\u0644\u0642\u0633\u0645", HasText(input.DepartmentName)),
            new("\u0627\u0644\u0645\u0633\u0645\u0649 \u0627\u0644\u0648\u0638\u064a\u0641\u064a", HasText(input.Position)),
            new("\u062a\u0627\u0631\u064a\u062e \u0627\u0644\u062a\u0639\u064a\u064a\u0646", input.HireDate.HasValue),
            new("\u0646\u0648\u0639 \u0627\u0644\u0639\u0645\u0644", HasText(input.WorkType)),
            new("\u062d\u0627\u0644\u0629 \u0627\u0644\u062a\u0648\u0638\u064a\u0641", HasText(input.EmploymentStatus)),
            new("\u0631\u0642\u0645 \u0627\u0644\u0647\u0627\u062a\u0641", HasText(input.Phone)),
            new(
                "\u0627\u0644\u0628\u0631\u064a\u062f \u0627\u0644\u0625\u0644\u0643\u062a\u0631\u0648\u0646\u064a",
                HasText(input.Email, input.PersonalEmail)),
            new("\u0627\u0644\u0639\u0646\u0648\u0627\u0646", address),
            new("\u0627\u0644\u062e\u0628\u0631\u0629 \u0627\u0644\u0639\u0645\u0644\u064a\u0629", experience),
            new("\u0627\u0644\u0645\u0624\u0647\u0644 \u0627\u0644\u0639\u0644\u0645\u064a", education),
            new("\u0627\u0644\u0645\u0647\u0627\u0631\u0627\u062a", skills.Count > 0),
            new("\u0627\u0644\u0644\u063a\u0627\u062a", languages.Count > 0)
        };

        var completed = checks.Count(x => x.Complete);
        var score = (int)Math.Round(
            completed * 100d / checks.Count,
            MidpointRounding.AwayFromZero);
        var missing = checks
            .Where(x => !x.Complete)
            .Select(x => x.Label)
            .ToList();

        return new EmployeeProfileIntelligenceResult(
            score,
            completed,
            checks.Count,
            missing,
            BuildSummary(input, records, skills, languages),
            skills,
            languages);
    }

    private static string BuildSummary(
        EmployeeProfileIntelligenceInput input,
        IReadOnlyList<EmployeeProfileRecordFact> records,
        IReadOnlyList<string> skills,
        IReadOnlyList<string> languages)
    {
        var sentences = new List<string>();
        var name = Clean(input.FullName);

        if (name is not null)
        {
            var roleParts = new List<string>();
            if (HasText(input.Position))
            {
                roleParts.Add(
                    "\u0628\u0645\u0633\u0645\u0649 " +
                    input.Position!.Trim());
            }

            if (HasText(input.DepartmentName))
            {
                roleParts.Add(
                    "\u0641\u064a \u0642\u0633\u0645 " +
                    input.DepartmentName!.Trim());
            }

            if (HasText(input.CompanyName))
            {
                roleParts.Add(
                    "\u0636\u0645\u0646 " +
                    input.CompanyName!.Trim());
            }

            sentences.Add(
                roleParts.Count == 0
                    ? name + "."
                    : name + " " +
                      string.Join(" ", roleParts) + ".");
        }

        if (input.HireDate.HasValue)
        {
            sentences.Add(
                "\u062a\u0627\u0631\u064a\u062e \u0627\u0644\u062a\u0639\u064a\u064a\u0646 " +
                input.HireDate.Value.ToString("yyyy-MM-dd") + ".");
        }

        var experience = records
            .Where(x => x.Type == EmployeeRecordType.Experience)
            .OrderByDescending(x => x.IsCurrent)
            .ThenByDescending(x => x.ToDate)
            .ThenByDescending(x => x.FromDate)
            .FirstOrDefault();
        if (experience is not null)
        {
            var detail = Clean(experience.Subtitle);
            sentences.Add(
                "\u0622\u062e\u0631 \u062e\u0628\u0631\u0629 \u0645\u0633\u062c\u0644\u0629: " +
                experience.Title.Trim() +
                (detail is null ? "." : " - " + detail + "."));
        }

        var education = records
            .Where(x => x.Type == EmployeeRecordType.Education)
            .OrderByDescending(x => x.ToDate)
            .ThenByDescending(x => x.FromDate)
            .FirstOrDefault();
        if (education is not null)
        {
            var detail = Clean(education.Subtitle);
            sentences.Add(
                "\u0627\u0644\u0645\u0624\u0647\u0644 \u0627\u0644\u0645\u0633\u062c\u0644: " +
                education.Title.Trim() +
                (detail is null ? "." : " - " + detail + "."));
        }

        if (skills.Count > 0)
        {
            sentences.Add(
                "\u0627\u0644\u0645\u0647\u0627\u0631\u0627\u062a \u0627\u0644\u0645\u0633\u062c\u0644\u0629: " +
                string.Join(
                    "\u060c ",
                    skills.Take(5)) + ".");
        }

        if (languages.Count > 0)
        {
            sentences.Add(
                "\u0627\u0644\u0644\u063a\u0627\u062a \u0627\u0644\u0645\u0633\u062c\u0644\u0629: " +
                string.Join(
                    "\u060c ",
                    languages) + ".");
        }

        if (sentences.Count == 0)
        {
            return "\u0644\u0627 \u062a\u0648\u062c\u062f \u0628\u064a\u0627\u0646\u0627\u062a \u0643\u0627\u0641\u064a\u0629 \u0644\u0625\u0646\u0634\u0627\u0621 \u0645\u0644\u062e\u0635 \u0645\u0647\u0646\u064a \u062d\u0627\u0644\u064a\u0627\u064b.";
        }

        return string.Join(" ", sentences);
    }

    private static bool HasText(params string?[] values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
