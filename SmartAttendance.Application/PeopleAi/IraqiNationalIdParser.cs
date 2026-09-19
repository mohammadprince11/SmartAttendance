using System.Text.RegularExpressions;

namespace SmartAttendance.Application.PeopleAi;

public sealed record IraqiNationalIdOcrLine(
    string Text,
    double? Confidence,
    int[]? Box);

public sealed record IraqiNationalIdParseResult(
    string? NationalNumber,
    string? DocumentNumber,
    string? FamilyNumber,
    string? FirstName,
    string? SecondName,
    string? ThirdName,
    string? LastName,
    string? MotherName);

public static class IraqiNationalIdParser
{
    private static readonly Regex NationalNumberRegex =
        new(@"(?<!\d)(\d{12})(?!\d)", RegexOptions.Compiled);

    private static readonly Regex DocumentNumberRegex =
        new(@"(?<![A-Z0-9])([A-Z]{1,3}\d{6,10})(?![A-Z0-9])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static IraqiNationalIdParseResult Parse(
        IEnumerable<IraqiNationalIdOcrLine> source)
    {
        var lines = source
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => x with { Text = NormalizeDigits(x.Text).Trim() })
            .ToList();

        var nationalNumber = FindNationalNumber(lines);
        var documentNumber = FindDocumentNumber(lines);
        var familyNumber = FindFamilyNumber(lines);

        var firstName = FindName(
            lines,
            ["الاسم"],
            ["لاناو"]);

        var secondName = FindName(
            lines,
            ["الاب", "الأب"],
            ["باوك"]);

        var thirdName = FindName(
            lines,
            ["الجد"],
            ["بابير"]);

        var lastName = FindName(
            lines,
            ["اللقب"],
            ["نارناو"]);
        var motherName = FindName(
            lines,
            ["الام", "الأم"],
            ["دايك"]);

