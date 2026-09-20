using System.Text;

namespace SmartAttendance.Application.PeopleAi;

public static class MrzOcrParser
{
    private sealed record Candidate(
        int Start,
        int End,
        string Text);

    public static MrzParseResult? Parse(
        IEnumerable<string?> source)
    {
        var fragments = source
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .ToList();

        if (fragments.Count == 0)
        {
            return null;
        }

        var direct = TryDirect(fragments, 44, 2) ??
                     TryDirect(fragments, 30, 3);
        if (direct is not null)
        {
            return direct;
        }
        foreach (var fragment in fragments)
        {
            if (fragment.Length == 88 &&
                LooksLikeFirstLine(fragment[..44], 44))
            {
                var parsed = MrzParser.Parse(
                    fragment[..44] + Environment.NewLine +
                    fragment[44..]);
                if (parsed is not null)
                {
                    return parsed;
                }
            }

            if (fragment.Length == 90 &&
                LooksLikeFirstLine(fragment[..30], 30))
            {
                var parsed = MrzParser.Parse(
                    fragment[..30] + Environment.NewLine +
                    fragment[30..60] + Environment.NewLine +
                    fragment[60..90]);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        var validatedTd3 = TryValidatedAcrossFragments(
            fragments,
            44,
            2);
        if (validatedTd3 is not null)
        {
            return validatedTd3;
        }

        var validatedTd1 = TryValidatedAcrossFragments(
            fragments,
            30,
            3);
        if (validatedTd1 is not null)
        {
            return validatedTd1;
        }

        var td3 = BuildCandidates(fragments, 44);
        for (var i = 0; i < td3.Count; i++)
        {
            for (var j = i + 1; j < td3.Count; j++)
            {
                if (td3[j].Start <= td3[i].End ||
                    td3[j].Start - td3[i].End > 4)
                {
                    continue;
                }

                if (!LooksLikeFirstLine(td3[i].Text, 44))
                {
                    continue;
                }

                var parsed = MrzParser.Parse(
                    td3[i].Text + Environment.NewLine +
                    td3[j].Text);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        var td1 = BuildCandidates(fragments, 30);
        for (var i = 0; i < td1.Count; i++)
        {
            for (var j = i + 1; j < td1.Count; j++)
            {
                if (!CanFollow(td1[i], td1[j]))
                {
                    continue;
                }
                for (var k = j + 1; k < td1.Count; k++)
                {
                    if (!CanFollow(td1[j], td1[k]))
                    {
                        continue;
                    }

                    if (!LooksLikeFirstLine(td1[i].Text, 30))
                    {
                        continue;
                    }

                    var parsed = MrzParser.Parse(
                        td1[i].Text + Environment.NewLine +
                        td1[j].Text + Environment.NewLine +
                        td1[k].Text);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
            }
        }

        return null;
    }

    private static MrzParseResult? TryValidatedAcrossFragments(
        IReadOnlyList<string> fragments,
        int lineLength,
        int lineCount)
    {
        var lines = fragments
            .Where(x => x.Length == lineLength)
            .ToList();

        if (lineCount == 2)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (!LooksLikeFirstLine(lines[i], lineLength))
                {
                    continue;
                }

                for (var j = i + 1; j < lines.Count; j++)
                {
                    var parsed = MrzParser.Parse(
                        lines[i] + Environment.NewLine +
                        lines[j]);
                    if (parsed?.AllRequiredChecksValid == true)
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        if (lineCount == 3)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (!LooksLikeFirstLine(lines[i], lineLength))
                {
                    continue;
                }

                for (var j = i + 1; j < lines.Count; j++)
                {
                    for (var k = j + 1; k < lines.Count; k++)
                    {
                        var parsed = MrzParser.Parse(
                            lines[i] + Environment.NewLine +
                            lines[j] + Environment.NewLine +
                            lines[k]);
                        if (parsed?.AllRequiredChecksValid == true)
                        {
                            return parsed;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool CanFollow(
        Candidate previous,
        Candidate next) =>
        next.Start > previous.End &&
        next.Start - previous.End <= 4;
    private static MrzParseResult? TryDirect(
        IReadOnlyList<string> fragments,
        int lineLength,
        int lineCount)
    {
        for (var start = 0;
             start + lineCount <= fragments.Count;
             start++)
        {
            var lines = fragments
                .Skip(start)
                .Take(lineCount)
                .ToArray();

            if (lines.All(x => x.Length == lineLength) &&
                LooksLikeFirstLine(lines[0], lineLength))
            {
                var parsed = MrzParser.Parse(
                    string.Join(Environment.NewLine, lines));
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        return null;
    }
    private static List<Candidate> BuildCandidates(
        IReadOnlyList<string> fragments,
        int targetLength)
    {
        var candidates = new List<Candidate>();

        for (var start = 0; start < fragments.Count; start++)
        {
            var builder = new StringBuilder();

            for (var end = start;
                 end < fragments.Count && end < start + 6;
                 end++)
            {
                if (builder.Length + fragments[end].Length >
                    targetLength)
                {
                    break;
                }

                builder.Append(fragments[end]);

                if (builder.Length == targetLength)
                {
                    candidates.Add(new Candidate(
                        start,
                        end,
                        builder.ToString()));
                    break;
                }
            }
        }

        return candidates;
    }

    private static string Normalize(string? value)
    {
        var builder = new StringBuilder();

        foreach (var c in (value ?? string.Empty)
                     .Trim()
                     .ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c == '<')
            {
                builder.Append(c);
            }
            else if (c is '«' or '‹')
            {
                builder.Append('<');
            }
        }

        var normalized = builder.ToString();

        // Common Iraqi TD1 OCR confusion: the filler '<' after the document
        // code I is read as D, producing IDIRQ... instead of I<IRQ....
        if (normalized.Length == 30 &&
            normalized.StartsWith("ID", StringComparison.Ordinal) &&
            normalized.Length >= 5 &&
            normalized[2..5].All(char.IsLetter))
        {
            normalized = "I<" + normalized[2..];
        }

        return normalized;
    }

    private static bool LooksLikeFirstLine(
        string value,
        int lineLength) =>
        lineLength switch
        {
            44 => value.StartsWith("P<", StringComparison.Ordinal),
            30 => value.StartsWith("I<", StringComparison.Ordinal) ||
                  value.StartsWith("A<", StringComparison.Ordinal) ||
                  value.StartsWith("C<", StringComparison.Ordinal),
            _ => false
        };
}
