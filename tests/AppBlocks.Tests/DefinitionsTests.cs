using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppBlocks.Api;
using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Setup;
using AppBlocks.Tabular;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Core.Strings;

namespace AppBlocks.Tests;

/// <summary>
/// The definitions on a site over time. Reinstall resets every
/// definition to the package without touching content, and Uninstall removes them, refusing while something of the site's depends on them.
/// </summary>
[Collection("Standalone Umbraco")]
public sealed class DefinitionsTests
{
    private const string Api = "/umbraco/management/api/v1/appblocks/definitions";
    private static readonly Guid FormsContainerGrid = Guid.Parse("97b36051-4abe-e750-6667-9cb7ee6a9a99");

    [Fact]
    public async Task A_reinstall_resets_the_definitions_while_the_site_keeps_its_content()
    {
        await using var site = await BlockSite.StartAsync();
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;

        using var admin = await site.SignInAsAdministratorAsync();
        var status = await admin.GetFromJsonAsync<JsonElement>(Api);
        Assert.False(status.GetProperty("uninstalled").GetBoolean());
        var package = Assert.Single(status.GetProperty("packages").EnumerateArray());
        Assert.Equal("AppBlocks", package.GetProperty("id").GetString());
        Assert.StartsWith("0.", package.GetProperty("version").GetString());
        var declared = package.GetProperty("declared").GetInt32();
        Assert.True(declared > 30);
        Assert.Equal(declared, package.GetProperty("installed").GetInt32());

        // What an editor might have done to the package's definitions in the backoffice.
        var types = services.GetRequiredService<IContentTypeService>();
        var dataTypes = services.GetRequiredService<IDataTypeService>();
        var strings = services.GetRequiredService<IShortStringHelper>();
        var form = types.Get("appForm")!;
        var formId = form.Id;
        var headingKey = form.PropertyTypes.Single(p => p.Alias == "heading").Key;
        form.Name = "Site form";
        form.PropertyTypes.Single(p => p.Alias == "heading").Name = "Site heading";
        form.AddPropertyType(new PropertyType(strings, (await dataTypes.GetAsync(form.PropertyTypes.Single(p => p.Alias == "heading").DataTypeKey))!) { Alias = "siteExtra", Name = "Site extra" }, "configuration");
        Assert.True((await types.UpdateAsync(form, Constants.Security.SuperUserKey)).Success);
        var grid = (await dataTypes.GetAsync(FormsContainerGrid))!;
        var gridConfiguration = JsonSerializer.Serialize(grid.ConfigurationData);
        grid.Name = "Site grid";
        grid.ConfigurationData = new Dictionary<string, object> { ["gridColumns"] = 6, ["blocks"] = Array.Empty<object>() };
        Assert.True((await dataTypes.UpdateAsync(grid, Constants.Security.SuperUserKey)).Success);
        var contents = services.GetRequiredService<IContentService>();
        var source = Assert.Single(contents.GetPagedOfTypes([types.Get(DataSourceSchema.SqlAlias)!.Id], 0, 10, out _, null));
        Assert.Equal("orders.lines", source.GetValue<string>("alias"));

        using var response = await admin.PostAsJsonAsync(Api + "/reinstall", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var change = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, change.GetProperty("created").GetInt32());
        Assert.Equal(declared, change.GetProperty("updated").GetInt32());
        Assert.Contains(change.GetProperty("notes").EnumerateArray(), note => note.GetString()!.Contains("'siteExtra'"));

        // The same types, by id, as the package has them again.
        var after = types.Get(form.Key)!;
        Assert.Equal(formId, after.Id);
        Assert.Equal("Form", after.Name);
        Assert.Equal("Heading", after.PropertyTypes.Single(p => p.Alias == "heading").Name);
        Assert.Equal(headingKey, after.PropertyTypes.Single(p => p.Alias == "heading").Key);
        Assert.False(after.PropertyTypeExists("siteExtra"));
        var gridAfter = (await dataTypes.GetAsync(FormsContainerGrid))!;
        Assert.Equal("AppBlocks: Forms container", gridAfter.Name);
        using (var expected = JsonDocument.Parse(gridConfiguration))
        using (var actual = JsonDocument.Parse(JsonSerializer.Serialize(gridAfter.ConfigurationData)))
            Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Contains(types.Get(DataSourceSchema.RootAlias)!.AllowedContentTypes!, child => child.Alias == DataSourceSchema.SqlAlias);

        // The site's content and its own type, which uses the package's grid, are as they were, and the page still works.
        Assert.Equal("orders.lines", contents.GetById(source.Id)!.GetValue<string>("alias"));
        Assert.Equal(FormsContainerGrid, types.Get(BlockSite.PageAlias)!.PropertyTypes.Single(p => p.Alias == BlockSite.BodyAlias).DataTypeKey);
        var member = site.CreateMember("definitions-" + Guid.NewGuid().ToString("N")[..8], Guid.NewGuid().ToString("N") + "@example.invalid", "Definitions member");
        using var client = await site.SignInMemberAsync(member);
        var lines = await site.Tabular(client, BlockSite.FormPageKey, BlockPages.LinesBlock).ListAsync("id=4");
        Assert.True(lines.IsSuccess, lines.Problem);
        Assert.Equal(2, lines.Rows.Count);
        status = await admin.GetFromJsonAsync<JsonElement>(Api);
        Assert.False(status.GetProperty("uninstalled").GetBoolean());
    }

