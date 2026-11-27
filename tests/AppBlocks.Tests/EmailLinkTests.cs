using System.Net;
using AppBlocks.Blocks;
using AppBlocks.Forms;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Tests;

/// <summary>
/// A templated Email link block inside a form: publishing validation and server markup.
/// tests/ui/email-link.browser.test.mjs covers live browser mailto: expansion.
/// </summary>
public sealed class EmailLinkTests
{
    [Fact]
    public void Publishing_checks_email_link_placement_recipients_and_referenced_fields()
    {
        var validator = new AppBlocksValidator(new FormResourceRegistry([new DisplayLinkTests.Contacts()], []));

        var outside = BlockPages.Empty();
        BlockPages.Layout(outside).Add(BlockPages.Add(outside, AppBlockSchema.EmailLinkAlias, ("to", "team@example.org")));
        Assert.Contains(validator.Validate(outside.ToJsonString()), error => error.Contains("inside a form"));

        string FormPage(bool includeReference, params (string Alias, string Value)[] emailProps)
        {
            var page = BlockPages.Empty();
            var fields = new List<System.Text.Json.Nodes.JsonNode> {
                BlockPages.Add(page, "appText", ("field", "email"), ("hidden", "1")),
                BlockPages.Add(page, AppBlockSchema.EmailLinkAlias, emailProps),
            };
            if (includeReference) fields.Add(BlockPages.Add(page, "appText", ("field", "reference"), ("hidden", "1")));
            BlockPages.Layout(page).Add(BlockPages.FormBlock(page, "contacts.test", [.. fields], []));
            return page.ToJsonString();
        }

        Assert.Empty(validator.Validate(FormPage(true,
            ("label", "Email the requester"),
            ("to", "{email}"),
            ("subject", "[{reference}] Update"),
            ("body", "Reference: {reference}\nContact: {email}"))));

        Assert.Equal(["Enter a To address or template for the Email link block."],
            validator.Validate(FormPage(true, ("label", "Missing To"))));

        Assert.Equal(["The Email link template names {reference}, which is not a field on this form. Add a field block for it; it may be hidden."],
            validator.Validate(FormPage(false, ("to", "{email}"), ("subject", "[{reference}] Update"))));

        Assert.Contains(validator.Validate(FormPage(true, ("to", "{email}"), ("cc", "{unknownField}"))),
            error => error.Contains("names {unknownField}"));
    }
}

[Collection("Standalone Umbraco")]
public sealed class EmailLinkSiteTests
{
    [Fact]
    public async Task An_email_link_block_installs_and_renders_templates_for_the_runtime()
    {
        await using var site = await BlockSite.BootAsync(configure: builder => builder.Services.AddSingleton<IFormResource>(new DisplayLinkTests.Contacts()));
        await site.PublishAsync();
        Guid pageKey;
        using (var scope = site.CreateScope())
        {
            var installed = scope.ServiceProvider.GetRequiredService<IContentTypeService>().Get(AppBlockSchema.EmailLinkAlias);
            Assert.NotNull(installed);
            Assert.Equal(AppBlockSchema.Key(AppBlockSchema.EmailLinkAlias), installed.Key);
            Assert.True(installed.IsElement);

            var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
            var blocks = BlockPages.Empty();
            var email = BlockPages.Add(blocks, "appText", ("field", "email"), ("hidden", "1"));
            var reference = BlockPages.Add(blocks, "appText", ("field", "reference"), ("hidden", "1"));
            var mailto = BlockPages.Add(blocks, AppBlockSchema.EmailLinkAlias,
                ("label", "Notify contact"),
                ("to", "{email}"),
                ("cc", "ops@example.org"),
                ("subject", "[{reference}] Status update"),
                ("body", "Reference: {reference}\n\nThank you."),
                ("emptyText", "No email available"),
                ("helpText", "Opens your email client."));
            BlockPages.Layout(blocks).Add(BlockPages.FormBlock(blocks, "contacts.test", [email, reference, mailto], []));
            Assert.Empty(ActivatorUtilities.CreateInstance<AppBlocksValidator>(scope.ServiceProvider).Validate(blocks.ToJsonString()));

            var page = contents.Create("EmailLinks", contents.GetById(BlockSite.FormPageKey)!.ParentId, BlockSite.PageAlias);
            page.SetValue(BlockSite.BodyAlias, blocks.ToJsonString());
#pragma warning disable CS0618
            Assert.True(contents.Save(page).Success);
            var published = contents.Publish(page, ["*"]);
            Assert.True(published.Success, string.Join("; ", published.EventMessages?.GetAll().Select(m => m.Message) ?? []));
#pragma warning restore CS0618
            pageKey = page.Key;
        }
        await site.WaitForPublishedAsync(pageKey);
        using var client = site.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/emaillinks/"));

        Assert.Contains("data-email-link", html);
        Assert.Contains("data-to=\"{email}\"", html);
        Assert.Contains("data-cc=\"ops@example.org\"", html);
        Assert.Contains("data-subject=\"[{reference}] Status update\"", html);
        Assert.Contains("data-empty-text=\"No email available\"", html);
        Assert.Contains(">Notify contact</label>", html);
        Assert.Contains("Opens your email client.", html);
        var label = html[..html.IndexOf(">Notify contact</label>", StringComparison.Ordinal)];
        Assert.DoesNotContain(" for=", label[label.LastIndexOf("<label", StringComparison.Ordinal)..]);
        Assert.Contains("aria-labelledby=", html);
    }
}
