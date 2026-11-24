using AppBlocks.Blocks;
using AppBlocks.Testing;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

/// <summary>The listing of a site's content that an application's tests keep under source control, and the check that compares it.</summary>
[Collection("Standalone Umbraco")]
public sealed class ContentListingTests
{
    [Fact]
    public async Task A_listing_shows_each_document_and_its_blocks_and_a_check_names_what_changed()
    {
        await using var site = await BlockSite.StartAsync();
        using var scope = site.CreateScope();
        var services = scope.ServiceProvider;
        var contents = services.GetRequiredService<IContentService>();
        var home = contents.GetById(AppBlockSchema.Key("block-site:home"))!;

        // A page an application might install: a script of several lines, and a form that returns to another page.
        var blocks = new AppBlockValues();
        blocks.Place(blocks.Add("appScript", new { script = "function start() {\n  return 1;\n}" }, "listing:script"));
        blocks.Place(blocks.Form("listing.sample", [blocks.Field("appText", "customer", id: "listing:customer")],
            returnPage: BlockSite.FormPageKey, actions: new { saveLabel = "Save" }, id: "listing:form"));
#pragma warning disable CS0618
        var page = contents.Create("Listing sample", home.Id, BlockSite.PageAlias);
        page.SetValue(BlockSite.BodyAlias, blocks.ToJson());
        Assert.True(contents.Save(page).Success);
        var access = services.GetRequiredService<IPublicAccessService>();
        Assert.True(access.Save(new PublicAccessEntry(page, home, home,
            [new PublicAccessRule { RuleType = Constants.Conventions.PublicAccess.MemberRoleRuleType, RuleValue = "Listing readers" }])).Success);
#pragma warning restore CS0618

        var listing = await ContentListing.OfAsync(services);
        Assert.Equal(listing, await ContentListing.OfAsync(services));
        string Key(string id) => AppBlockSchema.Key("block:" + id).ToString();
        var expected = $$"""
            /Home/Listing sample
              key {{page.Key}}; type {{BlockSite.PageAlias}}; unpublished
              access: MemberRole=Listing readers; sign in at {/Home}; refused at {/Home}
              {{BlockSite.BodyAlias}} =
                appScript {{Key("listing:script")}}
                  script = text
            """.Replace("\r\n", "\n");
        Assert.Contains(expected, listing);
        // The form's interface is a block value of its own, the page it returns to is named by its path, and each area is named.
        Assert.Contains($"""
                appForm {Key("listing:form")}
                  formInterface =
                    appFormInterface {Key("listing:form:interface")}
                      keyParameter = id
                      resource = listing.sample
                      returnPage = umb://document/{"{/Home/Orders form}"}
                  [fields]
                    appText {Key("listing:customer")}
                      field = customer
                  [actions]
                    appActions {Key("listing:form:actions")}
                      saveLabel = Save
            """.Replace("\r\n", "\n"), listing);
        // The script is listed once, under the name the block gives it.
        var name = listing[(listing.IndexOf("script = text ", StringComparison.Ordinal) + "script = text ".Length)..][..8];
        Assert.Contains($"script = text {name} (3 lines)\n", listing);
        Assert.Contains($"TEXT {name}\n  | function start() {{\n  |   return 1;\n  | }}\n", listing);
        // The site's other pages are there in tree order, and a document can be left out.
        Assert.True(listing.IndexOf("\n/Home/Orders form\n", StringComparison.Ordinal) < listing.IndexOf("\n/Home/Website\n", StringComparison.Ordinal));
        Assert.Contains("\n/Home\n", "\n" + listing);
        var withoutSample = await ContentListing.OfAsync(services, new ContentListingOptions { Include = node => node.Key != page.Key });
        Assert.DoesNotContain("/Home/Listing sample", withoutSample);
        Assert.Contains("/Home/Orders form", withoutSample);

        // The check writes the file when asked, passes while the site matches it, and says what changed when it does not.
        var folder = Directory.CreateTempSubdirectory("appblocks-content-").FullName;
        try
        {
            var file = Path.Combine(folder, "pages", "current.txt");
            Assert.Contains("is missing", Assert.Throws<ContentCheckException>(() => ContentCheck.Matches(listing, file, "The sample site's pages.", write: false)).Message);
            ContentCheck.Matches(listing, file, "The sample site's pages.", write: true);
            Assert.StartsWith("The sample site's pages.\n", System.IO.File.ReadAllText(file));
            ContentCheck.Matches(listing, file, "The sample site's pages.", write: false);
            page.Name = "Listing sample, renamed";
#pragma warning disable CS0618
            Assert.True(contents.Save(page).Success);
#pragma warning restore CS0618
            var changed = Assert.Throws<ContentCheckException>(() => ContentCheck.Matches(ContentListing.OfAsync(services).GetAwaiter().GetResult(), file, "The sample site's pages.", write: false));
            Assert.Contains("- /Home/Listing sample\n", changed.Message);
            Assert.Contains("+ /Home/Listing sample, renamed\n", changed.Message);
            Assert.Contains(ContentCheck.ListingVariable + "=write", changed.Message);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
