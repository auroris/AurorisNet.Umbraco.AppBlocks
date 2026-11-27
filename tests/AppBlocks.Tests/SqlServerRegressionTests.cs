using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AppBlocks.DataSources;
using AppBlocks.Forms;

namespace AppBlocks.Tests;

/// <summary>
/// The SQL Server side of fixes whose SQLite tests in <see cref="DataSourceTests"/> pass either way: SQLite reads a negative LIMIT as
/// none and binds any value into any column, where SQL Server refuses the one and converts the other by the column's type.
/// </summary>
public sealed class SqlServerRegressionTests
{
    private const string Master = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=90;TrustServerCertificate=true";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);
    private static readonly DataSourceUser Owner = new("1001", "S-1-5-21-1", "jsmith", "jsmith@example.invalid", ["Planners"], false);

    [LocalDbFact]
    public Task A_row_limit_of_int_MaxValue_lists_the_rows_on_sql_server() => WithDatabaseAsync(async (connection, runtime) => {
        var rows = await Source(runtime, Tasks()).ListRowsAsync(new("projectId", "1"), Context(), int.MaxValue, default);
        Assert.True(rows.Status == 200, JsonSerializer.Serialize(rows));
        Assert.Equal("Paint the hangar", Assert.Single(rows.Rows!).Values["title"]);
        // Without the guard the limit's one row past it wraps to int.MinValue, and SQL Server refuses a negative TOP outright.
        var wrapped = SqlServerDialect.Instance.Select(SqlServerDialect.Instance.From(new TableName("dbo", "TASKS"), null), ["TASK_ID"], [], null, limit: unchecked(int.MaxValue + 1));
        Assert.Contains("TOP (-2147483648)", wrapped);
        var refused = Assert.Throws<SqlException>(() => Execute(connection, wrapped));
        Assert.Contains("negative", refused.Message, StringComparison.OrdinalIgnoreCase);
    });

    [LocalDbFact]
    public Task A_custom_update_binds_the_audit_fields_as_the_generated_update_writes_them_on_sql_server() => WithDatabaseAsync(async (connection, runtime) => {
        var custom = Source(runtime, Tasks(("customStatements",
            "-- update\nUPDATE dbo.TASKS SET TITLE = :FIELD.title, UPDATED_BY = :FIELD.updatedBy, UPDATED_AT = :FIELD.updatedAt WHERE TASK_ID = :FIELD.taskId")));
        var task = (await custom.LoadAsync("1", Context(), default)).Record!;
        var updated = await custom.UpdateAsync("1", JsonSerializer.SerializeToElement(new { title = "Paint it" }), task.Checksum, Context(), default);
        Assert.True(updated.Status == 200, JsonSerializer.Serialize(updated.Errors));
        var generated = Source(runtime, Tasks());
        var other = (await generated.LoadAsync("3", Context(), default)).Record!;
        Assert.Equal(200, (await generated.UpdateAsync("3", JsonSerializer.SerializeToElement(new { title = "Order more cones" }), other.Checksum, Context(), default)).Status);
        Assert.Equal("Paint it", Scalar(connection, "SELECT TITLE FROM dbo.TASKS WHERE TASK_ID = 1"));
        Assert.Equal("jsmith", Scalar(connection, "SELECT UPDATED_BY FROM dbo.TASKS WHERE TASK_ID = 1"));
        // Into a datetime2 column the custom statement writes the same instant the generated update does.
        Assert.IsType<DateTime>(Scalar(connection, "SELECT UPDATED_AT FROM dbo.TASKS WHERE TASK_ID = 1"));
        Assert.Equal(Scalar(connection, "SELECT UPDATED_AT FROM dbo.TASKS WHERE TASK_ID = 3"), Scalar(connection, "SELECT UPDATED_AT FROM dbo.TASKS WHERE TASK_ID = 1"));
    });

    /// <summary>A database of its own with the tasks table, dropped afterwards, and the engine over it as the <c>LocalDb</c> connection.</summary>
    private static async Task WithDatabaseAsync(Func<string, DataSourceRuntime, Task> body)
    {
        var database = "ds_regression_" + Guid.NewGuid().ToString("N")[..12];
        Execute(Master, $"CREATE DATABASE [{database}]");
        var connection = Master + $";Initial Catalog={database};Pooling=false";
        try
        {
            Execute(connection, """
                CREATE TABLE dbo.TASKS (TASK_ID int IDENTITY(1,1) PRIMARY KEY, TITLE nvarchar(100) NOT NULL, OWNER_SID nvarchar(100) NOT NULL,
                    PRIORITY int NOT NULL DEFAULT 3, PROJECT_ID int NULL, UPDATED_BY nvarchar(100) NULL, UPDATED_AT datetime2 NULL);
                INSERT INTO dbo.TASKS (TITLE, OWNER_SID, PRIORITY, PROJECT_ID) VALUES
                    ('Paint the hangar', 'S-1-5-21-1', 2, 1), ('Sweep the runway', 'S-1-5-21-2', 1, 2), ('Order cones', 'S-1-5-21-1', 3, NULL);
                """);
            var collection = new ServiceCollection();
            collection.AddLogging();
            collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            collection.AddTestConnections(connections => connections.AddConnection("LocalDb", _ => new SqlConnection(connection)));
            collection.AddSingleton<TimeProvider>(new FixedTime(Now));
            collection.AddSingleton<DataSourceRuntime>();
            using var provider = collection.BuildServiceProvider();
            await body(connection, provider.GetRequiredService<DataSourceRuntime>());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            Execute(Master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }

    private static Dictionary<string, object?> Tasks(params (string Alias, object? Value)[] settings)
    {
        var values = new Dictionary<string, object?> {
            ["access"] = "Public", ["alias"] = "tasks", ["connection"] = "LocalDb", ["source"] = "dbo.TASKS", ["keyColumns"] = "TASK_ID",
            ["allowUpdate"] = "1", ["concurrencyMode"] = DataSourceConcurrency.Checksum,
            ["mapping"] = """{ "updatedAt": { "kind": "datetime" } }""", ["auditColumns"] = "updatedBy = UPDATED_BY\nupdatedAt = UPDATED_AT",
        };
        foreach (var (alias, value) in settings) values[alias] = value;
        return values;
    }

    private static SqlDataSource Source(DataSourceRuntime runtime, Dictionary<string, object?> values)
    {
        var errors = new List<string>();
        var definition = DataSourceDefinition.Read(Guid.NewGuid(), "Tasks", alias => values.GetValueOrDefault(alias), _ => [], errors);
        Assert.True(definition is not null, string.Join(" ", errors));
        var source = new SqlDataSource(definition, runtime, new OneUser(), []);
        Assert.True(source.Problem is null, source.Problem);
        return source;
    }

    private static FormExecutionContext Context() => new(
        new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, Owner.Key)], IdentityConstants.ApplicationScheme)),
        Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid().ToString());

    private sealed class OneUser : IDataSourceUserResolver
    {
        public Task<DataSourceUser?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct) => Task.FromResult<DataSourceUser?>(Owner);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static void Execute(string connection, string sql)
    {
        using var database = new SqlConnection(connection);
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 90;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string connection, string sql)
    {
        using var database = new SqlConnection(connection);
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
