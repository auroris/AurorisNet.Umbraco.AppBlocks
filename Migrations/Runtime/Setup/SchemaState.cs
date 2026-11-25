namespace AppBlocks.Setup;

/// <summary>What a reinstall or an uninstall did. Notes are what a person should know, one sentence each.</summary>
public sealed record SchemaChange(int Created, int Updated, int Deleted, IReadOnlyList<string> Notes);

/// <summary>Where a package's definitions stand on this site.</summary>
/// <param name="Declared">The definitions in the package's schema.</param>
/// <param name="Installed">How many of them are on the site, by key.</param>
/// <param name="Changed">The names of the installed definitions that this build has changed since the installer last wrote them on the site.</param>
/// <param name="Unrecorded">How many installed definitions have no record of the build that installed them, as those installed before records were kept.</param>
public sealed record DefinitionsPackage(string Id, string Name, string Version, int Declared, int Installed, IReadOnlyList<string> Changed, int Unrecorded);

/// <param name="Uninstalled">Whether the definitions were uninstalled and nothing has installed them since.</param>
public sealed record DefinitionsStatus(bool Uninstalled, IReadOnlyList<DefinitionsPackage> Packages);

/// <summary>An uninstall that would delete the site's content or properties, and so did nothing. Each problem says what to remove first.</summary>
public sealed class SchemaInUseException(IReadOnlyList<string> problems) : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
