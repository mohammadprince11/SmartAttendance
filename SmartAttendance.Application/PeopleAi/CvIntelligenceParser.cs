using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartAttendance.Application.PeopleAi;

public sealed record CvIntelligenceLine(
    string Text,
    double? Confidence = null);

public sealed record CvStructuredRecord(
    string RecordType,
    string Title,
    string? Subtitle,
    string? Country,
    string? RefNo,
    DateOnly? FromDate,
    DateOnly? ToDate,
    bool IsCurrent,
    string? Note,
    decimal? Confidence);

public sealed record CvIntelligenceResult(
    string? FullName,
    string? Address,
    string? Nationality,
    string? ProfessionalSummary,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages,
    IReadOnlyList<CvStructuredRecord> Records);

public static class CvIntelligenceParser
{
    private enum Section
    {
        None,
        Contact,
        ProfessionalSummary,
        Education,
        Experience,
        Certificates,
        Skills,
        Languages
    }

    private sealed record ParsedLine(
        string Text,
        decimal? Confidence);

    private static readonly Dictionary<string, Section> Headers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contact"] = Section.Contact,
            ["contact details"] = Section.Contact,
            ["contact information"] = Section.Contact,
            ["personal details"] = Section.Contact,
            ["personal information"] = Section.Contact,
            ["الاتصال"] = Section.Contact,
            ["معلومات الاتصال"] = Section.Contact,
            ["بيانات الاتصال"] = Section.Contact,

            ["professional summary"] = Section.ProfessionalSummary,
            ["profile summary"] = Section.ProfessionalSummary,
            ["career summary"] = Section.ProfessionalSummary,
            ["professional profile"] = Section.ProfessionalSummary,
            ["summary"] = Section.ProfessionalSummary,
            ["الملخص المهني"] = Section.ProfessionalSummary,
            ["النبذة المهنية"] = Section.ProfessionalSummary,
            ["نبذة مهنية"] = Section.ProfessionalSummary,

            ["education"] = Section.Education,
            ["academic background"] = Section.Education,
            ["academic qualifications"] = Section.Education,
            ["qualifications"] = Section.Education,
            ["\u0627\u0644\u062a\u0639\u0644\u064a\u0645"] = Section.Education,
            ["\u0627\u0644\u0645\u0624\u0647\u0644\u0627\u062a"] = Section.Education,
            ["\u0627\u0644\u0645\u0624\u0647\u0644\u0627\u062a \u0627\u0644\u0639\u0644\u0645\u064a\u0629"] = Section.Education,
            ["\u0627\u0644\u062f\u0631\u0627\u0633\u0629"] = Section.Education,

            ["experience"] = Section.Experience,
            ["work experience"] = Section.Experience,
            ["professional experience"] = Section.Experience,
            ["employment history"] = Section.Experience,
            ["work history"] = Section.Experience,
            ["\u0627\u0644\u062e\u0628\u0631\u0629"] = Section.Experience,
            ["\u0627\u0644\u062e\u0628\u0631\u0627\u062a"] = Section.Experience,
            ["\u0627\u0644\u062e\u0628\u0631\u0627\u062a \u0627\u0644\u0639\u0645\u0644\u064a\u0629"] = Section.Experience,
            ["\u0627\u0644\u062e\u0628\u0631\u0629 \u0627\u0644\u0639\u0645\u0644\u064a\u0629"] = Section.Experience,

            ["certificates"] = Section.Certificates,
            ["certifications"] = Section.Certificates,
            ["licenses"] = Section.Certificates,
            ["certificates & licenses"] = Section.Certificates,
            ["\u0627\u0644\u0634\u0647\u0627\u062f\u0627\u062a"] = Section.Certificates,
            ["\u0627\u0644\u0634\u0647\u0627\u062f\u0627\u062a \u0627\u0644\u0645\u0647\u0646\u064a\u0629"] = Section.Certificates,
            ["\u0627\u0644\u062a\u0631\u0627\u062e\u064a\u0635"] = Section.Certificates,

            ["skills"] = Section.Skills,
            ["technical skills"] = Section.Skills,
            ["core skills"] = Section.Skills,
            ["\u0627\u0644\u0645\u0647\u0627\u0631\u0627\u062a"] = Section.Skills,
            ["\u0627\u0644\u0645\u0647\u0627\u0631\u0627\u062a \u0627\u0644\u062a\u0642\u0646\u064a\u0629"] = Section.Skills,

