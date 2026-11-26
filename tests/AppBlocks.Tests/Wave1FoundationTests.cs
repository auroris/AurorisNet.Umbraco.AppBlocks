using System.Data;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppBlocks.Attachments;
using AppBlocks.Blocks;
using AppBlocks.Connections;
using AppBlocks.Databases;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using AppBlocks.Setup;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace AppBlocks.Tests;

public sealed class Wave1FoundationTests
{
    [Fact]
    public void AppBlockValues_supports_custom_elements_settings_returnWithKey_and_BlockList_json()
    {
        var customType = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var customSettingsType = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var pageKey = Guid.Parse("33333333-3333-3333-3333-333333333333");

        Assert.Equal(AppBlockSchema.Key("block:hero"), AppBlockValues.BlockKey("hero"));
        Assert.Equal("umb://document/33333333333333333333333333333333", AppBlockValues.Udi(pageKey));
        Assert.Equal("umb://media/33333333333333333333333333333333", AppBlockValues.Udi(pageKey, "media"));

        var grid = new AppBlockValues();
        var custom = grid.Add(customType, new { title = "Notice" }, id: "notice.1", columnSpan: 6,
            settingsElementTypeKey: customSettingsType, settings: new { theme = "dark" }, alias: "customNotice");
        var region = grid.Add(AppBlockSchema.RegionAlias, new { heading = "Section" }, id: "region.1", settings: new { surface = "tinted" });
        var form = grid.Form("orders", [grid.Field("appText", "title", id: "orders.title")],
            returnPage: pageKey, id: "orders.form", returnWithKey: true);
        grid.Place(custom, region, form);

        var parsed = JsonNode.Parse(grid.ToJson())!.AsObject();
        var layout = parsed["layout"]!["Umbraco.BlockGrid"]!.AsArray();
        Assert.Equal(3, layout.Count);
        Assert.Equal(custom.Key.ToString(), layout[0]!["contentKey"]!.GetValue<string>());
        Assert.Equal(6, layout[0]!["columnSpan"]!.GetValue<int>());
        Assert.NotNull(custom.SettingsKey);
        Assert.Equal(custom.SettingsKey.ToString(), layout[0]!["settingsKey"]!.GetValue<string>());

        var settingsByKey = parsed["settingsData"]!.AsArray().ToDictionary(n => n!["key"]!.GetValue<string>(), n => n!);
        var customSettingsValues = settingsByKey[custom.SettingsKey!.Value.ToString()]["values"]!.AsArray();
        Assert.Equal("dark", customSettingsValues.Single(v => v!["alias"]!.GetValue<string>() == "theme")!["value"]!.GetValue<string>());
        var regionSettingsValues = settingsByKey[region.SettingsKey!.Value.ToString()]["values"]!.AsArray();
        Assert.Equal("tinted", regionSettingsValues.Single(v => v!["alias"]!.GetValue<string>() == "surface")!["value"]!.GetValue<string>());

        var formContent = parsed["contentData"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == form.Key.ToString())!;
        var formInterface = formContent["values"]!.AsArray().Single(v => v!["alias"]!.GetValue<string>() == "formInterface")!["value"]!["contentData"]![0]!["values"]!.AsArray();
        Assert.True(formInterface.Single(v => v!["alias"]!.GetValue<string>() == "returnWithKey")!["value"]!.GetValue<bool>());

        var listJson = AppBlockValues.BlockList(list => list.Place(list.Add(customType, new { title = "List item" }, id: "item.1")));
        var listDoc = JsonNode.Parse(listJson)!.AsObject();
        var listLayout = listDoc["layout"]!["Umbraco.BlockList"]!.AsArray();
        Assert.Single(listLayout);
        Assert.Equal(AppBlockValues.BlockKey("item.1").ToString(), listLayout[0]!["contentKey"]!.GetValue<string>());
        Assert.Null(listLayout[0]!["areas"]);
    }

