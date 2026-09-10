namespace LabAi.Application.Chunking;

/// <summary>
/// Splits text into atomic units (paragraph lines and sentences inside them) for window packing.
/// A sentence terminator (<c>.</c> <c>!</c> <c>?</c>) ends a unit only when followed by whitespace
/// or the end of the line and the next non-whitespace character is not a digit, so decimals
/// (<c>2.0%</c>) and abbreviation-plus-number patterns (<c>п. 4.2</c>, <c>таб. 3</c>) stay in one
/// unit. A merged unit is only ever longer, never truncated — no text can be lost by this rule.
/// </summary>
internal static class TextUnitSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        var units = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            var start = 0;
            var i = 0;
            while (i < line.Length)
            {
                if (!IsSentenceTerminator(line[i]))
                {
                    i++;
                    continue;
                }

                var runEnd = i;
                while (runEnd < line.Length && IsSentenceTerminator(line[runEnd]))
                    runEnd++;

                if (StartsNewSentence(line, runEnd))
                {
                    AddUnit(units, line[start..runEnd]);
                    start = runEnd;
                }

                i = runEnd;
            }

            AddUnit(units, line[start..]);
        }

        return units;
    }

    private static bool IsSentenceTerminator(char c) => c is '.' or '!' or '?';

    private static bool StartsNewSentence(string line, int index)
    {
        if (index >= line.Length)
            return true;
        if (!char.IsWhiteSpace(line[index]))
            return false;

        var k = index;
        while (k < line.Length && char.IsWhiteSpace(line[k]))
            k++;
        return k >= line.Length || !char.IsDigit(line[k]);
    }

    private static void AddUnit(List<string> units, string candidate)
    {
        if (candidate.Trim().Length > 0)
            units.Add(candidate.Trim());
    }
}
