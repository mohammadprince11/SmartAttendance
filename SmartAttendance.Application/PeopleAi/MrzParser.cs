using System.Globalization;

namespace SmartAttendance.Application.PeopleAi;

public sealed record MrzCheck(
    string Field,
    bool IsValid);

public sealed record MrzParseResult(
    string Format,
    string DocumentCode,
    string IssuingCountry,
    string DocumentNumber,
    string Nationality,
    DateOnly? DateOfBirth,
    string Sex,
    DateOnly? ExpiryDate,
    string Surname,
    IReadOnlyList<string> GivenNames,
    IReadOnlyList<MrzCheck> Checks,
    IReadOnlyList<string> RawLines)
{
    public bool AllRequiredChecksValid =>
        Checks.Count > 0 && Checks.All(x => x.IsValid);
}

public static class MrzParser
{
    private static readonly int[] Weights = [7, 3, 1];

    public static MrzParseResult? Parse(string? input)
    {
        var lines = NormalizeLines(input);
        return lines switch
        {
            { Count: 2 } when lines.All(x => x.Length == 44) =>
                ParseTd3(lines[0], lines[1]),
            { Count: 3 } when lines.All(x => x.Length == 30) =>
                ParseTd1(lines[0], lines[1], lines[2]),
            _ => null
        };
    }

    public static bool ValidateCheckDigit(
        string value,
        char expected)
    {
        if (!char.IsDigit(expected))
        {
            return false;
        }

        return ComputeCheckDigit(value) == expected - '0';
    }

    public static int ComputeCheckDigit(string value)
    {
        var sum = 0;
        var normalized = (value ?? string.Empty).ToUpperInvariant();

        for (var i = 0; i < normalized.Length; i++)
        {
            sum += CharacterValue(normalized[i]) *
                   Weights[i % Weights.Length];
        }

        return sum % 10;
    }

    private static MrzParseResult ParseTd3(
        string line1,
        string line2)
    {
        var names = ParseNames(line1[5..44]);
        var documentNumber = Clean(line2[0..9]);
        var birthText = line2[13..19];
        var expiryText = line2[21..27];

        var checks = new List<MrzCheck>
        {
            new("DocumentNumber",
                ValidateCheckDigit(line2[0..9], line2[9])),
            new("DateOfBirth",
                ValidateCheckDigit(birthText, line2[19])),
            new("ExpiryDate",
                ValidateCheckDigit(expiryText, line2[27])),
            new("Composite",
                ValidateCheckDigit(
                    line2[0..10] +
                    line2[13..20] +
                    line2[21..43],
                    line2[43]))
        };

        return new MrzParseResult(
            "TD3",
            Clean(line1[0..2]),
            Clean(line1[2..5]),
            documentNumber,
            Clean(line2[10..13]),
            ParseMrzDate(birthText, expiry: false),
            Clean(line2[20..21]),
            ParseMrzDate(expiryText, expiry: true),
            names.Surname,
            names.GivenNames,
            checks,
            [line1, line2]);
    }

    private static MrzParseResult ParseTd1(
        string line1,
        string line2,
        string line3)
    {
        var names = ParseNames(line3);
        var documentNumber = Clean(line1[5..14]);
        var birthText = line2[0..6];
        var expiryText = line2[8..14];

        var checks = new List<MrzCheck>
        {
            new("DocumentNumber",
                ValidateCheckDigit(line1[5..14], line1[14])),
            new("DateOfBirth",
                ValidateCheckDigit(birthText, line2[6])),
            new("ExpiryDate",
                ValidateCheckDigit(expiryText, line2[14])),
            new("Composite",
                ValidateCheckDigit(
                    line1[5..30] + line2[0..7] +
                    line2[8..15] + line2[18..29],
                    line2[29]))
        };

        return new MrzParseResult(
            "TD1",
            Clean(line1[0..2]),
            Clean(line1[2..5]),
            documentNumber,
            Clean(line2[15..18]),
            ParseMrzDate(birthText, expiry: false),
            Clean(line2[7..8]),
            ParseMrzDate(expiryText, expiry: true),
            names.Surname,
            names.GivenNames,
            checks,
            [line1, line2, line3]);
    }

    private static (string Surname, IReadOnlyList<string> GivenNames)
        ParseNames(string raw)
    {
        var parts = raw
            .Split("<<", 2, StringSplitOptions.None);

        var surname = HumanizeName(parts[0]);
        var given = parts.Length < 2
            ? []
            : parts[1]
                .Split('<', StringSplitOptions.RemoveEmptyEntries)
                .Select(HumanizeName)
                .Where(x => x.Length > 0)
                .ToArray();

        return (surname, given);
    }

    private static string HumanizeName(string value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split('<', StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static string Clean(string value) =>
        (value ?? string.Empty)
            .Replace("<", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static int CharacterValue(char value) =>
        value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'A' and <= 'Z' => value - 'A' + 10,
            '<' => 0,
            _ => 0
        };

    private static DateOnly? ParseMrzDate(
        string text,
        bool expiry)
    {
        if (text.Length != 6 ||
            !int.TryParse(text[0..2], out var yy) ||
            !int.TryParse(text[2..4], out var month) ||
            !int.TryParse(text[4..6], out var day))
        {
            return null;
        }

        var currentYear = DateTime.UtcNow.Year;
        var currentCentury = currentYear / 100 * 100;

        int year;
        if (expiry)
        {
            year = currentCentury + yy;
            if (year < currentYear - 20)
            {
                year += 100;
            }
            else if (year > currentYear + 80)
            {
                year -= 100;
            }
        }
        else
        {
            year = currentCentury + yy;
            if (year > currentYear)
            {
                year -= 100;
            }

            if (year < currentYear - 120)
            {
                year += 100;
            }
        }

        return DateOnly.TryParseExact(
            $"{year:0000}-{month:00}-{day:00}",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
            ? result
            : null;
    }

    private static List<string> NormalizeLines(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Replace((char)13, (char)10)
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new string(
                line.Trim()
                    .ToUpperInvariant()
                    .Where(c => char.IsLetterOrDigit(c) || c == '<')
                    .ToArray()))
            .Where(line => line.Length > 0)
            .ToList();
    }
}