            ["languages"] = Section.Languages,
            ["language"] = Section.Languages,
            ["\u0627\u0644\u0644\u063a\u0627\u062a"] = Section.Languages,
            ["\u0627\u0644\u0644\u063a\u0629"] = Section.Languages
        };

    public static CvIntelligenceResult Parse(
        IEnumerable<CvIntelligenceLine> input)
    {
        var lines = input
            .Select(line => new ParsedLine(
                CleanLine(line.Text),
                ToDecimal(line.Confidence)))
            .Where(line => line.Text.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return new CvIntelligenceResult(
                null, null, null, null, [], [], []);
        }

        string? fullName = null;
        string? address = null;
        string? nationality = null;
        string? professionalSummary = null;
        var skills = new List<string>();
        var languages = new List<string>();
        var records = new List<CvStructuredRecord>();

        var section = Section.None;
        var sectionLines = new List<ParsedLine>();

        void FlushSection()
        {
            if (sectionLines.Count == 0)
            {
                return;
            }

            switch (section)
            {
                case Section.Contact:
                    address ??= ExtractContactAddress(sectionLines);
                    break;
                case Section.ProfessionalSummary:
                    professionalSummary ??= JoinNarrative(sectionLines);
                    break;
                case Section.Education:
                    records.AddRange(ParseStructuredSection(
                        "Education", sectionLines));
                    break;
                case Section.Experience:
                    records.AddRange(ParseStructuredSection(
                        "Experience", sectionLines));
                    break;
                case Section.Certificates:
                    records.AddRange(ParseStructuredSection(
                        "Certificate", sectionLines));
                    break;
                case Section.Skills:
                    AddDistinct(skills, ExpandList(sectionLines));
                    break;
                case Section.Languages:
                    AddDistinct(languages, ParseLanguages(sectionLines));
                    break;
            }

            sectionLines.Clear();
        }

        foreach (var line in lines)
        {
            if (TryResolveHeader(line.Text, out var nextSection))
            {
                FlushSection();
                section = nextSection;
                continue;
            }

            if (section == Section.None)
            {
                if (TryValueAfterLabel(
                        line.Text,
                        [
                            "address",
                            "location",
                            "\u0627\u0644\u0639\u0646\u0648\u0627\u0646",
                            "\u0627\u0644\u0633\u0643\u0646"
                        ],
                        out var addressValue))
                {
                    address ??= addressValue;
                    continue;
                }

                if (TryValueAfterLabel(
                        line.Text,
                        [
                            "nationality",
                            "\u0627\u0644\u062c\u0646\u0633\u064a\u0629"
                        ],
                        out var nationalityValue))
                {
                    nationality ??= nationalityValue;
                    continue;
                }

                if (fullName is null &&
                    LooksLikePersonName(line.Text))
                {
                    fullName = line.Text;
                }

                continue;
            }

            sectionLines.Add(line);
        }

        FlushSection();

        return new CvIntelligenceResult(
            fullName,
            address,
            nationality,
            professionalSummary,
            skills,
            languages,
            records);
    }

    private static IReadOnlyList<CvStructuredRecord>
        ParseStructuredSection(
            string recordType,
            IReadOnlyList<ParsedLine> lines)
    {
        if (recordType == "Experience")
        {
            var experience = ParseExperienceSection(lines);
            if (experience.Count > 0)
            {
                return experience;
            }
        }

        if (recordType == "Education")
        {
            var education = ParseEducationSection(lines);
            if (education.Count > 0)
            {
                return education;
            }
        }

        var result = new List<CvStructuredRecord>();
        var pending = new List<ParsedLine>();

        void FlushPending()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var joined = string.Join(
                " | ",
                pending.Select(line => line.Text));
            var date = ParseDateRange(joined);
            var content = pending
                .Select(line => RemoveDateTokens(line.Text))
                .Where(value => value.Length > 0)
                .ToList();

            if (content.Count == 0)
            {
                pending.Clear();
                return;
            }

            var title = content[0];
            string? subtitle = content.Count > 1
                ? content[1]
                : null;

            if (recordType == "Experience" &&
                TrySplitRoleAndCompany(
                    content[0],
                    out var company,
                    out var role))
            {
                title = company;
                subtitle = role;
            }

            if (recordType == "Education" &&
                content.Count > 1 &&
                LooksLikeDegree(content[0]) &&
                !LooksLikeDegree(content[1]))
            {
                title = content[1];
                subtitle = content[0];
            }

            var scored = pending
                .Where(line => line.Confidence.HasValue)
                .Select(line => line.Confidence!.Value)
                .ToList();

            result.Add(new CvStructuredRecord(
                recordType,
                title,
                subtitle,
                null,
                recordType == "Certificate"
                    ? ExtractReference(joined)
                    : null,
                date.FromDate,
                date.ToDate,
                date.IsCurrent,
                content.Count > 2
                    ? string.Join(" | ", content.Skip(2))
                    : null,
                scored.Count == 0
                    ? null
                    : Math.Round(
                        scored.Average(),
                        5,
                        MidpointRounding.AwayFromZero)));

            pending.Clear();
        }

        foreach (var line in lines)
        {
            if (IsDateOnlyLine(line.Text) &&
                pending.Count > 0)
            {
                pending.Add(line);
                FlushPending();
                continue;
            }

            if (pending.Count > 0 &&
                StartsNewEntry(recordType, line.Text))
            {
                FlushPending();
            }

            pending.Add(line);

            if (recordType == "Certificate" &&
                pending.Count == 1 &&
                !ContainsDate(line.Text))
            {
                FlushPending();
            }
        }

        FlushPending();
        return result;
    }

    private static IReadOnlyList<CvStructuredRecord> ParseExperienceSection(
        IReadOnlyList<ParsedLine> lines)
    {
        var dateIndexes = lines
            .Select((line, index) => new { line, index })
            .Where(x => ContainsDate(x.line.Text))
            .Select(x => x.index)
            .ToList();

        if (dateIndexes.Count == 0)
        {
            return [];
        }

        var entries = new List<(int HeaderStart, int DateIndex,
            string Company, string Role, string? Location,
            (DateOnly? FromDate, DateOnly? ToDate, bool IsCurrent) Date,
            decimal? Confidence)>();

        var lowerBound = 0;
        foreach (var dateIndex in dateIndexes)
        {
            var headerEnd = dateIndex - 1;
            if (headerEnd < lowerBound)
            {
                continue;
            }

            var headerStart = headerEnd;
            if (headerEnd - 1 >= lowerBound)
            {
                headerStart = headerEnd - 1;
            }

            var headerLines = lines
                .Skip(headerStart)
                .Take(headerEnd - headerStart + 1)
                .ToList();

            string role;
            string company;
            string? location = null;

            if (headerLines.Count == 1 &&
                TrySplitRoleAndCompany(
                    headerLines[0].Text,
                    out company,
                    out role))
            {
            }
            else if (headerLines.Count >= 2)
            {
                role = headerLines[0].Text;
                ParseCompanyAndLocation(
                    headerLines[^1].Text,
                    out company,
                    out location);
            }
            else
            {
                continue;
            }

            var scored = lines
                .Skip(headerStart)
                .Take(dateIndex - headerStart + 1)
                .Where(x => x.Confidence.HasValue)
                .Select(x => x.Confidence!.Value)
                .ToList();

            entries.Add((
                headerStart,
                dateIndex,
                company,
                role,
                location,
                ParseDateRange(lines[dateIndex].Text),
                scored.Count == 0
                    ? null
                    : Math.Round(scored.Average(), 5,
                        MidpointRounding.AwayFromZero)));

            lowerBound = dateIndex + 1;
        }

        var result = new List<CvStructuredRecord>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var descriptionEnd = i + 1 < entries.Count
                ? entries[i + 1].HeaderStart
                : lines.Count;
            var description = lines
                .Skip(entry.DateIndex + 1)
                .Take(Math.Max(
                    0,
                    descriptionEnd - entry.DateIndex - 1))
                .Select(x => x.Text)
                .Where(x => x.Length > 0)
                .ToList();

            var noteParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Location))
            {
                noteParts.Add($"Location: {entry.Location}");
            }
            if (description.Count > 0)
            {
                noteParts.Add(string.Join(" ", description));
            }

            result.Add(new CvStructuredRecord(
                "Experience",
                entry.Company,
                entry.Role,
                null,
                null,
                entry.Date.FromDate,
                entry.Date.ToDate,
                entry.Date.IsCurrent,
                noteParts.Count == 0
                    ? null
                    : string.Join(" | ", noteParts),
                entry.Confidence));
        }

        return result;
    }

    private static IReadOnlyList<CvStructuredRecord> ParseEducationSection(
        IReadOnlyList<ParsedLine> lines)
    {
        var degreeIndexes = lines
            .Select((line, index) => new { line, index })
            .Where(x => LooksLikeDegree(x.line.Text))
            .Select(x => x.index)
            .ToList();

        if (degreeIndexes.Count == 0)
        {
            return [];
        }

        var result = new List<CvStructuredRecord>();
        for (var i = 0; i < degreeIndexes.Count; i++)
        {
            var start = degreeIndexes[i];
            var end = i + 1 < degreeIndexes.Count
                ? degreeIndexes[i + 1]
                : lines.Count;
            var block = lines.Skip(start).Take(end - start).ToList();
            var degree = block[0].Text;
            var joined = string.Join(" | ", block.Select(x => x.Text));
            var date = ParseDateRange(joined);

            var content = block
                .Skip(1)
                .Select(x => RemoveDateTokens(x.Text))
                .Where(x => x.Length > 0)
                .ToList();

            var institution = content.FirstOrDefault();
            var note = content.Count > 1
                ? string.Join(" | ", content.Skip(1))
                : null;
            var scored = block
                .Where(x => x.Confidence.HasValue)
                .Select(x => x.Confidence!.Value)
                .ToList();

            result.Add(new CvStructuredRecord(
                "Education",
                institution ?? degree,
                institution is null ? null : degree,
                null,
                null,
                date.FromDate,
                date.ToDate,
                date.IsCurrent,
                note,
                scored.Count == 0
                    ? null
                    : Math.Round(scored.Average(), 5,
                        MidpointRounding.AwayFromZero)));
        }

        return result;
    }

    private static void ParseCompanyAndLocation(
        string value,
        out string company,
        out string? location)
    {
        var match = Regex.Match(
            value,
            @"^(?<company>.+?)\s+[\u2014\u2013-]\s+(?<location>[^\u2014\u2013|]{2,80})$");
        if (match.Success)
        {
            company = match.Groups["company"].Value.Trim();
            location = match.Groups["location"].Value.Trim();
            return;
        }

        company = value.Trim();
        location = null;
    }

    private static bool StartsNewEntry(
        string recordType,
        string text)
    {
        if (recordType == "Experience")
        {
            return TrySplitRoleAndCompany(
                       text,
                       out _,
                       out _) ||
                   ContainsDate(text);
        }

        if (recordType == "Education")
        {
            return LooksLikeDegree(text) ||
                   ContainsDate(text);
        }

        return false;
    }

    private static IEnumerable<string> ExpandList(
        IEnumerable<ParsedLine> lines) =>
        lines
            .SelectMany(line => Regex.Split(
                line.Text,
                @"[,;\u2022|]+"))
            .Select(CleanLine)
            .Where(value => value.Length > 0);

    private static string? JoinNarrative(
        IEnumerable<ParsedLine> lines)
    {
        var value = string.Join(
            " ",
            lines.Select(x => x.Text).Where(x => x.Length > 0));
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Regex.Replace(value, @"\s{2,}", " ").Trim();
    }

    private static string? ExtractContactAddress(
        IReadOnlyList<ParsedLine> lines)
    {
        foreach (var line in lines)
        {
            if (TryValueAfterLabel(
                    line.Text,
                    ["address", "location", "العنوان", "السكن"],
                    out var labeled))
            {
                return labeled;
            }
        }

        var locationParts = lines
            .Select(x => CleanLine(x.Text))
            .Where(x => x.Length is >= 2 and <= 80)
            .Where(x => !LooksLikeContactNoise(x))
            .Take(3)
            .ToList();

        return locationParts.Count == 0
            ? null
            : string.Join(", ", locationParts);
    }

    private static bool LooksLikeContactNoise(string value) =>
        value.Contains('@') ||
        Regex.IsMatch(value, @"https?://|www\.|linkedin", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(value, @"\+?\d[\d\s().-]{5,}") ||
        Regex.IsMatch(value, @"\b(?:19|20)\d{2}[-/.]\d{1,2}[-/.]\d{1,2}\b") ||
        Regex.IsMatch(value, @"^\d{1,2}[-/.]\d{1,2}[-/.](?:19|20)\d{2}$");

    private static IEnumerable<string> ParseLanguages(
        IReadOnlyList<ParsedLine> lines)
    {
        var result = new List<string>();
        foreach (var item in ExpandList(lines))
        {
            if (IsLanguageProficiency(item) && result.Count > 0)
            {
                result[^1] = $"{result[^1]} ({item})";
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private static bool IsLanguageProficiency(string value) =>
        Regex.IsMatch(
            value,
            @"^(native|mother tongue|fluent|advanced|intermediate|basic|beginner|elementary|conversational|professional|proficient)$",
            RegexOptions.IgnoreCase) ||
        value is "أم" or "اللغة الأم" or "طليق" or "متقدم" or
            "متوسط" or "أساسي" or "مبتدئ";

    private static void AddDistinct(
        ICollection<string> target,
        IEnumerable<string> values)
    {
        var known = new HashSet<string>(
            target,
            StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (known.Add(value))
            {
                target.Add(value);
            }
        }
    }

    private static bool TryResolveHeader(
        string text,
        out Section section)
    {
        var key = text.Trim();
        while (key.Length > 0 &&
               ":,-".Contains(key[^1]))
        {
            key = key[..^1].TrimEnd();
        }

        if (Headers.TryGetValue(key, out section))
        {
            return true;
        }

        var canonical = HeaderForm(key);
        foreach (var pair in Headers)
        {
            if (HeaderForm(pair.Key).Equals(
                    canonical,
                    StringComparison.OrdinalIgnoreCase))
            {
                section = pair.Value;
                return true;
            }
        }

        section = Section.None;
        return false;
    }

    private static string HeaderForm(string value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static bool TryValueAfterLabel(
        string text,
        IEnumerable<string> labels,
        out string value)
    {
        foreach (var label in labels)
        {
            if (!text.StartsWith(
                    label,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = text[label.Length..]
                .TrimStart(' ', ':', '-');

            if (suffix.Length > 0)
            {
                value = suffix;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool LooksLikePersonName(string text)
    {
        if (text.Length is < 4 or > 90 ||
            text.Contains('@') ||
            Regex.IsMatch(
                text,
                @"https?://|www\.",
                RegexOptions.IgnoreCase) ||
            Regex.IsMatch(
                text,
                @"\+?\d[\d\s().-]{6,}") ||
            Headers.ContainsKey(
                text.Trim().TrimEnd(':')))
        {
            return false;
        }

        var words = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (words.Length is < 2 or > 6)
        {
            return false;
        }

        return words.All(word =>
            word.Any(char.IsLetter) &&
            word.Count(char.IsDigit) == 0);
    }

    private static bool LooksLikeDegree(string text) =>
        Regex.IsMatch(
            text,
            @"\b(bachelor|master|phd|doctorate|diploma|bsc|msc|mba|degree)\b",
            RegexOptions.IgnoreCase) ||
        text.Contains(
            "\u0628\u0643\u0627\u0644\u0648\u0631\u064a\u0648\u0633",
            StringComparison.OrdinalIgnoreCase) ||
        text.Contains(
            "\u0645\u0627\u062c\u0633\u062a\u064a\u0631",
            StringComparison.OrdinalIgnoreCase) ||
        text.Contains(
            "\u062f\u0643\u062a\u0648\u0631\u0627\u0647",
            StringComparison.OrdinalIgnoreCase) ||
        text.Contains(
            "\u062f\u0628\u0644\u0648\u0645",
            StringComparison.OrdinalIgnoreCase);

    private static bool TrySplitRoleAndCompany(
        string text,
        out string company,
        out string role)
    {
        var at = Regex.Match(
            text,
            @"^(?<role>.+?)\s+(?:at|@)\s+(?<company>.+)$",
            RegexOptions.IgnoreCase);

        if (at.Success)
        {
            role = at.Groups["role"].Value.Trim();
            company = at.Groups["company"].Value.Trim();
            return role.Length > 0 && company.Length > 0;
        }

        var separator = Regex.Split(
            text,
            @"\s+\|\s+|\s+-\s+");

        if (separator.Length == 2 &&
            separator.All(part => part.Trim().Length > 1))
        {
            role = separator[0].Trim();
            company = separator[1].Trim();
            return true;
        }

        company = string.Empty;
        role = string.Empty;
        return false;
    }

    private static (
        DateOnly? FromDate,
        DateOnly? ToDate,
        bool IsCurrent)
        ParseDateRange(string text)
    {
        var years = Regex
            .Matches(text, @"\b(?:19|20)\d{2}\b")
            .Select(match =>
                int.TryParse(
                    match.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var year)
                    ? year
                    : 0)
            .Where(year => year is >= 1950 and <= 2100)
            .ToList();

        var current = Regex.IsMatch(
            text,
            @"\b(present|current|now)\b",
            RegexOptions.IgnoreCase) ||
            text.Contains(
                "\u062d\u062a\u0649 \u0627\u0644\u0622\u0646",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                "\u0644\u063a\u0627\u064a\u0629 \u0627\u0644\u0622\u0646",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                "\u0645\u0633\u062a\u0645\u0631",
                StringComparison.OrdinalIgnoreCase);

        var monthYears = Regex
            .Matches(
                text,
                @"\b(?<month>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(?<year>(?:19|20)\d{2})\b",
                RegexOptions.IgnoreCase)
            .Select(match =>
            {
                var candidate =
                    $"{match.Groups["month"].Value} 1, {match.Groups["year"].Value}";
                return DateTime.TryParse(
                    candidate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsed)
                    ? new DateOnly(parsed.Year, parsed.Month, 1)
                    : (DateOnly?)null;
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        DateOnly? from = monthYears.Count > 0
            ? monthYears[0]
            : years.Count > 0
                ? new DateOnly(years[0], 1, 1)
                : null;

        DateOnly? to = monthYears.Count > 1
            ? new DateOnly(
                monthYears[^1].Year,
                monthYears[^1].Month,
                DateTime.DaysInMonth(
                    monthYears[^1].Year,
                    monthYears[^1].Month))
            : years.Count > 1
                ? new DateOnly(years[^1], 12, 31)
                : null;

        return (from, current ? null : to, current);
    }

    private static bool ContainsDate(string text) =>
        Regex.IsMatch(
            text,
            @"\b(?:19|20)\d{2}\b") ||
        Regex.IsMatch(
            text,
            @"\b(present|current|now)\b",
            RegexOptions.IgnoreCase) ||
        text.Contains(
            "\u062d\u062a\u0649 \u0627\u0644\u0622\u0646",
            StringComparison.OrdinalIgnoreCase) ||
        text.Contains(
            "\u0644\u063a\u0627\u064a\u0629 \u0627\u0644\u0622\u0646",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDateOnlyLine(string text)
    {
        var remaining = RemoveDateTokens(text);
        return remaining.Length <= 3 &&
               ContainsDate(text);
    }

    private static string RemoveDateTokens(string text)
    {
        var value = Regex.Replace(
            text,
            @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(?:19|20)\d{2}\b",
            " ",
            RegexOptions.IgnoreCase);

        value = Regex.Replace(
            value,
            @"\b(?:19|20)\d{2}\b",
            " ");

        value = Regex.Replace(
            value,
            @"\b(present|current|now)\b",
            " ",
            RegexOptions.IgnoreCase);

        value = value
            .Replace(
                "\u062d\u062a\u0649 \u0627\u0644\u0622\u0646",
                " ",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "\u0644\u063a\u0627\u064a\u0629 \u0627\u0644\u0622\u0646",
                " ",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "\u0645\u0633\u062a\u0645\u0631",
                " ",
                StringComparison.OrdinalIgnoreCase);

        value = Regex.Replace(
            value,
            @"^[\s\-|,:/]+|[\s\-|,:/]+$",
            string.Empty);

        return Regex.Replace(
            value,
            @"\s{2,}",
            " ").Trim();
    }

    private static string? ExtractReference(string text)
    {
        var match = Regex.Match(
            text,
            @"(?:certificate|cert|license|ref|no\.?)\s*[:#-]?\s*(?<ref>[A-Z0-9][A-Z0-9./_-]{3,})",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups["ref"].Value.Trim()
            : null;
    }

    private static string CleanLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Replace('\u00A0', ' ')
            .Trim();

        cleaned = Regex.Replace(
            cleaned,
            @"^[\s\u2022\u00B7\u25AA\u25E6*-]+",
            string.Empty);

        return Regex.Replace(
            cleaned,
            @"\s{2,}",
            " ").Trim();
    }

    private static decimal? ToDecimal(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Math.Round(
            (decimal)value.Value,
            5,
            MidpointRounding.AwayFromZero);
    }
}
