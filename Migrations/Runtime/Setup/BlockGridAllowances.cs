using System.Text.Json.Nodes;
using AppBlocks.Connections;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;

namespace AppBlocks.Setup;

/// <summary>
/// An element type allowed in a Block Grid or Block List data type, registered through <see cref="AppBlocksBuilder.AllowInBlockGrid"/>,
/// <see cref="AppBlocksBuilder.AllowInBlockList"/>, or directly via DI (<c>services.AddSingleton(new BlockGridAllowance(...))</c>).
/// </summary>
public sealed record BlockGridAllowance(
    string DataType,
    string ElementType,
    int ColumnSpan = 12,
    int RowSpan = 1,
    string? Area = null,
    string? Group = null,
    bool RestrictToGroupAreas = false,
    string? SettingsElementTypeAlias = null,
    int[]? ColumnSpanOptions = null);

/// <summary>
/// Published once at startup after AppBlocks has ensured its schema definitions, applied Block Grid/List allowances,
/// and run any pending content upgrades: from AppBlocks' <see cref="UmbracoApplicationStartedNotification"/> pass, which on a
/// boot that installs or upgrades comes after Umbraco's migrations. Downstream startup handlers can subscribe to this
/// notification instead of racing <see cref="UmbracoApplicationStartedNotification"/>.
/// </summary>
public sealed class AppBlocksReadyNotification : INotification;

