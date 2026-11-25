namespace AppBlocks.Databases;

/// <summary>The single chain of releases shipped by one project. Versions compare numerically.</summary>
internal sealed class ReleasePlan
{
    internal sealed record Release(Version Version, string Folder, ApplicationSchema Schema, bool Snapshot);
    internal Release Base { get; }
    internal Release Install { get; }
    internal List<Release> Transitions { get; } = [];

    internal ReleasePlan(IEnumerable<string> directories, string? snapshot, Func<string, ApplicationSchema> schema)
    {
        var folders = directories.Where(s => s.Length > 0 && char.IsAsciiDigit(s[0])).ToArray();
        var baseFolder = Single("-Base");
        var currentFolder = Single("-Current");
        if (snapshot != null && snapshot != baseFolder && snapshot != currentFolder)
            throw new InvalidOperationException($"Unknown installation snapshot {snapshot}.");
        var first = Parse(baseFolder[..^"-Base".Length]);
        var current = Parse(currentFolder[..^"-Current".Length]);
        Base = new(first, baseFolder, schema(baseFolder), true);
        Install = snapshot == baseFolder ? Base : new(current, currentFolder, schema(currentFolder), true);
        if (snapshot == baseFolder) return;

        var links = folders.Except([baseFolder, currentFolder]).Select(folder =>
        {
            var ends = folder.Split('-');
            if (ends.Length != 2) throw new InvalidOperationException($"Invalid migration folder {folder}.");
            return (From: Parse(ends[0]), To: Parse(ends[1]), Folder: folder);
        }).OrderBy(link => link.From);
        var at = first;
        foreach (var link in links)
        {
            if (link.From != at || link.To <= at)
                throw new InvalidOperationException($"Migration {link.Folder} does not continue the chain from {at}.");
            Transitions.Add(new(link.To, link.Folder, schema(link.Folder), false));
            at = link.To;
        }
        if (at != current) throw new InvalidOperationException($"Migrations end at {at}, but the clean installer is {current}.");

        string Single(string suffix) => folders.Where(s => s.EndsWith(suffix, StringComparison.Ordinal)).ToArray() is [var folder]
            ? folder : throw new InvalidOperationException($"Migrations must have exactly one *{suffix} folder.");
    }

    internal static Version Parse(string value) =>
        Version.TryParse(value, out var version) && version.Build >= 0 && version.Revision < 0 && version.ToString() == value
            ? version : throw new InvalidOperationException($"Expected a major.minor.patch migration version, found {value}.");
}
