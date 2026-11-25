using System.Data.Common;
using System.Text;
using AppBlocks.Databases;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace AppBlocks.Testing;

/// <summary>An application database's scripts and the files a test compares them with disagree; the message says where.</summary>
public sealed class SchemaCheckException(string message) : Exception(message);

/// <summary>
/// Checks an application's tests run on the scripts of its application database (<see cref="ApplicationSchema"/>), without a site:
/// that the scripts make the schema a listing under source control shows, that a second start changes nothing, that a script
/// which has shipped keeps its text and its name, and that every dialect has the same run-once scripts. A check that fails
/// throws <see cref="SchemaCheckException"/>.
/// </summary>
/// <remarks>
/// The listing and the list of shipped scripts are files the application keeps beside its scripts. Running the tests with
/// <see cref="ListingVariable"/> or <see cref="ShippedVariable"/> set to <c>write</c> writes them from the scripts, for a
/// review to read as a diff.
/// </remarks>
public static class SchemaCheck
{
    /// <summary>The environment variable that, set to <c>write</c>, makes <see cref="Sqlite(ApplicationSchema, string, string)"/> and <see cref="SqlServer(ApplicationSchema, string, string, string)"/> write the listing.</summary>
    public const string ListingVariable = "APPBLOCKS_SCHEMA_LISTING";

    /// <summary>The environment variable that, set to <c>write</c>, makes <see cref="Shipped(string, string)"/> list every run-once script as shipped.</summary>
    public const string ShippedVariable = "APPBLOCKS_SCHEMA_SHIPPED";

    /// <summary>The dialect folders a schema folder can hold.</summary>
    private static readonly string[] DialectFolders = ["sqlite", "sqlserver", "postgresql", "oracle"];

    /// <summary>
    /// Ensures the schema on a new SQLite database at two starts and compares what it made with the listing at
    /// <paramref name="listingPath"/>, or writes that file when <see cref="ListingVariable"/> is <c>write</c>.
    /// </summary>
    /// <param name="schema">The schema as the application registers it, with the steps that ensure its other schemas.</param>
    /// <param name="listingPath">The listing file, such as <c>Schema/current/sqlite.txt</c> in the application's source.</param>
    /// <param name="description">What the database is, for the top of the file; one or more lines.</param>
    /// <exception cref="SchemaCheckException">A second start changed the schema, or the scripts and the listing differ.</exception>
    public static void Sqlite(ApplicationSchema schema, string listingPath, string description)
        => Sqlite(schema, listingPath, description, Writing(ListingVariable));

    internal static void Sqlite(ApplicationSchema schema, string listingPath, string description, bool write)
    {
        var file = Path.Combine(Path.GetTempPath(), "appblocks-schema-" + Guid.NewGuid().ToString("N") + ".sqlite.db");
        try
        {
            // Each start has its own connection string, because a schema is ensured once per process for each one.
            var listing = Install(schema, start => new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false, DefaultTimeout = 30 + start }.ToString()));
            Compare(listingPath, description, "SQLite", listing, write);
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// Ensures the schema on a new SQL Server database at two starts and compares what it made with the listing at
    /// <paramref name="listingPath"/>, or writes that file when <see cref="ListingVariable"/> is <c>write</c>. Every constraint
    /// must have a name: a later script can drop or change one only by its name, and a name the server makes up differs in every database.
    /// </summary>
    /// <param name="schema">The schema as the application registers it, with the steps that ensure its other schemas.</param>
    /// <param name="server">A connection string to a server where the account may create and drop a database; the check uses one of its own.</param>
    /// <param name="listingPath">The listing file, such as <c>Schema/current/sqlserver.txt</c> in the application's source.</param>
    /// <param name="description">What the database is, for the top of the file; one or more lines.</param>
    /// <exception cref="SchemaCheckException">A second start changed the schema, a constraint has no name, or the scripts and the listing differ.</exception>
    public static void SqlServer(ApplicationSchema schema, string server, string listingPath, string description)
        => SqlServer(schema, server, listingPath, description, Writing(ListingVariable));

