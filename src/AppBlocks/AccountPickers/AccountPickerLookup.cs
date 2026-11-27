using AppBlocks.Blocks;
using AppBlocks.DataSources;
using AppBlocks.Forms;

namespace AppBlocks.AccountPickers;

/// <summary>Use the published page and containing form for both searches and selection checks.</summary>
public sealed class AccountPickerLookup(AppBlockContent content, AppPageAccess access, IDataSourceUserResolver users,
    FormResourceRegistry resources, PublishedDataCatalog catalog, IEnumerable<IAppBlockPlugin> plugins)
{
    public async Task<IResult> GetAsync(HttpContext http, Guid pageKey, Guid blockKey, CancellationToken ct,
        string? expectedAlias = null, string keyParameter = "key", Func<AccountPickerItem, object>? project = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (await content.PageAsync(pageKey) is not { } page) return Results.NotFound();
        if (await access.VisitorAsync(page) is not { } visitor) return Results.StatusCode(403);
        var culture = AppBlockContent.RequestCulture(http);
        var types = plugins.OfType<AccountPickerBlock>().ToArray();
        var forms = AppBlockContent.Flatten(AppBlockContent.Blocks(page, culture), culture)
            .Where(b => b.Content.ContentType.Alias == "appForm"
                && AppBlockContent.Flatten(b.Areas.SelectMany(a => a), culture).Any(child => child.ContentKey == blockKey
                    && types.Any(t => t.Alias == child.Content.ContentType.Alias && (expectedAlias is null || t.Alias == expectedAlias)))).ToArray();
        if (forms.Length != 1) return Results.NotFound();
        var form = await FormDefinition.ResolveAsync(page, forms[0].ContentKey, resources, catalog, culture, plugins);
        if (form is null) return Results.NotFound();
        var conditions = http.RequestServices.GetRequiredService<BlockConditions>();
        if (!await conditions.AllowsPathAsync(page, form.Block.ContentKey, visitor, culture, ct)) return Results.StatusCode(403);
        if (FormResourceAccess.Refusal(form.Resource, visitor) is { } refusal) return Results.StatusCode(refusal);
        // A form that only shows records, or a data source the visitor may only read, still shows the stored account's name below.
        var writable = !form.ViewOnly && (form.Resource is not SqlDataSource sql || await sql.MayWriteAsync(visitor, ct));
        var pageQuery = new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(http.Request.Query["appPageQuery"]));
        Task<FormResult?>? loaded = null;
        async Task<FormRecord?> Record() => (await (loaded ??= FormAccess.LoadForPageAsync(form, visitor, http, pageQuery)))?.Record;
        form = await conditions.ForFormAsync(form, visitor, Record, false, ct);
        try
        {
            var picker = AccountPickerForm.Definitions(form, types, includeReadOnly: true).SingleOrDefault(p => p.BlockKey == blockKey);
            if (picker is null) return Results.NotFound();
            var context = new FormExecutionContext(visitor, pageKey, form.Block.ContentKey, form.Revision);
            if (!picker.Settings.Permits(await users.ResolveAsync(context, ct))) return Results.StatusCode(403);
            var query = http.Request.Query;
            if (query["s"].Count > 1 || query[keyParameter].Count > 1 || query.ContainsKey("s") && query.ContainsKey(keyParameter))
                return Results.BadRequest();
            var current = await Record();
            var canEdit = writable && !form.IsReadOnly(blockKey) && current is not null
                && (current.Key is null ? current.Capabilities.Create : current.Capabilities.Update);
            if (!canEdit)
            {
                // A read-only or view-only key-only picker still displays the loaded record's selected account name, and searches nothing.
                if (query[keyParameter].Count != 1 || current is null
                    || !current.Values.TryGetValue(picker.Settings.IdentityField, out var selectedKey)
                    || BlockOwnerAccess.Text(selectedKey) is not { } expected || query[keyParameter][0] != expected)
                    return Results.StatusCode(403);
            }
            var provider = http.RequestServices.GetRequiredKeyedService<IAccountPickerProvider>(picker.Alias);
            project ??= item => item;
            if (query.TryGetValue(keyParameter, out var key))
            {
                var selected = await provider.ResolveAsync(picker, key.ToString(), null, ct);
                return selected is null || selected.Person.Key != key.ToString()
                    ? Results.UnprocessableEntity(new { message = "This selection is unavailable in this picker." })
                    : Results.Ok(new { person = project(selected.Person), selected.Cached });
            }
            var found = await provider.SearchAsync(picker, query["s"], ct);
            return Results.Ok(new { items = found.Items.Select(project), found.Cached });
        }
        catch (AccountPickerException error)
        {
            return Results.Json(new { message = error.Message }, statusCode: error.Unavailable ? 503 : 422);
        }
    }
}