    [Fact]
    public async Task A_reinstall_leaves_a_type_of_the_site_that_took_one_of_the_aliases()
    {
        await using var site = await BlockSite.BootAsync();
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        var types = services.GetRequiredService<IContentTypeService>();
        await WaitForInstallAsync(types);
        var packaged = types.Get("listOfValues")!;
        Assert.Equal(ContentTypeOperationStatus.Success, await types.DeleteAsync(packaged.Key, Constants.Security.SuperUserKey));
        var own = new ContentType(services.GetRequiredService<IShortStringHelper>(), -1) { Key = Guid.NewGuid(), Alias = "listOfValues", Name = "Site list", AllowedAsRoot = true };
        Assert.True((await types.CreateAsync(own, Constants.Security.SuperUserKey)).Success);

        var change = await services.GetRequiredService<SchemaInstaller>().ReinstallAsync(default);
        Assert.Contains(change.Notes, note => note.Contains("'Site list' (listOfValues)"));
        Assert.Null(types.Get(packaged.Key));
        Assert.Equal(own.Key, types.Get("listOfValues")!.Key);
        Assert.Equal("Site list", types.Get("listOfValues")!.Name);
    }

    [Fact]
    public async Task An_uninstall_refuses_while_content_or_the_site_depend_on_the_definitions_and_otherwise_removes_them_until_a_reinstall()
    {
        await using (var site = await BlockSite.StartAsync())
        {
            using var admin = await site.SignInAsAdministratorAsync();
            using var refused = await admin.PostAsJsonAsync(Api + "/uninstall", new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            var detail = (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;
            Assert.Contains($"({DataSourceSchema.SqlAlias})", detail);
            Assert.Contains($"({BlockSite.PageAlias})", detail);
            var status = await admin.GetFromJsonAsync<JsonElement>(Api);
            Assert.False(status.GetProperty("uninstalled").GetBoolean());
            var package = Assert.Single(status.GetProperty("packages").EnumerateArray());
            Assert.Equal(package.GetProperty("declared").GetInt32(), package.GetProperty("installed").GetInt32());
        }

        await using var bare = await BlockSite.BootAsync();
        using var scope = bare.CreateScope();
        var services = scope.ServiceProvider;
        var types = services.GetRequiredService<IContentTypeService>();
        var dataTypes = services.GetRequiredService<IDataTypeService>();
        var contentFolders = services.GetRequiredService<IContentTypeContainerService>();
        var dataFolders = services.GetRequiredService<IDataTypeContainerService>();
        await WaitForInstallAsync(types);
        var schema = services.GetServices<AppBlocksSchema>().DistinctBy(s => s.Assembly).Single();
        var declared = schema.Definitions("ContentTypes").Concat(schema.Definitions("DataTypes")).Count();
        using var administrator = await bare.SignInAsAdministratorAsync();
        var installer = services.GetRequiredService<SchemaInstaller>();
        var change = await installer.UninstallAsync(default);
        Assert.True(change.Deleted > declared, "The folders go too.");
        foreach (var definition in schema.Definitions("ContentTypes")) Assert.Null(types.Get(Guid.Parse(definition.Attribute("Key")!.Value)));
        foreach (var definition in schema.Definitions("DataTypes")) Assert.Null(await dataTypes.GetAsync(Guid.Parse(definition.Attribute("Key")!.Value)));
        Assert.DoesNotContain(await contentFolders.GetAllAsync(), folder => folder.Name == "AppBlocks");
        Assert.DoesNotContain(await dataFolders.GetAllAsync(), folder => folder.Name == "AppBlocks");
        var gone = await administrator.GetFromJsonAsync<JsonElement>(Api);
        Assert.True(gone.GetProperty("uninstalled").GetBoolean());
        Assert.Equal(0, Assert.Single(gone.GetProperty("packages").EnumerateArray()).GetProperty("installed").GetInt32());

        // A start installs nothing now.
        await installer.EnsureAsync(default);
        Assert.Null(types.Get(DataSourceSchema.SqlAlias));

        using var back = await administrator.PostAsJsonAsync(Api + "/reinstall", new { });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal(declared, (await back.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        foreach (var definition in schema.Definitions("ContentTypes")) Assert.NotNull(types.Get(Guid.Parse(definition.Attribute("Key")!.Value)));
        foreach (var definition in schema.Definitions("DataTypes")) Assert.NotNull(await dataTypes.GetAsync(Guid.Parse(definition.Attribute("Key")!.Value)));
        var again = await administrator.GetFromJsonAsync<JsonElement>(Api);
        Assert.False(again.GetProperty("uninstalled").GetBoolean());
        Assert.Equal(declared, Assert.Single(again.GetProperty("packages").EnumerateArray()).GetProperty("installed").GetInt32());
    }

    [Fact]
    public async Task A_registered_element_is_allowed_in_the_named_Block_Grid_once_both_are_there()
    {
        await using var site = await BlockSite.BootAsync(configure: builder => {
            var appBlocks = (AppBlocks.Connections.AppBlocksBuilder)builder.Services.Single(d => d.ServiceType == typeof(AppBlocks.Connections.AppBlocksBuilder)).ImplementationInstance!;
            appBlocks.AllowInBlockGrid("Site grid", "siteNotice", columnSpan: 6).AllowInBlockGrid("Site grid", "siteBanner", areaKey: "main")
                .AllowInBlockGrid("No such grid", "siteNotice");
        });
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        await WaitForInstallAsync(services.GetRequiredService<IContentTypeService>());
        var types = services.GetRequiredService<IContentTypeService>();
        var dataTypes = services.GetRequiredService<IDataTypeService>();
        var serializer = services.GetRequiredService<IConfigurationEditorJsonSerializer>();
        var strings = services.GetRequiredService<IShortStringHelper>();
        async Task<IContentType> Element(string alias)
        {
            var element = new ContentType(strings, -1) { Key = Guid.NewGuid(), Alias = alias, Name = alias, IsElement = true };
            Assert.True((await types.CreateAsync(element, Constants.Security.SuperUserKey)).Success);
            return element;
        }

        // The site's grid: a section block whose area "main" takes only the blocks it lists.
        var section = await Element("siteSection");
        var editor = services.GetRequiredService<PropertyEditorCollection>().Single(e => e.Alias == Constants.PropertyEditors.Aliases.BlockGrid);
        var area = $$"""{"key":"{{Guid.NewGuid()}}","alias":"main","columnSpan":12,"rowSpan":1,"specifiedAllowance":[{"elementTypeKey":"{{section.Key}}"}]}""";
        var json = $$"""{"gridColumns":12,"blocks":[{"contentElementTypeKey":"{{section.Key}}","allowAtRoot":true,"allowInAreas":false,"areas":[{{area}}]}]}""";
        var grid = new DataType(editor, serializer) { Key = Guid.NewGuid(), Name = "Site grid", EditorUiAlias = "Umb.PropertyEditorUi.BlockGrid",
            ConfigurationData = editor.GetConfigurationEditor().FromDatabase(json, serializer) };
        Assert.True((await dataTypes.CreateAsync(grid, Constants.Security.SuperUserKey)).Success);
        System.Text.Json.Nodes.JsonObject Configuration() => System.Text.Json.Nodes.JsonNode.Parse(
            editor.GetConfigurationEditor().ToDatabase(dataTypes.GetAsync(grid.Key).GetAwaiter().GetResult()!.ConfigurationData, serializer))!.AsObject();
        System.Text.Json.Nodes.JsonNode[] Blocks(Guid key) => Configuration()["blocks"]!.AsArray()
            .Where(block => string.Equals(block!["contentElementTypeKey"]!.GetValue<string>(), key.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray()!;

        // An element type created after startup, as a package's own startup creates one, is allowed when it is saved.
        var notice = await Element("siteNotice");
        var allowed = Assert.Single(Blocks(notice.Key));
        Assert.True(allowed["allowAtRoot"]!.GetValue<bool>());
        Assert.Equal(6, allowed["columnSpanOptions"]![0]!["columnSpan"]!.GetValue<int>());
        // One for an area goes into the area's list, and not at the root.
        var banner = await Element("siteBanner");
        Assert.False(Assert.Single(Blocks(banner.Key))["allowAtRoot"]!.GetValue<bool>());
        var listed = Configuration()["blocks"]![0]!["areas"]![0]!["specifiedAllowance"]!.AsArray();
        Assert.Single(listed, entry => string.Equals(entry!["elementTypeKey"]!.GetValue<string>(), banner.Key.ToString(), StringComparison.OrdinalIgnoreCase));

        // Reapplying the registered allowances preserves the site's label and keeps each entry unique.
        var configuration = Configuration();
        configuration["blocks"]!.AsArray().Single(block => string.Equals(block!["contentElementTypeKey"]!.GetValue<string>(), notice.Key.ToString(), StringComparison.OrdinalIgnoreCase))!["label"] = "Site's own label";
        var changed = (await dataTypes.GetAsync(grid.Key))!;
        changed.ConfigurationData = editor.GetConfigurationEditor().FromDatabase(configuration.ToJsonString(), serializer);
        Assert.True((await dataTypes.UpdateAsync(changed, Constants.Security.SuperUserKey)).Success);
        await ActivatorUtilities.CreateInstance<BlockGridAllowances>(services).ApplyAsync(
            services.GetRequiredService<AppBlocks.Connections.AppBlocksBuilder>().BlockGridAllowances, default);
        Assert.Equal("Site's own label", Assert.Single(Blocks(notice.Key))["label"]!.GetValue<string>());
        Assert.Single(Blocks(banner.Key));
        Assert.Equal(3, Configuration()["blocks"]!.AsArray().Count);
        Assert.Equal(2, Configuration()["blocks"]![0]!["areas"]![0]!["specifiedAllowance"]!.AsArray().Count);
    }

    private static async Task WaitForInstallAsync(IContentTypeService types)
    {
        for (var i = 0; i < 100 && types.Get(DataSourceSchema.SqlAlias) is null; i++) await Task.Delay(100);
        Assert.NotNull(types.Get(DataSourceSchema.SqlAlias));
    }

}