    internal static void SqlServer(ApplicationSchema schema, string server, string listingPath, string description, bool write)
    {
        var name = "AppBlocksSchema_" + Guid.NewGuid().ToString("N");
        var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ToString();
        Execute(master, $"CREATE DATABASE [{name}]");
        try
        {
            var listing = Install(schema, start => new SqlConnection(
                new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false, ApplicationName = "AppBlocks schema check, start " + (start + 1) }.ToString()));
            var unnamed = listing.Split('\n').Where(line => line.Contains(SchemaListing.Unnamed, StringComparison.Ordinal)).Select(line => line.Trim()).ToArray();
            if (unnamed.Length > 0)
                throw new SchemaCheckException("Give every constraint a name in the script that creates it, so a later script can drop or change it: " + string.Join("; ", unnamed));
            Compare(listingPath, description, "SQL Server", listing, write);
        }
        finally { Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"); }
    }

    /// <summary>
    /// Checks the run-once scripts under <paramref name="schemaRoot"/> against the list of shipped scripts at
    /// <paramref name="listPath"/>: each listed script is there under its name with its text unchanged, and every other script
    /// sorts after the listed ones of its dialect. When <see cref="ShippedVariable"/> is <c>write</c>, it writes the list from
    /// every script instead, which is the step for a release.
    /// </summary>
    /// <param name="schemaRoot">The schema folder that holds the dialect folders, as <see cref="ApplicationSchema.FromDirectory"/> takes it.</param>
    /// <param name="listPath">The list, such as <c>Schema/shipped.txt</c>: one <c>dialect/migrations/file hash</c> to a line, and <c>#</c> comments.</param>
    /// <exception cref="SchemaCheckException">The list is missing or malformed, or a shipped script changed, is gone, or sorts after one that has not shipped.</exception>
    public static void Shipped(string schemaRoot, string listPath) => Shipped(schemaRoot, listPath, Writing(ShippedVariable));