/// <summary>
/// Adds each element type registered with <see cref="AppBlocksBuilder.AllowInBlockGrid"/>, <see cref="AppBlocksBuilder.AllowInBlockList"/>,
/// or DI <see cref="BlockGridAllowance"/> singletons to its Block Grid or Block List data type once the definitions
/// are there: at startup when nothing waits to be upgraded, after Umbraco finishes an unattended installation or upgrade, and when an element type it
/// names is saved, as a package's own startup may create one after this ran. Each pass reads the data type afresh and saves it only when
/// it adds something, so repeating it changes nothing.
/// </summary>
public sealed class BlockGridAllowances(AppBlocksBuilder builder, IRuntimeState runtime, IDataTypeService dataTypes, IContentTypeService contentTypes,
    IConfigurationEditorJsonSerializer serializer, ILogger<BlockGridAllowances> logger, IServiceScopeFactory services,
    IEnumerable<BlockGridAllowance>? registeredAllowances = null, IEventAggregator? events = null)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>, INotificationAsyncHandler<MigrationPlansExecutedNotification>,
        INotificationAsyncHandler<ContentTypeSavedNotification>
{
    private IReadOnlyList<BlockGridAllowance> AllAllowances()
        => registeredAllowances is null ? builder.BlockGridAllowances : [.. builder.BlockGridAllowances, .. registeredAllowances];

    public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken ct)
        => runtime.Level == RuntimeLevel.Run ? InstallAndApplyAsync(ct, upgradeContent: true, publishReady: true) : Task.CompletedTask;

    // Not ready yet: the content upgrades have not run. Umbraco starts the application after its migrations, and that pass publishes.
    public Task HandleAsync(MigrationPlansExecutedNotification notification, CancellationToken ct)
        => InstallAndApplyAsync(ct, upgradeContent: false, publishReady: false);

    /// <summary>
    /// Installs the definitions that are missing and applies every registration now. For a host that changes Block Grid/List data types at a
    /// point of its own, such as an import of its saved definitions, and uses the grids before the site has started.
    /// </summary>
    public Task ApplyAsync(CancellationToken ct) => InstallAndApplyAsync(ct, upgradeContent: true, publishReady: false);

    private async Task InstallAndApplyAsync(CancellationToken ct, bool upgradeContent = false, bool publishReady = false)
    {
        using var scope = services.CreateScope();
        var installer = scope.ServiceProvider.GetRequiredService<SchemaInstaller>();
        await installer.EnsureAsync(ct);
        await ApplyAsync(AllAllowances(), ct);
        if (upgradeContent) await installer.UpgradeContentAsync(ct);
        if (publishReady && upgradeContent && events is not null && Interlocked.Exchange(ref builder.ReadyPublished, 1) == 0)
            await events.PublishAsync(new AppBlocksReadyNotification(), ct);
    }

    public Task HandleAsync(ContentTypeSavedNotification notification, CancellationToken ct)
    {
        var saved = notification.SavedEntities.Select(type => type.Alias).ToHashSet(StringComparer.Ordinal);
        return ApplyAsync(AllAllowances().Where(allowance =>
            saved.Contains(allowance.ElementType)
            || allowance.SettingsElementTypeAlias is { Length: > 0 } settings && saved.Contains(settings)).ToArray(), ct);
    }

    /// <summary>Applies every registration; a data type or element type that is not there yet is skipped and tried at the next pass.</summary>
    internal async Task ApplyAsync(IReadOnlyList<BlockGridAllowance> allowances, CancellationToken ct)
    {
        foreach (var group in allowances.GroupBy(allowance => allowance.DataType, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            try { await ApplyAsync(group.Key, group.ToArray()); }
            // One grid or list that cannot be changed must not stop the site starting; the next start tries again.
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "AppBlocks could not allow the registered blocks in the data type {DataType}.", group.Key);
            }
        }
    }

    private async Task ApplyAsync(string name, IReadOnlyList<BlockGridAllowance> allowances)
    {
        var dataType = await dataTypes.GetAsync(name);
        if (dataType is not { Editor: { } editor }
            || dataType.EditorAlias is not (Constants.PropertyEditors.Aliases.BlockGrid or Constants.PropertyEditors.Aliases.BlockList))
        {
            logger.LogDebug("No Block Grid or Block List data type is named {DataType} yet, so its registered blocks wait.", name);
            return;
        }
        var isGrid = dataType.EditorAlias == Constants.PropertyEditors.Aliases.BlockGrid;
        var configurationEditor = editor.GetConfigurationEditor();
        var configuration = JsonNode.Parse(configurationEditor.ToDatabase(dataType.ConfigurationData, serializer))?.AsObject() ?? [];
        if (configuration["blocks"] is not JsonArray blocks) configuration["blocks"] = blocks = [];
        var changed = false;
        foreach (var allowance in allowances)
        {
            if (contentTypes.Get(allowance.ElementType) is not { IsElement: true } element)
            {
                logger.LogDebug("No element type {Alias} yet, so it waits to be allowed in {DataType}.", allowance.ElementType, name);
                continue;
            }
            string? settingsKey = null;
            if (!string.IsNullOrWhiteSpace(allowance.SettingsElementTypeAlias))
            {
                if (contentTypes.Get(allowance.SettingsElementTypeAlias) is { IsElement: true } settingsElement)
                    settingsKey = settingsElement.Key.ToString();
                else
                    logger.LogDebug("No settings element type {Alias} yet for {Element} in {DataType}.", allowance.SettingsElementTypeAlias, allowance.ElementType, name);
            }
            var key = element.Key.ToString();
            var existing = blocks.OfType<JsonObject>().FirstOrDefault(block => Same(block["contentElementTypeKey"], key));
            if (existing is null)
            {
                JsonObject block;
                if (isGrid)
                {
                    var spans = allowance.ColumnSpanOptions is { Length: > 0 } configuredSpans
                        ? configuredSpans.Where(s => s > 0).Distinct().ToArray()
                        : [];
                    if (spans.Length == 0) spans = [Math.Max(1, allowance.ColumnSpan)];
                    block = new JsonObject {
                        ["contentElementTypeKey"] = key,
                        ["allowAtRoot"] = allowance.Area is null && !allowance.RestrictToGroupAreas,
                        ["allowInAreas"] = !allowance.RestrictToGroupAreas,
                        ["columnSpanOptions"] = new JsonArray(spans.Select(span => (JsonNode)new JsonObject { ["columnSpan"] = span }).ToArray()),
                        ["rowMinSpan"] = Math.Max(1, allowance.RowSpan),
                        ["rowMaxSpan"] = Math.Max(1, allowance.RowSpan),
                        ["areas"] = new JsonArray(),
                    };
                    if (allowance.Group is { } group && configuration["blockGroups"] is JsonArray groups &&
                        groups.FirstOrDefault(item => Same(item?["name"], group))?["key"] is { } groupKey)
                        block["groupKey"] = groupKey.DeepClone();
                }
                else
                {
                    block = new JsonObject { ["contentElementTypeKey"] = key };
                }
                if (settingsKey is not null) block["settingsElementTypeKey"] = settingsKey;
                blocks.Add(block);
                changed = true;
            }
            else if (settingsKey is not null && string.IsNullOrWhiteSpace(existing["settingsElementTypeKey"]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? existing["settingsElementTypeKey"]!.GetValue<string>() : null))
            {
                existing["settingsElementTypeKey"] = settingsKey;
                changed = true;
            }

            if (!isGrid || allowance.Area is not { } area) continue;
            var target = blocks.SelectMany(block => block?["areas"] as JsonArray ?? []).FirstOrDefault(candidate =>
                Same(candidate?["key"], area) || Same(candidate?["alias"], area));
            if (target is null) { logger.LogWarning("The Block Grid data type {DataType} has no area {Area}, so {Alias} is not allowed in one.", name, area, allowance.ElementType); continue; }
            // An area with no list of its own takes any block the grid allows in areas.
            if (target["specifiedAllowance"] is JsonArray { Count: > 0 } allowed && !allowed.Any(entry => Same(entry?["elementTypeKey"], key)))
            {
                allowed.Add(new JsonObject { ["elementTypeKey"] = key });
                changed = true;
            }
        }
        if (!changed) return;
        dataType.ConfigurationData = configurationEditor.FromDatabase(configuration.ToJsonString(), serializer);
        var result = await dataTypes.UpdateAsync(dataType, Constants.Security.SuperUserKey);
        if (!result.Success) throw new InvalidOperationException($"The data type could not be saved: {result.Status}.");
        logger.LogInformation("Allowed {Aliases} in the {Kind} data type {DataType}.",
            string.Join(", ", allowances.Select(a => a.ElementType)), isGrid ? "Block Grid" : "Block List", name);
    }

    private static bool Same(JsonNode? node, string value) => node?.GetValueKind() == System.Text.Json.JsonValueKind.String
        && node.GetValue<string>().Equals(value, StringComparison.OrdinalIgnoreCase);
}
