using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AppBlocks.Databases;

public static partial class ApplicationDatabase
{
    private static async Task<int> ApplyReleaseAsync(DbConnection connection, ApplicationSchema schema, ReleasePlan plan,
        string dialect, Dictionary<string, string> applied, bool sync, CancellationToken ct)
    {
        var prefix = schema.Name + "/release/";
        var position = Position();
        var count = 0;
        if (position is null)
        {
            var original = plan.Base.Schema.Migrations(dialect);
            FromThisBuild(schema, dialect, original, applied);
            var legacy = applied.Keys.Any(key => key.StartsWith(schema.Name + "/", StringComparison.Ordinal))
                || original.Any(file => applied.ContainsKey("migrations/" + file.Name))
                || plan.Base.Schema.Scripts(dialect).Any(file => applied.ContainsKey(file.Name));
            var install = legacy ? plan.Base : plan.Install;

            // An older and a newer build can both see an empty database. The same claim protects
            // installation whichever build wins. Its release marker commits with its SQL.
            await RunOnceAsync(connection, dialect, schema.Table, schema.Name + "/installed", MigrationHash([]), "installation of " + schema.Name,
                async transaction =>
                {
                    count += await ExecuteReleaseAsync(connection, transaction, schema, install, dialect, legacy ? applied : null, sync, ct);
                    await RecordAsync(connection, transaction, dialect, schema.Table, prefix + install.Version, MigrationHash([]), sync, ct);
                }, sync, ct);
            applied = await AppliedAsync(connection, schema.Table, sync, ct);
            position = Position() ?? throw new ApplicationSchemaException($"Missing release record for {schema.Name}.");
        }
        if (position != plan.Base.Version && !plan.Transitions.Any(step => step.Version == position))
            throw new ApplicationSchemaException($"The installed release {position} of {schema.Name} is outside this build's chain to {plan.Install.Version}.")
                { DatabaseFromAnotherBuild = true };

        // Clean installs and upgraded databases may reach the same version by different scripts.
        // Check the immutable snapshots and transitions this particular database actually used.
        foreach (var release in plan.Transitions.Prepend(plan.Base).Append(plan.Install))
        {
            if (applied.TryGetValue(HistoryKey(schema, release), out var hash)
                && hash != ReleaseHash(release.Schema.Scripts(dialect), release.Schema.Migrations(dialect)))
                throw new ApplicationSchemaException($"{schema.Name} migration {release.Folder} has changed since it ran. Restore its released scripts and put the change in a new transition.");
        }

        foreach (var step in plan.Transitions.Where(step => step.Version > position))
        {
            try
            {
                await RunOnceAsync(connection, dialect, schema.Table, prefix + step.Version, MigrationHash([]), $"{schema.Name} migration {step.Folder}",
                    async transaction => { count += await ExecuteReleaseAsync(connection, transaction, schema, step, dialect, null, sync, ct); }, sync, ct);
            }
            catch (DbException error)
            {
                // Read access to the previous release's tables cannot stand in for a successful upgrade.
                throw new ApplicationSchemaException($"Could not upgrade {schema.Name} through {step.Folder}: {error.Message}", error) { UpgradeFailed = true };
            }
        }
        return count;

        Version? Position() => applied.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => ReleasePlan.Parse(key[prefix.Length..])).Max();
    }

    private static string HistoryKey(ApplicationSchema schema, ReleasePlan.Release release) => schema.Name + "/history/" +
        (release.Snapshot && release.Version == schema.Releases!.Base.Version ? schema.Releases.Base.Folder : release.Folder);

    private static string ReleaseHash(IReadOnlyList<(string Name, byte[] Content)> scripts, IReadOnlyList<(string Name, byte[] Content)> migrations)
    {
        var files = scripts.Concat(migrations.Select(file => (Name: "migrations/" + file.Name, file.Content)));
        var listing = string.Concat(files.Select(file => file.Name + "\0" + MigrationHash(file.Content) + "\n"));
        return MigrationHash(Encoding.UTF8.GetBytes(listing));
    }

    private static async Task<int> ExecuteReleaseAsync(DbConnection connection, DbTransaction transaction, ApplicationSchema schema,
        ReleasePlan.Release release, string dialect, Dictionary<string, string>? legacy, bool sync, CancellationToken ct)
    {
        var scripts = release.Schema.Scripts(dialect);
        var migrations = release.Schema.Migrations(dialect);
        if (release.Snapshot && scripts.Count + migrations.Count == 0)
            throw new ApplicationSchemaException($"Missing {dialect} installer for {schema.Name} in {release.Folder}.");

        foreach (var (name, content) in scripts)
        {
            await ExecuteFile(name, content, tolerateExistingColumn: release.Snapshot);
            await ClaimLegacyRow(name);
        }
        foreach (var (name, content) in migrations)
        {
            var key = "migrations/" + name;
            if (legacy != null && (legacy.TryGetValue(schema.Name + "/" + key, out var hash) || legacy.TryGetValue(key, out hash)))
            {
                Unchanged(key, schema.Table, hash, MigrationHash(content));
                await ClaimLegacyRow(key);
            }
            else await ExecuteFile(name, content, tolerateExistingColumn: false);
        }
        await RecordAsync(connection, transaction, dialect, schema.Table, HistoryKey(schema, release), ReleaseHash(scripts, migrations), sync, ct);
        return scripts.Count + migrations.Count;

        async Task ClaimLegacyRow(string key)
        {
            var owned = schema.Name + "/" + key;
            if (legacy?.ContainsKey(key) != true || legacy.ContainsKey(owned)) return;
            await ExecuteAsync(connection, transaction, dialect, $"UPDATE {schema.Table} SET SCRIPT = {Marker(dialect, "owned")} WHERE SCRIPT = {Marker(dialect, "old")}",
                sync, ct, ("owned", owned), ("old", key));
        }

        async Task ExecuteFile(string file, byte[] bytes, bool tolerateExistingColumn)
        {
            try
            {
                foreach (var sql in Statements(dialect, Text(bytes)))
                {
                    try { await ExecuteAsync(connection, transaction, dialect, sql, sync, ct); }
                    catch (SqliteException error) when (tolerateExistingColumn && error.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) && AddColumn.IsMatch(sql)) { }
                }
            }
            catch (DbException error)
            {
                throw new ApplicationSchemaException($"{schema.Name} script {release.Folder}/{dialect}/{file} failed: {error.Message}", error);
            }
        }
    }
}
