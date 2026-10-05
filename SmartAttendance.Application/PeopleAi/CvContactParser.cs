using System.Text.RegularExpressions;

namespace SmartAttendance.Application.PeopleAi;

public sealed record CvContactParseResult(
    string? Phone,
    string? Email);

public static class CvContactParser
{
    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+[.][A-Z]{2,}",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new(
        @"[+]?[0-9][0-9 ()-]{8,}[0-9]",
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public static CvContactParseResult Parse(
        IEnumerable<string?> lines)
    {
        string? phone = null;
        string? email = null;

        foreach (var source in lines)
        {
            var text = (source ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (email is null)
            {
                var emailMatch = EmailPattern.Match(text);
                if (emailMatch.Success)
                {
                    email = emailMatch.Value.Trim();
                }
            }

            if (phone is null)
            {
                var phoneMatch = PhonePattern.Match(text);
                if (phoneMatch.Success)
                {
                    var raw = phoneMatch.Value.Trim();
                    var digits = new string(
                        raw.Where(char.IsDigit).ToArray());

                    if (digits.Length is >= 10 and <= 15)
                    {
                        phone = raw.StartsWith(
                                "+",
                                StringComparison.Ordinal)
                            ? "+" + digits
                            : digits;
                    }
                }
            }

            if (phone is not null && email is not null)
            {
                break;
            }
        }

        return new CvContactParseResult(phone, email);
    }
}
