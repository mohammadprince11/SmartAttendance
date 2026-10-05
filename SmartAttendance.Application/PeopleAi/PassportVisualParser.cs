using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartAttendance.Application.PeopleAi;

public sealed record PassportVisualOcrLine(
    string Text,
    double? Confidence,
    int[]? Box);

public sealed record PassportVisualParseResult(
    string? DocumentNumber,
    string? GivenNames,
    string? Surname,
    string? DateOfBirth,
    string? ExpiryDate,
    string? IssueDate,
    string? Nationality,
    string? Sex,
    string? IssuingCountry,
    string? PlaceOfBirth,
    string? MotherName,
    string? IssuingAuthority)
{
    public bool HasStrongIdentityEvidence =>
        !string.IsNullOrWhiteSpace(DocumentNumber) &&
        (!string.IsNullOrWhiteSpace(GivenNames) ||
         !string.IsNullOrWhiteSpace(DateOfBirth));
}

public static class PassportVisualParser
{    private static readonly Regex PassportNumberRegex =
        new(@"(?<![A-Z0-9])([A-Z]{1,3}[A-Z0-9]{6,11})(?![A-Z0-9])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] KnownLabels =
    [
        "passport", "passportno", "passportnumber",
        "fullname", "givenname", "givennames",
        "surname", "familyname", "nationality",
        "sex", "dateofbirth", "birthdate", "placeofbirth",
        "mothername", "dateofexpiry", "expirydate",
        "dateofissue", "issuedate", "issuingauthority",
        "country", "issuingcountry",
        "رقمالجواز", "الجواز", "الاسمالكامل", "الاسم",
        "اللقب", "الجنسية", "الجنس", "تاريخالميلاد",
        "مكانالميلاد", "محلالميلاد", "اسمالام", "تاريخالانتهاء",
        "تاريخالاصدار", "جهةالاصدار", "بلدالاصدار"
    ];

    public static PassportVisualParseResult Parse(
        IEnumerable<PassportVisualOcrLine> source)
    {
        var lines = source
            .Select((line, index) => new Line(
                index,
                (line.Text ?? string.Empty).Trim(),
                line.Confidence,
                line.Box))
            .Where(x => x.Text.Length > 0)
            .ToList();

        var documentNumber =
            FindValue(lines,
                ["passport", "passportno", "passportnumber", "رقمالجواز"],
                IsPassportNumber) ??
            FindGlobalPassportNumber(lines);        var givenNames = CleanText(
            FindValue(lines,
                ["fullname", "givenname", "givennames", "الاسمالكامل"],
                IsNameValue));
        var surname = CleanText(
            FindValue(lines,
                ["surname", "familyname", "اللقب"],
                IsNameValue));
        var nationality = NormalizeNationality(
            FindValue(lines,
                ["nationality", "الجنسية"],
                IsNationalityValue));
        var sex = NormalizeSex(
            FindValue(lines,
                ["sex", "الجنس"],
                IsSexValue));
        var dateOfBirth = NormalizeDate(
            FindValue(lines,
                ["dateofbirth", "birthdate", "تاريخالميلاد"],
                IsDateValue));
        var expiryDate = NormalizeDate(
            FindValue(lines,
                ["dateofexpiry", "expirydate", "تاريخالانتهاء"],
                IsDateValue));
        var issueDate = NormalizeDate(
            FindValue(lines,
                ["dateofissue", "issuedate", "تاريخالاصدار"],
                IsDateValue));
        var issuingCountry = CleanToken(
            FindValue(lines,
                ["country", "issuingcountry", "بلدالاصدار"],
                IsCountryValue));
        var placeOfBirth = CleanText(
            FindValue(lines,
                ["placeofbirth", "مكانالميلاد", "محلالميلاد"],
                IsPlaceValue));
        var motherName = CleanText(
            FindValue(lines,
                ["mothername", "اسمالام"],
                IsNameValue));
        var issuingAuthority = CleanText(
            FindValue(lines,
                ["issuingauthority", "جهةالاصدار"],
                IsShortTextValue));

        return new PassportVisualParseResult(
            documentNumber,
            givenNames,
            surname,
            dateOfBirth,
            expiryDate,
            issueDate,
            nationality,
            sex,
            issuingCountry,
            placeOfBirth,
            motherName,
            issuingAuthority);
    }    private static string? FindValue(
        IReadOnlyList<Line> lines,
        IReadOnlyList<string> labels,
        Func<string, bool> predicate)
    {
        var normalizedLabels = labels
            .Select(Canonical)
            .Where(x => x.Length > 0)
            .ToArray();

        for (var labelPosition = 0;
             labelPosition < lines.Count;
             labelPosition++)
        {
            var label = lines[labelPosition];
            if (!normalizedLabels.Any(labelKey =>
                    Canonical(label.Text).Contains(
                        labelKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var candidates = lines
                .Where(candidate =>
                    candidate.Index != label.Index &&
                    !LooksLikeLabel(candidate.Text) &&
                    predicate(candidate.Text))
                .Select(candidate => new
                {
                    Line = candidate,
                    Score = SpatialScore(label, candidate)
                })
                .Where(x => x.Score < double.MaxValue)
                .OrderBy(x => x.Score)
                .ToList();

            if (candidates.Count > 0)
            {
                return candidates[0].Line.Text;
            }

            for (var i = labelPosition + 1;
                 i < lines.Count && i <= labelPosition + 5;
                 i++)
            {
                var candidate = lines[i];
                if (LooksLikeLabel(candidate.Text))
                {
                    break;
                }

                if (predicate(candidate.Text))
                {
                    return candidate.Text;
                }
            }
        }

        return null;
    }    private static double SpatialScore(Line label, Line candidate)
    {
        if (label.Box is not { Length: >= 4 } labelBox ||
            candidate.Box is not { Length: >= 4 } candidateBox)
        {
            return double.MaxValue;
        }

        var verticalGap = candidateBox[1] - labelBox[3];
        if (verticalGap < -35 || verticalGap > 150)
        {
            return double.MaxValue;
        }

        var labelCenterX = (labelBox[0] + labelBox[2]) / 2d;
        var candidateCenterX =
            (candidateBox[0] + candidateBox[2]) / 2d;
        var horizontalDistance =
            Math.Abs(candidateCenterX - labelCenterX);

        if (horizontalDistance > 500)
        {
            return double.MaxValue;
        }

        return Math.Max(verticalGap, 0) +
               horizontalDistance / 8d;
    }

    private static string? FindGlobalPassportNumber(
        IEnumerable<Line> lines)
    {
        foreach (var line in lines)
        {
            var normalized = line.Text
                .Trim()
                .ToUpperInvariant();
            var match = PassportNumberRegex.Match(normalized);
            if (match.Success &&
                IsPassportNumber(match.Groups[1].Value))
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }    private static bool IsPassportNumber(string value)
    {
        var compact = CleanToken(value);
        if (string.IsNullOrWhiteSpace(compact) ||
            compact.Length is < 7 or > 14)
        {
            return false;
        }

        return compact.Any(char.IsDigit) &&
               compact.Any(char.IsLetter) &&
               compact.All(char.IsLetterOrDigit);
    }

    private static bool IsNameValue(string value)
    {
        var cleaned = CleanText(value);
        if (string.IsNullOrWhiteSpace(cleaned) ||
            LooksLikeLabel(cleaned) ||
            cleaned.Any(char.IsDigit))
        {
            return false;
        }

        return cleaned.Count(char.IsLetter) >= 3;
    }

    private static bool IsNationalityValue(string value)
    {
        var cleaned = CleanToken(value);
        return !string.IsNullOrWhiteSpace(cleaned) &&
               cleaned.Length is >= 3 and <= 24 &&
               cleaned.Count(char.IsLetter) >= 3 &&
               !LooksLikeLabel(cleaned);
    }

    private static bool IsCountryValue(string value)
    {
        var cleaned = CleanToken(value);
        return !string.IsNullOrWhiteSpace(cleaned) &&
               cleaned.Length is >= 3 and <= 24 &&
               cleaned.Count(char.IsLetter) >= 3 &&
               !LooksLikeLabel(cleaned);
    }    private static string? NormalizeNationality(string? value)
    {
        var cleaned = CleanToken(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var canonical = Canonical(cleaned);
        return canonical is "IRAQI" or "IRAQ" or "عراقي" or "العراق"
            ? "IRQ"
            : cleaned;
    }

    private static bool IsSexValue(string value) =>
        NormalizeSex(value) is not null;

    private static bool IsDateValue(string value) =>
        NormalizeDate(value) is not null;

    private static bool IsPlaceValue(string value)
    {
        var cleaned = CleanText(value);
        return !string.IsNullOrWhiteSpace(cleaned) &&
               cleaned.Count(char.IsLetter) >= 4 &&
               cleaned.Length <= 60 &&
               !LooksLikeLabel(cleaned);
    }

    private static bool IsShortTextValue(string value)
    {
        var cleaned = CleanText(value);
        return !string.IsNullOrWhiteSpace(cleaned) &&
               cleaned.Length <= 60 &&
               !LooksLikeLabel(cleaned);
    }

    private static string? NormalizeSex(string? value)
    {
        var canonical = Canonical(value);
        if (canonical.Length == 0)
        {
            return null;
        }

        if (canonical.Contains("female", StringComparison.OrdinalIgnoreCase) ||
            canonical.Contains("انث", StringComparison.Ordinal))
        {
            return "F";
        }

        if (canonical.Contains("male", StringComparison.OrdinalIgnoreCase) ||
            canonical.Contains("ذكر", StringComparison.Ordinal))
        {
            return "M";
        }

        if (canonical[0] is 'M' or 'm' && canonical.Length <= 4)
        {
            return "M";
        }

        if (canonical[0] is 'F' or 'f' && canonical.Length <= 4)
        {
            return "F";
        }

        return null;
    }    private static string? NormalizeDate(string? value)
    {
        var text = (value ?? string.Empty)
            .Trim()
            .Replace('.', '-')
            .Replace('/', '-');

        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd-MM-yyyy",
            "yyyy-M-d",
            "d-M-yyyy"
        };

        return DateOnly.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
                ? parsed.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                : null;
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Regex.Replace(
            value.Trim(),
            @"\s+",
            " ");
        return text.Trim(' ', '/', '\\', '|', ':', ';');
    }

    private static string? CleanToken(string? value)
    {
        var cleaned = CleanText(value);
        return string.IsNullOrWhiteSpace(cleaned)
            ? null
            : Regex.Replace(cleaned, @"[^A-Za-z0-9؀-ۿ-]", "");
    }    private static bool LooksLikeLabel(string value)
    {
        var canonical = Canonical(value);
        return KnownLabels.Any(label =>
            canonical.Contains(
                Canonical(label),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string Canonical(string? value)
    {
        var builder = new StringBuilder();

        foreach (var c in (value ?? string.Empty)
                     .Trim()
                     .ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c switch
                {
                    'أ' or 'إ' or 'آ' => 'ا',
                    'ى' => 'ي',
                    _ => c
                });
            }
        }

        return builder.ToString();
    }

    private sealed record Line(
        int Index,
        string Text,
        double? Confidence,
        int[]? Box);
}
