using AppBlocks.Blocks;
using AppBlocks.Setup;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace AppBlocks.Tests;

/// <summary>Content an application installs from code: the steps that change it once per site, and the patch that keeps an editor's draft.</summary>
[Collection("Standalone Umbraco")]
public sealed class ContentStepTests
{
    private const string Plan = "ContentStepTests";

    [Fact]
    public async Task A_new_site_is_installed_and_an_installed_one_runs_each_later_step_once_in_order()
    {
        await using var site = await BlockSite.StartAsync();
        T With<T>(Func<IServiceProvider, T> use) { using var scope = site.CreateScope(); return use(scope.ServiceProvider); }
        string? Position() => With(services => services.GetRequiredService<ContentStepRunner>().Position(Plan));
        // The home page's name as the content service gives it, which is from Umbraco's cache when it has it, and as the database has it.
        string? HomeName() => With(services => services.GetRequiredService<IContentService>().GetById(AppBlockSchema.Key("block-site:home"))!.Name);
        var homeId = With(services => services.GetRequiredService<IContentService>().GetById(AppBlockSchema.Key("block-site:home"))!.Id);
        string? StoredHomeName() => With(services =>
        {
            using var scope = services.GetRequiredService<Umbraco.Cms.Infrastructure.Scoping.IScopeProvider>().CreateScope(autoComplete: true);
            return scope.Database.ExecuteScalar<string>("SELECT text FROM umbracoNode WHERE id = @0", homeId);
        });
        var log = new List<string>();
        Task<ContentStepsResult> Run(params ContentStep[] steps) => With(services =>
            services.GetRequiredService<ContentStepRunner>().RunAsync(Plan, _ => { log.Add("install"); return Task.CompletedTask; }, steps, TestContext.Current.CancellationToken));
        ContentStep Step(string name) => new(name, _ => { log.Add(name); return Task.CompletedTask; });
        // A step that renames the home page, and can fail after it has saved the name.
        ContentStep Rename(string name, string to, bool fail = false) => new(name, _ =>
        {
            using var scope = site.CreateScope();
            var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
            var home = contents.GetById(AppBlockSchema.Key("block-site:home"))!;
            home.Name = to;
#pragma warning disable CS0618
            Assert.True(contents.Save(home).Success);
#pragma warning restore CS0618
            log.Add(name);
            return fail ? throw new InvalidOperationException("The step broke after saving.") : Task.CompletedTask;
        });

        // A site the plan has not touched is installed as the build has it, and the steps count as run.
        Assert.Null(Position());
        var installed = await Run(Step("0001-first"), Step("0002-second"));
        Assert.True(installed.Installed);
        Assert.Empty(installed.Ran);
        Assert.Equal(["install"], log);
        Assert.Equal("0002-second", Position());
        Assert.Equal("installed:0002-second", With(services => services.GetRequiredService<IKeyValueService>().GetValue(ContentStepRunner.StoreKey(Plan))));

        // The next start has nothing to do. A later build's steps run once, in order, and the start after it runs none.
        log.Clear();
        Assert.Empty((await Run(Step("0001-first"), Step("0002-second"))).Ran);
        var updated = await Run(Step("0001-first"), Step("0002-second"), Rename("0003-rename", "Home, renamed"), Step("0004-fourth"));
        Assert.False(updated.Installed);
        Assert.Equal(["0003-rename", "0004-fourth"], updated.Ran);
        Assert.Equal(["0003-rename", "0004-fourth"], log);
        Assert.Equal("Home, renamed", HomeName());
        Assert.Empty((await Run(Step("0001-first"), Step("0002-second"), Rename("0003-rename", "Not again"), Step("0004-fourth"))).Ran);
        Assert.Equal("Home, renamed", HomeName());

        // A step that fails leaves its changes and the site's position as they were, and the step after it does not run.
        log.Clear();
        var error = await Assert.ThrowsAsync<ContentStepException>(() => Run(Step("0004-fourth"), Rename("0005-broken", "Half done", fail: true), Step("0006-after")));
        Assert.Contains("0005-broken", error.Message);
        Assert.Contains("The step broke after saving.", error.Message);
        Assert.False(error.SiteFromAnotherBuild);
        Assert.Equal(["0005-broken"], log);
        Assert.Equal("Home, renamed", StoredHomeName());
        Assert.Equal("Home, renamed", HomeName());
        Assert.Equal("0004-fourth", Position());
        // Once fixed, it runs, and so does the step after it.
        Assert.Equal(["0005-broken", "0006-after"], (await Run(Step("0004-fourth"), Rename("0005-broken", "Home, fixed"), Step("0006-after"))).Ran);
        Assert.Equal("Home, fixed", StoredHomeName());
        Assert.Equal("Home, fixed", HomeName());

        // An older build, whose last step is behind the site, runs nothing. A build that has retired every step it had passes.
        log.Clear();
        var older = await Assert.ThrowsAsync<ContentStepException>(() => Run(Step("0004-fourth"), Step("0005-broken")));
        Assert.True(older.SiteFromAnotherBuild);
        Assert.Contains("0006-after", older.Message);
        Assert.Empty(log);
        Assert.Empty((await Run()).Ran);
        Assert.Equal("0006-after", Position());

        // Each step is a transaction of its own, so a step does not run inside a scope its caller left open.
        using (var outer = site.CreateScope())
        using (outer.ServiceProvider.GetRequiredService<ICoreScopeProvider>().CreateCoreScope())
        {
            var nested = await Assert.ThrowsAsync<InvalidOperationException>(() => outer.ServiceProvider.GetRequiredService<ContentStepRunner>()
                .RunAsync(Plan, _ => Task.CompletedTask, [Step("0006-after"), Step("0007-nested")], TestContext.Current.CancellationToken));
            Assert.Contains("already open", nested.Message);
        }
        Assert.Empty(log);
        Assert.Equal("0006-after", Position());

        // Names sort in the order the steps are given, and a site installed before its application had a plan is recorded by hand.
        Assert.Contains("does not sort after", (await Assert.ThrowsAsync<ContentStepException>(() => Run(Step("0007-b"), Step("0007-a")))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Run(Step("has spaces")));
        With(services => { services.GetRequiredService<ContentStepRunner>().Record("Adopted", ""); return 0; });
        Assert.Equal("", With(services => services.GetRequiredService<ContentStepRunner>().Position("Adopted")));
        Assert.Null(With(services => services.GetRequiredService<ContentStepRunner>().Position("NeverInstalled")));
    }

    [Fact]
    public async Task A_patch_changes_the_published_version_and_keeps_an_editors_draft_as_a_draft()
    {
        await using var site = await BlockSite.StartAsync();
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        var contents = services.GetRequiredService<IContentService>();
        var patcher = services.GetRequiredService<ContentPatcher>();
        var types = services.GetRequiredService<IContentTypeService>();
        var shortStrings = services.GetRequiredService<IShortStringHelper>();
        var text = (await services.GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.TextareaGuid))!;
        // A note that gains a line, once: a value that already has it is left alone.
        static string? AddLine(string? value) => value is null || value.Contains("patched") ? null : value + "\npatched";
#pragma warning disable CS0618
        IContent Note(string name, string alias, string value, string? culture)
        {
            var note = contents.Create(name, -1, alias);
            if (culture is not null) note.SetCultureName(name, culture);
            note.SetValue("body", value, culture);
            Assert.True(contents.Save(note).Success);
            Assert.True(contents.Publish(note, [culture ?? "*"]).Success);
            return contents.GetById(note.Key)!;
        }
        foreach (var culture in new[] { null, "en" })
        {
            var alias = culture is null ? "patchNote" : "patchNoteByCulture";
            var type = new ContentType(shortStrings, -1) { Alias = alias, Name = alias, AllowedAsRoot = true, Variations = culture is null ? ContentVariation.Nothing : ContentVariation.Culture };
            type.AddPropertyType(new PropertyType(shortStrings, text) { Alias = "body", Name = "Body", Variations = type.Variations });
            Assert.True((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success);
            string? Live(IContent note) => contents.GetById(note.Key)!.GetValue<string>("body", culture, published: true);
            string? Draft(IContent note) => contents.GetById(note.Key)!.GetValue<string>("body", culture);
            string? LiveName(IContent note) { var read = contents.GetById(note.Key)!; return culture is null ? read.PublishName : read.GetPublishName(culture); }
            string? DraftName(IContent note) { var read = contents.GetById(note.Key)!; return culture is null ? read.Name : read.GetCultureName(culture); }

            // A published node with no pending edits is changed and published again, and has no draft afterwards.
            var plain = Note("Plain", alias, "as installed", culture);
            Assert.True(patcher.Patch(plain, "body", AddLine, culture));
            Assert.Equal("as installed\npatched", Live(plain));
            Assert.False(contents.GetById(plain.Key)!.Edited);
            // A second patch finds the change made and writes no new version.
            var version = contents.GetById(plain.Key)!.VersionId;
            Assert.False(patcher.Patch(contents.GetById(plain.Key)!, "body", AddLine, culture));
            Assert.Equal(version, contents.GetById(plain.Key)!.VersionId);

            // An editor's saved draft stays a draft: the published version takes only the change, the draft keeps the edit and takes the change too.
            var edited = Note("Edited", alias, "as installed", culture);
            edited.SetValue("body", "an editor's unpublished work", culture);
            if (culture is null) edited.Name = "Edited, draft name"; else edited.SetCultureName("Edited, draft name", culture);
            Assert.True(contents.Save(edited).Success);
            Assert.True(patcher.Patch(contents.GetById(edited.Key)!, "body", AddLine, culture));
            Assert.Equal("as installed\npatched", Live(edited));
            Assert.Equal("an editor's unpublished work\npatched", Draft(edited));
            Assert.Equal("Edited", LiveName(edited));
            Assert.Equal("Edited, draft name", DraftName(edited));
            Assert.True(contents.GetById(edited.Key)!.Edited);

            // A new name goes to both versions. A node that was never published has its draft changed and stays unpublished.
            Assert.True(patcher.Patch(contents.GetById(edited.Key)!, "body", _ => null, culture, name: "Renamed"));
            Assert.Equal("Renamed", LiveName(edited));
            Assert.Equal("Renamed", DraftName(edited));
            Assert.Equal("an editor's unpublished work\npatched", Draft(edited));
            Assert.Equal("as installed\npatched", Live(edited));
            var unpublished = contents.Create("Unpublished", -1, alias);
            if (culture is not null) unpublished.SetCultureName("Unpublished", culture);
            unpublished.SetValue("body", "never published", culture);
            Assert.True(contents.Save(unpublished).Success);
            Assert.True(patcher.Patch(contents.GetById(unpublished.Key)!, "body", AddLine, culture));
            Assert.Equal("never published\npatched", Draft(unpublished));
            Assert.False(contents.GetById(unpublished.Key)!.Published);
        }
#pragma warning restore CS0618
    }
}