    internal static void Shipped(string schemaRoot, string listPath, bool write)
    {
        var dialects = DialectFolders.Where(dialect => Directory.Exists(Path.Combine(schemaRoot, dialect))).ToArray();
        if (write)
        {
            var text = new StringBuilder($"""
                # The run-once scripts that have shipped in a release, each with the SHA-256 that AppBlocks records for it in the version table.
                # AppBlocks.Testing's SchemaCheck.Shipped checks that each one keeps this text and this file name, and that every other script runs after it.
                # At a release, write this file again by running the tests with {ShippedVariable}=write, and commit it with the release.

                """.Replace("\r\n", "\n"));
            foreach (var dialect in dialects)
                foreach (var name in RunOnceScripts(schemaRoot, dialect))
                    text.Append(dialect).Append('/').Append(ApplicationSchema.MigrationsFolder).Append('/').Append(name).Append(' ')
                        .Append(ApplicationDatabase.MigrationHash(File.ReadAllBytes(ScriptPath(schemaRoot, dialect, name)))).Append('\n');
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(listPath))!);
            File.WriteAllText(listPath, text.ToString(), new UTF8Encoding(false));
            return;
        }
        if (!File.Exists(listPath))
            throw new SchemaCheckException($"{listPath} is missing. Before the first release it holds comments only; from then on, write it with {ShippedVariable}=write when a release ships.");
        var shipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(listPath).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')))
        {
            var entry = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = entry[0].Split('/');
            if (entry.Length != 2 || path.Length != 3 || path[1] != ApplicationSchema.MigrationsFolder || !DialectFolders.Contains(path[0]))
                throw new SchemaCheckException($"{listPath} has the line \"{line}\"; an entry is <dialect>/{ApplicationSchema.MigrationsFolder}/<file> and a hash.");
            shipped[entry[0]] = entry[1];
        }
        foreach (var (script, hash) in shipped)
        {
            var parts = script.Split('/');
            var file = ScriptPath(schemaRoot, parts[0], parts[2]);
            if (!File.Exists(file))
                throw new SchemaCheckException($"{script} has shipped and its file is gone. AppBlocks would run a renamed script a second time on an installed database: restore the file under its name.");
            if (!hash.Equals(ApplicationDatabase.MigrationHash(File.ReadAllBytes(file)), StringComparison.OrdinalIgnoreCase))
                throw new SchemaCheckException($"{script} has shipped and its text has changed. AppBlocks stops opening a database that ran the earlier text: restore the script and put the change in a new one.");
        }
        foreach (var dialect in dialects)
        {
            var prefix = dialect + "/" + ApplicationSchema.MigrationsFolder + "/";
            var last = shipped.Keys.Where(script => script.StartsWith(prefix, StringComparison.Ordinal)).Select(script => script[prefix.Length..]).Order(StringComparer.Ordinal).LastOrDefault();
            if (last is null) continue;
            foreach (var name in RunOnceScripts(schemaRoot, dialect).Where(name => !shipped.ContainsKey(prefix + name)))
                if (string.CompareOrdinal(name, last) < 0)
                    throw new SchemaCheckException($"{prefix}{name} sorts before {last}, which has shipped, so a new database and an updated one would run them in different orders. Give it a later number.");
        }
    }

    /// <summary>
    /// Checks that every dialect folder under <paramref name="schemaRoot"/> has run-once scripts of the same file names, so each
    /// change reaches every kind of database the application runs on.
    /// </summary>
    /// <exception cref="SchemaCheckException">A script is in one dialect's folder and missing from another's.</exception>
    public static void SameScriptsInEveryDialect(string schemaRoot)
    {
        var dialects = DialectFolders.Where(dialect => Directory.Exists(Path.Combine(schemaRoot, dialect))).ToArray();
        var all = dialects.SelectMany(dialect => RunOnceScripts(schemaRoot, dialect)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = dialects.SelectMany(dialect => all.Except(RunOnceScripts(schemaRoot, dialect), StringComparer.Ordinal)
            .Select(name => $"{dialect}/{ApplicationSchema.MigrationsFolder}/{name}")).ToArray();
        if (missing.Length > 0)
            throw new SchemaCheckException("Each change needs a run-once script of the same name in every dialect's folder. Missing: " + string.Join(", ", missing));
    }

    /// <summary>A dialect's run-once scripts, in the order AppBlocks runs them.</summary>
    private static string[] RunOnceScripts(string schemaRoot, string dialect)
    {
        var folder = Path.Combine(schemaRoot, dialect, ApplicationSchema.MigrationsFolder);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.sql").Select(file => Path.GetFileName(file)!).Order(StringComparer.Ordinal).ToArray()
            : [];
    }

    private static string ScriptPath(string schemaRoot, string dialect, string name) => Path.Combine(schemaRoot, dialect, ApplicationSchema.MigrationsFolder, name);

    private static bool Writing(string variable) => string.Equals(Environment.GetEnvironmentVariable(variable), "write", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ensures the schema on one database at two starts of the application and returns its listing, which the second start must leave as it was.</summary>
    private static string Install(ApplicationSchema schema, Func<int, DbConnection> connect)
    {
        string? first = null;
        for (var start = 0; start < 2; start++)
        {
            using var connection = connect(start);
            connection.Open();
            ApplicationDatabase.Ensure(connection, schema);
            var listing = SchemaListing.Of(connection, schema.Table);
            if (first is null) first = listing;
            else if (listing != first)
                throw new SchemaCheckException("A second start changed the schema the first one made. Scripts that run at every start must leave a current database as it is.\n" + Difference(first, listing));
        }
        return first!;
    }

    private static void Compare(string listingPath, string description, string database, string listing, bool write)
    {
        var document = $"""
            {description.Replace("\r\n", "\n").Trim()}
            A new {database} database after its scripts have run, as AppBlocks.Testing's SchemaCheck lists it. The tests compare
            this file with the scripts. To change the schema, change the scripts, then write this file again by running the tests
            with {ListingVariable}=write.


            """.Replace("\r\n", "\n") + listing;
        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(listingPath))!);
            File.WriteAllText(listingPath, document, new UTF8Encoding(false));
            return;
        }
        if (!File.Exists(listingPath)) throw new SchemaCheckException($"{listingPath} is missing. Run the tests with {ListingVariable}=write to write it.");
        var recorded = File.ReadAllText(listingPath).Replace("\r\n", "\n");
        if (recorded != document)
            throw new SchemaCheckException($"The scripts no longer make the schema that {listingPath} lists.\n" + Difference(recorded, document)
                + $"If the change is intended, run the tests with {ListingVariable}=write and review the file's diff.");
    }

    private static string Difference(string before, string after) => ListingDifference.Of(before, after);

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 90;
        command.ExecuteNonQuery();
    }
}