        return new IraqiNationalIdParseResult(
            nationalNumber,
            documentNumber,
            familyNumber,
            firstName,
            secondName,
            thirdName,
            lastName,
            motherName);
    }

    private static string? FindNationalNumber(
        IReadOnlyList<IraqiNationalIdOcrLine> lines)
    {
        foreach (var line in lines)
        {
            var match = NationalNumberRegex.Match(line.Text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
    private static string? FindDocumentNumber(
        IReadOnlyList<IraqiNationalIdOcrLine> lines)
    {
        foreach (var line in lines)
        {
            var upper = line.Text.ToUpperInvariant();
            var match = DocumentNumberRegex.Match(upper);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string? FindFamilyNumber(
        IReadOnlyList<IraqiNationalIdOcrLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var form = LabelForm(lines[i].Text);
            var isFamilyLabel =
                form.StartsWith("الرقمالعائلي", StringComparison.Ordinal) ||
                form.StartsWith("الرقمالعانلي", StringComparison.Ordinal) ||
                form.Contains("ژماردىخيزاني", StringComparison.Ordinal);

            if (!isFamilyLabel)
            {
                continue;
            }

            var inline = Regex.Match(
                lines[i].Text.ToUpperInvariant(),
                @"(?<![A-Z0-9])([A-Z0-9]{6,24})(?![A-Z0-9])");
            if (inline.Success &&
                inline.Groups[1].Value.Count(char.IsDigit) >= 4)
            {
                return inline.Groups[1].Value;
            }

            if (lines[i].Box is not { Length: >= 4 } labelBox)
            {
                continue;
            }

            var labelCenterY = (labelBox[1] + labelBox[3]) / 2d;
            var candidates = lines
                .Where((_, index) => index != i)
                .Select(line => new
                {
                    Line = line,
                    Match = Regex.Match(
                        line.Text.ToUpperInvariant(),
                        @"^\s*([A-Z0-9]{6,24})\s*$")
                })
                .Where(x =>
                    x.Match.Success &&
                    x.Match.Groups[1].Value.Count(char.IsDigit) >= 4 &&
                    x.Line.Box is { Length: >= 4 })
                .Select(x =>
                {
                    var box = x.Line.Box!;
                    var centerY = (box[1] + box[3]) / 2d;
                    return new
                    {
                        Value = x.Match.Groups[1].Value,
                        Distance = Math.Abs(centerY - labelCenterY)
                    };
                })
                .Where(x => x.Distance <= 60)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (candidates is not null)
            {
                return candidates.Value;
            }
        }

        return null;
    }

    private static string? FindName(
        IReadOnlyList<IraqiNationalIdOcrLine> lines,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> noiseWords)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var labelForm = LabelForm(lines[i].Text);
            if (!labels.Any(label =>
                    labelForm.StartsWith(
                        LabelForm(label),
                        StringComparison.Ordinal)))
            {
                continue;
            }
            var inline = ExtractInlineName(
                lines[i].Text,
                labels,
                noiseWords);
            if (!string.IsNullOrWhiteSpace(inline))
            {
                return inline;
            }

            var neighbor = FindNeighborName(
                lines,
                i,
                labels,
                noiseWords);
            if (!string.IsNullOrWhiteSpace(neighbor))
            {
                return neighbor;
            }
        }

        return null;
    }

    private static string? FindNeighborName(
        IReadOnlyList<IraqiNationalIdOcrLine> lines,
        int labelIndex,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> noiseWords)
    {
        var label = lines[labelIndex];
        var candidates = new List<(double Score, string Value)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == labelIndex)
            {
                continue;
            }

            var value = CleanName(lines[i].Text);
            if (!IsPlausibleName(value) ||
                LooksLikeLabel(lines[i].Text))
            {
                continue;
            }

            if (label.Box is { Length: >= 4 } labelBox &&
                lines[i].Box is { Length: >= 4 } candidateBox)
            {
                var labelCenterY = (labelBox[1] + labelBox[3]) / 2d;
                var candidateCenterY =
                    (candidateBox[1] + candidateBox[3]) / 2d;
                var verticalDistance =
                    Math.Abs(labelCenterY - candidateCenterY);

                if (verticalDistance > 48)
                {
                    continue;
                }
                var horizontalGap = Math.Max(
                    0,
                    labelBox[0] - candidateBox[2]);

                if (candidateBox[0] > labelBox[0] + 30)
                {
                    continue;
                }

                candidates.Add((
                    verticalDistance + horizontalGap / 20d,
                    value));
                continue;
            }

            if (Math.Abs(i - labelIndex) == 1)
            {
                candidates.Add((100 + Math.Abs(i - labelIndex), value));
            }
        }

        return candidates
            .OrderBy(x => x.Score)
            .Select(x => x.Value)
            .FirstOrDefault();
    }
    private static string? ExtractInlineName(
        string value,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> noiseWords)
    {
        var result = value;

        foreach (var token in labels.Concat(noiseWords))
        {
            result = Regex.Replace(
                result,
                Regex.Escape(token),
                " ",
                RegexOptions.IgnoreCase);
        }

        result = CleanName(result);
        return IsPlausibleName(result)
            ? result
            : null;
    }

    private static bool LooksLikeLabel(string value)
    {
        var label = LabelForm(value);
        return label.Contains("الاسم", StringComparison.Ordinal) ||
               label.Contains("الاب", StringComparison.Ordinal) ||
               label.Contains("الجد", StringComparison.Ordinal) ||
               label.Contains("اللقب", StringComparison.Ordinal) ||
               label.Contains("الام", StringComparison.Ordinal) ||
               label.Contains("الجنس", StringComparison.Ordinal) ||
               label.Contains("فصيلة", StringComparison.Ordinal);
    }
    private static bool IsPlausibleName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var words = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        return words.Length is >= 1 and <= 4 &&
               value.Any(IsArabicLetter) &&
               !value.Any(char.IsDigit);
    }

    private static string CleanName(string value)
    {
        var chars = (value ?? string.Empty)
            .Where(c => IsArabicLetter(c) ||
                        char.IsWhiteSpace(c) ||
                        c is '-' or 'ـ')
            .ToArray();

        return string.Join(
            ' ',
            new string(chars)
                .Replace("ـ", string.Empty, StringComparison.Ordinal)
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
    }
    private static string LabelForm(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("أ", "ا", StringComparison.Ordinal)
            .Replace("إ", "ا", StringComparison.Ordinal)
            .Replace("آ", "ا", StringComparison.Ordinal)
            .Replace("ى", "ي", StringComparison.Ordinal)
            .Replace("ة", "ه", StringComparison.Ordinal)
            .Replace("ـ", string.Empty, StringComparison.Ordinal);

        return new string(normalized
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray());
    }

    private static string NormalizeDigits(string value)
    {
        var chars = (value ?? string.Empty).ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '\u0660' and <= '\u0669' =>
                    (char)('0' + chars[i] - '\u0660'),
                >= '\u06F0' and <= '\u06F9' =>
                    (char)('0' + chars[i] - '\u06F0'),
                _ => chars[i]
            };
        }

        return new string(chars);
    }

    private static bool IsArabicLetter(char value) =>
        value is >= '\u0600' and <= '\u06FF' &&
        char.IsLetter(value);
}
