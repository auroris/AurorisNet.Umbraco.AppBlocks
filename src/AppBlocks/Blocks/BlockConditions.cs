using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using AppBlocks.AccountPickers;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using AppBlocks.Publishing;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace AppBlocks.Blocks;

/// <summary>Server-side conditions on a published block. Both renderers and block endpoints use the same published values.</summary>
public sealed class BlockConditions(IDataSourceUserResolver users, IMemberGroupService memberGroups, IEnumerable<IAppBlockPlugin> plugins, IHttpContextAccessor http)
{
    public const string Visibility = "blockVisibility", Security = "blockSecurity";
    private MemberGroupNames? groups;

    private static BlockListModel Conditions(IPublishedElement block, string alias)
        => block.GetProperty(alias)?.GetValue() as BlockListModel ?? BlockListModel.Empty;

    internal static string ConditionAlias(string property) => property switch {
        Visibility => "appVisibilityCondition", Security => "appSecurityCondition", _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    internal static bool? ConstantResult(string property, string? comparison) => (property, comparison) switch {
        (Visibility, "Always visible") or (Security, "Always rendered") => true,
        (Visibility, "Never visible") or (Security, "Never rendered") => false,
        _ => null,
    };

    private static bool? ConstantResult(string property, IPublishedElement condition) =>
        condition.ContentType.Alias == ConditionAlias(property) ? ConstantResult(property, condition.Value<string>("comparison")) : false;

    internal static bool VariesByRequest(BlockGridItem item) =>
        Conditions(item.Content, Security).Any(entry => ConstantResult(Security, entry.Content) is null)
        || Conditions(item.Content, Visibility).Any(entry => ConstantResult(Visibility, entry.Content) is null);

    internal static bool HasSecurity(BlockGridItem item) =>
        Conditions(item.Content, Security).Any(entry => ConstantResult(Security, entry.Content) != true);

    public async Task<bool> MatchesAsync(BlockGridItem item, string alias, IPublishedContent page, ClaimsPrincipal? visitor,
        string? culture = null, FormDefinition? form = null, Func<Task<FormRecord?>>? load = null, CancellationToken ct = default)
    {
        foreach (var entry in Conditions(item.Content, alias))
        {
            var condition = entry.Content;
            if (condition.ContentType.Alias != ConditionAlias(alias)) return false;
            var comparison = condition.Value<string>("comparison");
            if (ConstantResult(alias, comparison) is { } passes)
            {
                if (!passes) return false;
                continue;
            }
            if (alias == Security) http.HttpContext?.KeepOutOfCaches();
            if (visitor is null) return false;
            if (FieldCondition.IsComparison(comparison))
            {
                var name = condition.Value<string>("item")?.Trim();
                if (string.IsNullOrEmpty(name)) return false;
                var itemValue = form is null ? PageValue(page, name, culture) : await FieldValueAsync(form, name, load);
                if (itemValue is not { } left || !FieldCondition.Compare(left.Kind, comparison!, left.Text, condition.Value<string>("literal") ?? "")) return false;
            }
            else if (!await VisitorMatchesAsync(condition, comparison, visitor, ct)) return false;
        }
        return true;
    }

    private async Task<bool> VisitorMatchesAsync(IPublishedElement condition, string? comparison, ClaimsPrincipal visitor, CancellationToken ct)
    {
        var user = await DataSourceQueries.ResolveReaderAsync(users, visitor, ct);
        if (user is null) return false;
        switch (comparison)
        {
            case "User is anonymous": return user.IsAnonymous;
            case "User is signed in": return !user.IsAnonymous;
            case "Backoffice user is in group": return user.IsInUserGroup(condition.Value<string>("literal")?.Trim() ?? "");
            case "Member is in group" when user.IsMember:
                groups ??= new((await memberGroups.GetAllAsync()).ToArray());
                var picked = condition.GetProperty("memberGroups")?.GetValue()?.ToString() ?? "";
                return groups.Resolve(picked)?.Any(user.IsInMemberGroup) == true;
            default: return false;
        }
    }

    private static (string Kind, string Text)? PageValue(IPublishedContent page, string name, string? culture)
    {
        if (page.GetProperty(name) is not { } property) return null;
        var value = property.GetValue(culture);
        var kind = value switch {
            decimal or double or float or long or int or short or byte => "number",
            DateTime or DateTimeOffset => "datetime", DateOnly => "date", TimeOnly => "time", _ => "text",
        };
        return (kind, ItemText(value));
    }

    private static async Task<(string Kind, string Text)?> FieldValueAsync(FormDefinition form, string name, Func<Task<FormRecord?>>? load)
    {
        if (form.Resource.Fields.FirstOrDefault(f => f.Key == name) is not { } field || load is null
            || await load() is not { } record || !record.Values.TryGetValue(name, out var value)) return null;
        return (field.Kind, ItemText(value));
    }

    private static string ItemText(object? value) => value switch {
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        JsonElement { ValueKind: JsonValueKind.True } => "true", JsonElement { ValueKind: JsonValueKind.False } => "false",
        _ => BindVariableProviders.Text(value) ?? "",
    };

    /// <summary>Checks the published path to a page-level block, including its enclosing layouts and Forms container.</summary>
    public async Task<bool> AllowsPathAsync(IPublishedContent page, Guid key, ClaimsPrincipal? visitor, string? culture, CancellationToken ct)
    {
        var path = AppBlockContent.PathTo(AppBlockContent.Blocks(page, culture), key, culture);
        return path is not null && await AllowsPathAsync(page, path, visitor, culture, ct);
    }

    internal async Task<bool> AllowsPathAsync(IPublishedContent page, IReadOnlyList<BlockGridItem> path, ClaimsPrincipal? visitor, string? culture, CancellationToken ct)
    {
        foreach (var item in path)
            if (!await MatchesAsync(item, Security, page, visitor, culture, ct: ct)) return false;
        return true;
    }

    /// <summary>Scopes field binding, projection, validation and child blocks to this caller and record.
    /// Only item comparisons need the record, which is loaded at most once.</summary>
    public async Task<FormDefinition> ForFormAsync(FormDefinition form, ClaimsPrincipal visitor, Func<Task<FormRecord?>> load,
        bool rendering, CancellationToken ct)
    {
        var denied = new HashSet<Guid>(form.DeniedBlocks);
        var hidden = new HashSet<Guid>();
        Task<FormRecord?>? record = null;
        Task<FormRecord?> Record() => record ??= load();
        async Task Visit(BlockGridItem item)
        {
            if (denied.Contains(item.ContentKey)) return;
            if (!await MatchesAsync(item, Security, form.Page, visitor, form.Culture, form, Record, ct))
            {
                foreach (var excluded in AppBlockContent.Flatten([item], form.Culture)) denied.Add(excluded.ContentKey);
                return;
            }
            if (rendering && !await MatchesAsync(item, Visibility, form.Page, visitor, form.Culture, form, Record, ct)) hidden.Add(item.ContentKey);
            foreach (var child in AppBlockContent.Children(item, form.Culture)) await Visit(child);
        }
        foreach (var child in form.Block.Areas.SelectMany(a => a)) await Visit(child);
        if (denied.Count == 0 && hidden.Count == 0) return form;
        var excludedFields = form.Fields.Where(f => denied.Contains(f.ContentKey)).Select(f => f.Content.Value<string>("field")!).ToHashSet(StringComparer.Ordinal);
        excludedFields.UnionWith(form.ExcludedFields);
        if (excludedFields.Count > 0)
        {
            // A picker writes several fields. Omit the companion as a whole when one of its bindings is secured.
            var pickers = plugins.OfType<AccountPickerBlock>().ToDictionary(p => p.Alias);
            foreach (var block in AppBlockContent.Flatten(form.Block.Areas.SelectMany(a => a), form.Culture))
            {
                if (denied.Contains(block.ContentKey) || !pickers.TryGetValue(block.Content.ContentType.Alias, out var picker)) continue;
                var settings = picker.ReadSettings(block.Content);
                if (settings.WritableFields().Concat(settings.FallbackFields.Values).Any(excludedFields.Contains)) denied.Add(block.ContentKey);
            }
        }
        return form with {
            DeniedBlocks = denied, InvisibleBlocks = hidden, ExcludedFields = excludedFields,
            Fields = form.Fields.Where(f => !denied.Contains(f.ContentKey)).ToArray(),
            Rules = form.Rules.Where(r => !excludedFields.Contains(r.Field)).ToArray(),
            AntiSpam = form.AntiSpam.Where(b => !denied.Contains(b.ContentKey)).ToArray(),
        };
    }
}
