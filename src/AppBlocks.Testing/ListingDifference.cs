using System.Text;

namespace AppBlocks.Testing;

/// <summary>The lines by which two listings differ, for the message of a failed check.</summary>
internal static class ListingDifference
{
    /// <summary>
    /// The lines only the first text has (<c>-</c>) and the lines only the second has (<c>+</c>), each group under the heading it
    /// belongs to: the nearest line above it that starts in the first column.
    /// </summary>
    public static string Of(string before, string after)
    {
        var old = before.Split('\n');
        var now = after.Split('\n');
        // Lines the two texts start and end with are the same in both, which leaves a long listing a short middle to compare.
        var start = 0;
        while (start < old.Length && start < now.Length && old[start] == now[start]) start++;
        var end = 0;
        while (end < old.Length - start && end < now.Length - start && old[old.Length - 1 - end] == now[now.Length - 1 - end]) end++;
        string? heading = null, shownUnder = null;
        for (var i = 0; i < start; i++) if (IsHeading(old[i])) heading = old[i];
        old = old[start..(old.Length - end)];
        now = now[start..(now.Length - end)];

        var message = new StringBuilder();
        var changes = 0;
        const int limit = 80;
        void Changed(char mark, string line)
        {
            if (++changes > limit) return;
            if (heading is not null && heading != shownUnder && line != heading) message.Append("  ").Append(shownUnder = heading).Append('\n');
            message.Append(mark).Append(' ').Append(line).Append('\n');
        }
        if ((long)old.Length * now.Length > 4_000_000)
        {
            // Too much changed to line the two up: the start of each side says where they part.
            foreach (var line in old.Take(limit / 2)) Changed('-', line);
            foreach (var line in now.Take(limit / 2)) Changed('+', line);
            return message.Append($"... the texts differ in {old.Length} lines of the first and {now.Length} of the second\n").ToString();
        }
        // The longest common subsequence of the two texts' lines; a line outside it was removed or added.
        var common = new int[old.Length + 1, now.Length + 1];
        for (var i = old.Length - 1; i >= 0; i--)
            for (var j = now.Length - 1; j >= 0; j--)
                common[i, j] = old[i] == now[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        for (int i = 0, j = 0; i < old.Length || j < now.Length;)
        {
            if (i < old.Length && j < now.Length && old[i] == now[j])
            {
                if (IsHeading(old[i])) heading = old[i];
                i++; j++;
            }
            else if (j == now.Length || i < old.Length && common[i + 1, j] >= common[i, j + 1]) Changed('-', old[i++]);
            else
            {
                if (IsHeading(now[j])) heading = now[j];
                Changed('+', now[j++]);
            }
        }
        if (changes > limit) message.Append($"... and {changes - limit} more changed lines\n");
        return message.ToString();
    }

    private static bool IsHeading(string line) => line.Length > 0 && !line.StartsWith(' ');
}