    [Fact]
    public async Task AttachmentStore_PurgeOwnerAsync_removes_owner_links_and_unreferenced_files_transactionally()
    {
        var root = Directory.CreateTempSubdirectory("attachments-purge-owner-").FullName;
        var file = Path.Combine(root, "files.db");
        var connectionString = $"Data Source={file};Pooling=False";
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddTestConnections(c => c.AddConnection("FilesDb", _ => new SqliteConnection(connectionString)));
            await using var provider = services.BuildServiceProvider();
            var store = new AttachmentStore(provider.GetRequiredService<IApplicationConnectionFactory>(),
                Options.Create(new AttachmentSettings { Connection = "FilesDb" }), NullLogger<AttachmentStore>.Instance);

            var ownerA = new AttachmentOwner("requests", "10");
            var ownerASub = new AttachmentOwner("requests.lines", "10");
            var ownerB = new AttachmentOwner("requests", "20");
            await store.AddAsync(ownerA, new("a1.txt", "text/plain", Encoding.UTF8.GetBytes("alpha")), null, null, null, "u1", default);
            await store.AddAsync(ownerASub, new("a2.txt", "text/plain", Encoding.UTF8.GetBytes("beta")), null, null, null, "u1", default);
            var kept = await store.AddAsync(ownerB, new("b1.txt", "text/plain", Encoding.UTF8.GetBytes("gamma")), null, null, null, "u2", default);

            await using (var connection = await store.OpenConnectionAsync(default))
            {
                await using (var tx = await connection.BeginTransactionAsync(default))
                {
                    var rolled = await AttachmentStore.PurgeOwnerAsync(connection, tx, ["requests", "requests.lines"], "10", default);
                    Assert.Equal(new AttachmentPurge(2, 2, "alpha".Length + "beta".Length), rolled);
                    await tx.RollbackAsync(default);
                }
                Assert.Single(await store.ListAsync(ownerA, default));
                Assert.Single(await store.ListAsync(ownerASub, default));

                await using (var tx = await connection.BeginTransactionAsync(default))
                {
                    var committed = await AttachmentStore.PurgeOwnerAsync(connection, tx, ["requests", "requests.lines"], "10", default);
                    Assert.Equal(new AttachmentPurge(2, 2, "alpha".Length + "beta".Length), committed);
                    await tx.CommitAsync(default);
                }
            }

            Assert.Empty(await store.ListAsync(ownerA, default));
            Assert.Empty(await store.ListAsync(ownerASub, default));
            Assert.Equal(kept.Id, Assert.Single(await store.ListAsync(ownerB, default)).Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ApplicationDatabase_CreateCommand_formats_parameters_and_DependsOn_ensures_dependencies_first()
    {
        var root = Directory.CreateTempSubdirectory("schema-depends-").FullName;
        try
        {
            var depDir = Path.Combine(root, "dep");
            var appDir = Path.Combine(root, "app");
            Directory.CreateDirectory(Path.Combine(depDir, "sqlite"));
            Directory.CreateDirectory(Path.Combine(appDir, "sqlite"));
            System.IO.File.WriteAllText(Path.Combine(depDir, "sqlite", "001.sql"), "CREATE TABLE IF NOT EXISTS PARENT_T (ID INTEGER PRIMARY KEY);");
            System.IO.File.WriteAllText(Path.Combine(appDir, "sqlite", "001.sql"), "CREATE TABLE IF NOT EXISTS CHILD_T (ID INTEGER PRIMARY KEY, CREATED_AT TEXT, NOTE TEXT, PARENT_ID INTEGER REFERENCES PARENT_T(ID));");

            var depSchema = ApplicationSchema.FromDirectory(depDir, "dep");
            var appSchema = ApplicationSchema.FromDirectory(appDir, "app").DependsOn(depSchema);
            // Cycles are safely ignored without stack overflow.
            depSchema.DependsOn(appSchema);

            var dbFile = Path.Combine(root, "db.sqlite");
            using var connection = new SqliteConnection($"Data Source={dbFile};Pooling=False");
            connection.Open();
            ApplicationDatabase.Ensure(connection, appSchema);

            var stamp = new DateTime(2026, 11, 25, 14, 30, 5, 123, DateTimeKind.Utc);
            using (var insert = ApplicationDatabase.CreateCommand(connection,
                "INSERT INTO CHILD_T (ID, CREATED_AT, NOTE) VALUES (@id, @at, @note)",
                ("id", 1), ("@at", stamp), ("note", null)))
            {
                Assert.Equal("2026-11-25 14:30:05.123", insert.Parameters["@at"].Value);
                Assert.Equal(DBNull.Value, insert.Parameters["@note"].Value);
                insert.ExecuteNonQuery();
            }

            using var sqlServer = new SqlConnection();
            using var sqlCmd = ApplicationDatabase.CreateCommand(sqlServer, "SELECT @at, @bin", ("at", stamp), ("bin", new byte[] { 1, 2 }));
            Assert.Equal(DbType.DateTime2, sqlCmd.Parameters["@at"].DbType);
            Assert.Equal(DbType.Binary, sqlCmd.Parameters["@bin"].DbType);
            Assert.Equal(-1, sqlCmd.Parameters["@bin"].Size);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SettingsFile_WriteAsync_writes_atomically_and_invalidates_cache()
    {
        var root = Directory.CreateTempSubdirectory("settings-file-").FullName;
        try
        {
            var path = Path.Combine(root, "nested", "settings.json");
            var parses = 0;
            var file = new SettingsFile<string>(() => path, text => { parses++; return text ?? "<none>"; });
            Assert.Equal("<none>", file.Read());
            Assert.Equal("<none>", file.Read());
            Assert.Equal(1, parses);

            await file.WriteAsync("{\"v\":1}");
            Assert.Equal("{\"v\":1}", file.Read());
            Assert.Equal(2, parses);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "nested")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AddExtensionsFromAssembly_registers_public_extensions_without_duplicates()
    {
        var services = new ServiceCollection();
        var builder = new AppBlocksBuilder(services);
        builder.AddExtensionsFromAssembly(typeof(SampleScannedPolicy).Assembly);
        builder.AddExtensionsFromAssembly(typeof(SampleScannedPolicy).Assembly);

        Assert.Single(services, d => d.ServiceType == typeof(IDataSourcePolicy) && d.ImplementationType == typeof(SampleScannedPolicy));
        Assert.Single(services, d => d.ServiceType == typeof(IBindVariableProvider) && d.ImplementationType == typeof(SampleScannedVariables));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(PrivateScannedPolicy));
    }

    [Fact]
    public async Task DI_BlockGridAllowance_wires_settings_columnSpans_BlockList_and_publishes_AppBlocksReadyNotification_once()
    {
        var readyCount = 0;
        await using var site = await BlockSite.BootAsync(configure: builder =>
        {
            builder.Services.AddSingleton(new BlockGridAllowance("Custom grid", "customCard",
                SettingsElementTypeAlias: "customCardSettings", ColumnSpanOptions: [12, 6, 4]));
            builder.Services.AddSingleton(new BlockGridAllowance("Custom list", "customCard",
                SettingsElementTypeAlias: "customCardSettings"));
            builder.Services.AddSingleton<INotificationAsyncHandler<AppBlocksReadyNotification>>(
                new ReadyCounter(() => Interlocked.Increment(ref readyCount)));
        });
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        var types = services.GetRequiredService<IContentTypeService>();
        for (var i = 0; i < 100 && types.Get(DataSourceSchema.SqlAlias) is null; i++) await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref readyCount));

        var dataTypes = services.GetRequiredService<IDataTypeService>();
        var serializer = services.GetRequiredService<IConfigurationEditorJsonSerializer>();
        var strings = services.GetRequiredService<IShortStringHelper>();
        var editors = services.GetRequiredService<PropertyEditorCollection>();

        var gridEditor = editors.Single(e => e.Alias == Constants.PropertyEditors.Aliases.BlockGrid);
        var listEditor = editors.Single(e => e.Alias == Constants.PropertyEditors.Aliases.BlockList);
        var grid = new DataType(gridEditor, serializer) { Key = Guid.NewGuid(), Name = "Custom grid", EditorUiAlias = "Umb.PropertyEditorUi.BlockGrid",
            ConfigurationData = gridEditor.GetConfigurationEditor().FromDatabase("""{"gridColumns":12,"blocks":[]}""", serializer) };
        var list = new DataType(listEditor, serializer) { Key = Guid.NewGuid(), Name = "Custom list", EditorUiAlias = "Umb.PropertyEditorUi.BlockList",
            ConfigurationData = listEditor.GetConfigurationEditor().FromDatabase("""{"blocks":[]}""", serializer) };
        Assert.True((await dataTypes.CreateAsync(grid, Constants.Security.SuperUserKey)).Success);
        Assert.True((await dataTypes.CreateAsync(list, Constants.Security.SuperUserKey)).Success);

        var card = new ContentType(strings, -1) { Key = Guid.NewGuid(), Alias = "customCard", Name = "Custom card", IsElement = true };
        Assert.True((await types.CreateAsync(card, Constants.Security.SuperUserKey)).Success);

        // Before settings type exists, block is added without settingsElementTypeKey, with all ColumnSpanOptions.
        JsonObject Config(IDataEditor editor, Guid key) => JsonNode.Parse(editor.GetConfigurationEditor().ToDatabase(dataTypes.GetAsync(key).GetAwaiter().GetResult()!.ConfigurationData, serializer))!.AsObject();
        var gridBlock = Assert.Single(Config(gridEditor, grid.Key)["blocks"]!.AsArray())!.AsObject();
        Assert.Equal([12, 6, 4], gridBlock["columnSpanOptions"]!.AsArray().Select(o => o!["columnSpan"]!.GetValue<int>()).ToArray());
        Assert.Null(gridBlock["settingsElementTypeKey"]);
        var listBlock = Assert.Single(Config(listEditor, list.Key)["blocks"]!.AsArray())!.AsObject();
        Assert.Equal(card.Key.ToString(), listBlock["contentElementTypeKey"]!.GetValue<string>());
        Assert.Null(listBlock["settingsElementTypeKey"]);

        // Saving the settings element type later idempotently wires settingsElementTypeKey on existing blocks!
        var cardSettings = new ContentType(strings, -1) { Key = Guid.NewGuid(), Alias = "customCardSettings", Name = "Custom card settings", IsElement = true };
        Assert.True((await types.CreateAsync(cardSettings, Constants.Security.SuperUserKey)).Success);
        Assert.Equal(cardSettings.Key.ToString(), Assert.Single(Config(gridEditor, grid.Key)["blocks"]!.AsArray())!["settingsElementTypeKey"]!.GetValue<string>());
        Assert.Equal(cardSettings.Key.ToString(), Assert.Single(Config(listEditor, list.Key)["blocks"]!.AsArray())!["settingsElementTypeKey"]!.GetValue<string>());
    }

    private sealed class ReadyCounter(Action onReady) : INotificationAsyncHandler<AppBlocksReadyNotification>
    {
        public Task HandleAsync(AppBlocksReadyNotification notification, CancellationToken cancellationToken) { onReady(); return Task.CompletedTask; }
    }

    private sealed class PrivateScannedPolicy : IDataSourcePolicy
    {
        public ValueTask<bool> IsAllowedAsync(DataSourceRequest request, CancellationToken ct) => ValueTask.FromResult(true);
    }
}

public sealed class SampleScannedPolicy : IDataSourcePolicy
{
    public ValueTask<bool> IsAllowedAsync(DataSourceRequest request, CancellationToken ct) => ValueTask.FromResult(true);
}

public sealed class SampleScannedVariables : IBindVariableProvider
{
    public ValueTask<IReadOnlyDictionary<string, object?>?> ResolveAsync(FormExecutionContext context, CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyDictionary<string, object?>?>(null);
}
