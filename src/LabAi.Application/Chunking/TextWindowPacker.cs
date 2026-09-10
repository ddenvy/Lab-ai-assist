namespace LabAi.Application.Chunking;

/// <summary>
/// Packs ordered text units into windows whose joined text fits a character budget. Every window
/// after the first begins with the longest tail of the previous window that reaches the overlap
/// target, so a sentence cut at the budget boundary is repeated in full in the next window. The
/// tail never spans the entire previous window: if even the shortened tail does not leave room
/// for a new unit, tail units are shed instead of emitting a window that would only duplicate
/// already-covered content. A unit longer than the whole budget is hard-split by characters.
/// </summary>
internal static class TextWindowPacker
{
    public static IReadOnlyList<string> Pack(IReadOnlyList<string> units, int maxChars, int overlapChars)
    {
        var windows = new List<string>();
        var current = new List<string>();
        var currentLength = 0; // Length of string.Join('\n', current)
        var currentIsOverlapOnly = true; // No new unit has been added since the last flush.

        foreach (var unit in units)
        {
            if (unit.Length == 0)
                continue;

            if (unit.Length > maxChars)
            {
                Flush();
                for (var start = 0; start < unit.Length; start += maxChars)
                {
                    var length = Math.Min(maxChars, unit.Length - start);
                    windows.Add(unit[start..(start + length)]);
                }

                continue;
            }

            if (current.Count > 0 && currentLength + 1 + unit.Length > maxChars)
            {
                if (!currentIsOverlapOnly)
                    Flush();

                // Shed overlap units from the front until the new unit fits; a window made of
                // nothing but overlap would return near-duplicates at retrieval time.
                while (current.Count > 0 && currentLength + 1 + unit.Length > maxChars)
                {
                    currentLength -= current[0].Length + (current.Count > 1 ? 1 : 0);
                    current.RemoveAt(0);
                }
            }

            current.Add(unit);
            currentLength = current.Count == 1 ? unit.Length : currentLength + 1 + unit.Length;
            currentIsOverlapOnly = false;
        }

        Flush();
        return windows;

        void Flush()
        {
            if (current.Count == 0)
                return;

            windows.Add(string.Join('\n', current));

            var tailStart = current.Count;
            if (overlapChars > 0 && current.Count > 1)
            {
                var taken = 0;
                for (var i = current.Count - 1; i > 0; i--)
                {
                    taken += current[i].Length + 1;
                    tailStart = i;
                    if (taken >= overlapChars)
                        break;
                }
            }

            var tail = current.GetRange(tailStart, current.Count - tailStart);
            current = tail;
            currentLength = tail.Count == 0 ? 0 : string.Join('\n', tail).Length;
            currentIsOverlapOnly = true;
        }
    }
}
