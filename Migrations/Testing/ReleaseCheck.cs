using System.Reflection;

namespace AppBlocks.Testing;

/// <summary>Locates the snapshot selected by the build and verifies the first release's two installers.</summary>
public static class ReleaseCheck
{
    public static string Snapshot(string migrations, Assembly assembly)
    {
        var folder = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "InstallSnapshot").Value!;
        return Path.Combine(migrations, folder);
    }

    /// <summary>Every dialect gets the corresponding SQL files in each snapshot or transition that changes this component.</summary>
    public static void CheckSqlDialects(string migrations, string component, params string[] dialects)
    {
        foreach (var folder in Directory.GetDirectories(migrations).Where(path => char.IsAsciiDigit(Path.GetFileName(path)[0])))
        {
            var root = Path.Combine(folder, component);
            var first = Files(dialects[0]);
            if ((folder.EndsWith("-Base") || folder.EndsWith("-Current")) && first.Length == 0)
                throw new SchemaCheckException($"Missing SQL installer at {root}/{dialects[0]}.");
            foreach (var dialect in dialects.Skip(1))
                if (!first.SequenceEqual(Files(dialect), StringComparer.Ordinal))
                    throw new SchemaCheckException($"SQL scripts do not match between {root}/{dialects[0]} and {root}/{dialect}.");

            string[] Files(string dialect)
            {
                var directory = Path.Combine(root, dialect);
                return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.sql", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(directory, file)).Order(StringComparer.Ordinal).ToArray() : [];
            }
        }
    }

    public static void CheckBaseline(string migrations)
    {
        var baseline = Directory.GetDirectories(migrations, "*-Base").Single();
        var current = Directory.GetDirectories(migrations, "*-Current").Single();
        if (Path.GetFileName(current) != Path.GetFileName(baseline).Replace("-Base", "-Current", StringComparison.Ordinal)) return;
        var expected = Files(baseline);
        var actual = Files(current);
        if (expected.Length == 0 || !expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new SchemaCheckException("Base and Current must contain the same nonempty set of installation files.");
        foreach (var name in expected)
            if (!File.ReadAllBytes(Path.Combine(baseline, name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(current, name))))
                throw new SchemaCheckException($"Base and Current differ at {name}.");

        static string[] Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file)).Order(StringComparer.Ordinal).ToArray();
    }
}
