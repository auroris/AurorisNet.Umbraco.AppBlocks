using Umbraco.Cms.Core.Models.Blocks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using AppBlocks.AntiSpam;
using AppBlocks.AccountPickers;
using AppBlocks.Attachments;
using AppBlocks.DataSources;
using AppBlocks.Forms;
using AppBlocks.DocumentOutput;
using AppBlocks.Schedules;
using AppBlocks.Tabular;

namespace AppBlocks.Blocks;

public sealed record BlockLocation(Guid Key, string Label);
public sealed record BlockProblem(string Message, IReadOnlyList<BlockLocation> Locations);

/// <summary>
/// Validates authored structure without executing a report or invoking a form operation. Forms, tabular forms and record owners
/// use registered C# contracts or published data sources and their database schemas. Call <see cref="LoadAsync"/> before validating.
/// </summary>
public sealed class AppBlocksValidator(FormResourceRegistry resources, PublishedDataCatalog? definitions = null, IContentService? contents = null,
    IOptions<AttachmentSettings>? attachments = null, ScheduleSources? schedules = null,
    IContentTypeService? contentTypes = null, IEnumerable<IAppBlockPlugin>? plugins = null, AntiSpamGate? antiSpam = null,
    DataSources.BindVariableProviders? providers = null, IOptions<TabularOptions>? tabular = null, IOptions<ScheduleOptions>? scheduleOptions = null,
    Umbraco.Cms.Core.Security.IBackOfficeSecurityAccessor? security = null, Authentication.ApplicationAccessPolicy? access = null)
    : INotificationAsyncHandler<ContentPublishingNotification>
{
    private static readonly string[] Aliases = ["appForm", "appFormInterface", "appFieldset", "appActions", AppBlockSchema.AttachmentsAlias, AppBlockSchema.AntiSpamAlias,
        AppBlockSchema.EmailLinkAlias, AppBlockSchema.ScheduleAlias, AppBlockSchema.TabularAlias, ..AppBlockSchema.LayoutAliases, ..AppBlockSchema.ContentAliases, ..AppBlockSchema.FieldAliases];
    /// <summary>Loads the published reference data before synchronous authored-value validation.</summary>
    public async Task LoadAsync()
    {
        await resources.LoadAsync();
        if (definitions is not null) await definitions.LoadAsync();
        if (schedules is not null) await schedules.LoadAsync();
    }

    public async Task HandleAsync(ContentPublishingNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.PublishedEntities.Any(AppBlockSchema.HasBlocks)) return;
        await LoadAsync();
        foreach (var page in notification.PublishedEntities.Where(AppBlockSchema.HasBlocks)) {
            foreach (var property in page.Properties.Where(p => AppBlockSchema.IsBlockEditor(p.PropertyType.PropertyEditorAlias))) {
            IEnumerable<string?> cultures = [null];
            if (property.PropertyType.VariesByCulture()) cultures = page.AvailableCultures.Where(culture => notification.IsPublishingCulture(page, culture));
            foreach (var culture in cultures) {
                var json = page.GetValue(property.Alias, culture)?.ToString();
                if (string.IsNullOrWhiteSpace(json)) continue;
                try {
                    var sets = BlockValueSets.Read(json, contentTypes);
                    if (security?.BackOfficeSecurity?.CurrentUser is { } current && access is not null && !access.IsBackOfficeAdministrator(current))
                    {
                        var publishedJson = page.GetValue(property.Alias, culture, published: true)?.ToString();
                        var publishedReports = ReportSources(publishedJson);
                        var draftReports = ReportSources(json);
                        if (draftReports.Any(entry => !publishedReports.TryGetValue(entry.Key, out var existing) || existing != entry.Value))
                        {
                            notification.CancelOperation(new EventMessage("AppBlocks", $"{page.Name} · {property.Alias}: Only AppBlocks administrators may add or change Interactive Report data sources.", EventMessageType.Error));
                            continue;
                        }
                    }
                    foreach (var set in sets) {
                        var warnings = new List<string>();
                        var errors = Problems(set, warnings);
                        foreach (var error in errors) {
                            var location = string.Join(" / ", error.Locations.Select(l => l.Label));
                            notification.CancelOperation(new EventMessage("AppBlocks", $"{page.Name} · {property.Alias}: {error.Message}" + (location.Length > 0 ? $" ({location})" : ""), EventMessageType.Error));
                        }
                        foreach (var warning in warnings.Distinct()) notification.Messages.Add(new EventMessage("AppBlocks", page.Name + ": " + warning, EventMessageType.Warning));
                    }
                } catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) {
                    notification.CancelOperation(new EventMessage("AppBlocks", page.Name + ": Check the saved block configuration.", EventMessageType.Error));
                }
            }
            }
        }
    }

    private Dictionary<Guid, string> ReportSources(string? json)
    {
        var result = new Dictionary<Guid, string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        foreach (var set in BlockValueSets.Read(json, contentTypes))
            foreach (var item in set.ContentData)
                if (Alias(item) == "appReport") result[item.Key] = Text(item, "dataSource");
        return result;
    }

    /// <summary>The errors publishing would report for one block value, as JSON; blank is an empty value and has none.</summary>
    public IReadOnlyList<string> Validate(string? json) => Validate(json, null);

    /// <summary>The errors for one block value, and its warnings into <paramref name="warnings"/>.</summary>
    public IReadOnlyList<string> Validate(string? json, List<string>? warnings)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return Validate(BlockValueSets.Parse(json), warnings); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { return ["The block structure is invalid: " + error.Message]; }
    }

    /// <summary>The publish checks and their block locations for a working copy in the editor.</summary>
    public IReadOnlyList<BlockProblem> EditorProblems(string json) => Problems(BlockValueSets.Parse(json), null);

    private IReadOnlyList<BlockProblem> Problems(BlockValue value, List<string>? warnings)
    {
        var locations = new Dictionary<int, List<BlockLocation>>();
        return Validate(value, warnings, locations).Select((message, i) => new BlockProblem(message, locations.GetValueOrDefault(i) ?? [])).ToArray();
    }

    /// <summary>
    /// A block value wherever a page holds one: content and layouts stand alone, a form judges what it holds, and fields, form actions
    /// and an Anti-spam block belong inside a form. A block this validator does not know is the site's own and is left to the site.
    /// </summary>
    private IReadOnlyList<string> Validate(BlockValue value, List<string>? warnings, Dictionary<int, List<BlockLocation>>? locations = null)
    {
        var errors = new List<string>();
        try {
            var data = value.ContentData.ToDictionary(d => d.Key);
            var visited = new HashSet<Guid>();
            var index = 0;
            foreach (var item in BlockValueSets.Layout(value)) VisitContent(item, "", ++index);

            void VisitContent(IBlockLayoutItem item, string parent, int position) {
                var key = item.ContentKey;
                var block = data[key]; var alias = Alias(block);
                var target = Location(block, alias, parent, position);
                var start = errors.Count;
                if (alias == "appForm") {
                    Form(item, block, data, visited, errors, warnings, locations, target);
                    Locate(start, errors.Count, locations, target);
                    return;
                }
                ValidateConditions(block, null, errors);
                if (!visited.Add(key)) throw new InvalidOperationException("Blocks cannot be repeated or cyclic.");
                if (Plugin(alias) is AccountPickerBlock) errors.Add("Place the account picker inside its form.");
                else if (Plugin(alias) is { } plugin) plugin.Validate(BlockValueSets.Element(block), errors);
                else if (alias == AppBlockSchema.AttachmentsAlias) Attachments(block, null, errors);
                else if (alias == AppBlockSchema.ScheduleAlias) Schedule(block, errors);
                else if (alias == AppBlockSchema.TabularAlias) Tabular(block, null, errors);
                else if (alias == AppBlockSchema.AntiSpamAlias) errors.Add("Place the Anti-spam block inside the form it protects.");
                else if (alias == AppBlockSchema.EmailLinkAlias) errors.Add("Place the Email link block inside a form.");
                else if (alias == "appActions" || AppBlockSchema.FieldAliases.Contains(alias)) errors.Add("Fields and form actions must be placed inside a form.");
                Locate(start, errors.Count, locations, target);
                var child = 0;
                foreach (var nested in BlockValueSets.Children(item)) VisitContent(nested, target.Label, ++child);
            }
        } catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException) {
            errors.Add("The block structure or starting view is invalid: " + error.Message);
        }
        return errors;
    }

    /// <summary>
    /// Print opens /api/paper-reports/{key}, which only a Paper report node answers. A node not yet imported is not judged, and
    /// neither is anything without <paramref name="contents"/>. Public so a companion's block that offers Print judges it the same way.
    /// </summary>
    public static void PaperReport(IContentService? contents, string picked, List<string> errors)
    {
        if (contents is null || PaperReportLinks.PickedKey(picked) is not { } key || contents.GetById(key) is not { } node) return;
        if (node.ContentType.Alias != PaperReportLinks.ContentTypeAlias) errors.Add($"Select a Paper report node for Paper report; {node.Name} is not one.");
    }

    private static void ValidateConditions(BlockItemData block, IFormResource? enclosingForm, List<string> errors, bool formBlock = false)
    {
        foreach (var property in new[] { BlockConditions.Visibility, BlockConditions.Security, BlockConditions.ReadOnly })
        {
            var raw = Text(block, property);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var label = property switch { BlockConditions.Security => "Security", BlockConditions.ReadOnly => "Read Only", _ => "Visibility" };
            var list = BlockValueSets.Parse(raw);
            var entries = list.ContentData.ToDictionary(e => e.Key);
            foreach (var entry in BlockValueSets.Layout(list))
            {
                var condition = entries[entry.ContentKey];
                var comparison = Text(condition, "comparison");
                var item = Text(condition, "item").Trim();
                if (condition.ContentTypeKey != AppBlockSchema.Key(BlockConditions.ConditionAlias(property))) errors.Add($"Choose a {label} condition.");
                else if (FieldCondition.IsComparison(comparison))
                {
                    if (item.Length == 0) errors.Add($"Enter an item for the {label} comparison.");
                    else if (enclosingForm is not null && (!formBlock || property == BlockConditions.ReadOnly) && !Guid.TryParse(item, out _) && !enclosingForm.Fields.Any(f => f.Key == item))
                        errors.Add($"The {label} item '{item}' is not a field of {enclosingForm.Alias}.");
                }
                else if (comparison is "Member is in group" or "User is outside member groups" && string.IsNullOrWhiteSpace(Text(condition, "memberGroups")))
                    errors.Add($"Pick a Member Group for {label}.");
                else if (comparison == "Backoffice user is in group" && string.IsNullOrWhiteSpace(Text(condition, "literal")))
                    errors.Add($"Pick a User Group for {label}.");
                else if (BlockConditions.ConstantResult(property, comparison) is null
                    && !(property == BlockConditions.ReadOnly && comparison == "User is outside member groups")
                    && comparison is not ("User is anonymous" or "User is signed in" or "Member is in group" or "Backoffice user is in group"))
                    errors.Add($"Choose a condition type for {label}.");
            }
        }
    }

    private static void Locate(int start, int end, Dictionary<int, List<BlockLocation>>? locations, BlockLocation target)
    {
        if (locations is null) return;
        for (var i = start; i < end; i++) locations.TryAdd(i, [target]);
    }

    private BlockLocation Location(BlockItemData block, string alias, string parent, int position)
    {
        var name = new[] { "heading", "label", "field" }.Select(p => Text(block, p)).FirstOrDefault(s => s.Length > 0)
            ?? contentTypes?.Get(block.ContentTypeKey)?.Name
            ?? (alias.StartsWith("app", StringComparison.Ordinal) ? alias[3..] : alias);
        if (string.IsNullOrEmpty(name)) name = "Block";
        return new(block.Key, parent.Length == 0 ? name : $"{parent} › {name} ({position})");
    }

    private void Form(IBlockLayoutItem layout, BlockItemData block, Dictionary<Guid, BlockItemData> data, HashSet<Guid> visited, List<string> errors, List<string>? warnings,
        Dictionary<int, List<BlockLocation>>? locations, BlockLocation formLocation)
    {
        if (Text(block, "paperReport") is { Length: > 0 } paper) PaperReport(contents, paper, errors);
        var formInterface = Text(block, "formInterface");
        if (string.IsNullOrWhiteSpace(formInterface)) { errors.Add("Each form needs exactly one Form Interface."); return; }
        var definition = BlockValueSets.Parse(formInterface);
        if (definition is not SingleBlockValue) { errors.Add("Recreate the Form Interface using the Single Block editor."); return; }
        var interfaces = definition.ContentData.ToArray();
        var interfaceLayout = BlockValueSets.Layout(definition).ToArray();
        if (interfaces.Length != 1 || Alias(interfaces[0]) != "appFormInterface" || interfaceLayout.Length != 1
            || interfaceLayout[0].ContentKey != interfaces[0].Key) { errors.Add("Each form needs exactly one Form Interface."); return; }
        var configuration = interfaces[0];
        // A class keeps its alias when a published data source has the same one, as the registry orders them.
        var resource = Text(configuration, "resource") is { Length: > 0 } alias ? resources.Find(alias) : null;
        if (resource is null) { errors.Add("Select a registered C# form class or a published data source."); return; }
        // A data source's fields come from its database; without them every binding would look wrong.
        if (resource is SqlDataSource { Problem: { } problem }) { errors.Add($"The data source {resource.Alias} is not available: {problem}"); return; }
        var fields = new Dictionary<string, (BlockItemData Block, BlockLocation Location)>(StringComparer.Ordinal);
        var links = new List<(string Binding, string Template, BlockLocation Target)>();
        var emailLinks = new List<(BlockItemData Block, BlockLocation Target)>();
        var duplicates = new Dictionary<string, List<BlockLocation>>(StringComparer.Ordinal);
        var actionBlocks = new List<BlockLocation>();
        var pickers = new List<(AccountPickerSettings Settings, BlockLocation Location)>();
        var checkBlocks = new List<BlockLocation>();
        void Visit(IBlockLayoutItem item, int depth, string parent, int position) {
            var key = item.ContentKey;
            if (!visited.Add(key)) throw new InvalidOperationException("Blocks cannot be repeated or cyclic.");
            var child = data[key]; var alias = Alias(child);
            var target = depth == 0 ? formLocation : Location(child, alias, parent, position);
            var start = errors.Count;
            ValidateConditions(child, resource, errors, formBlock: depth == 0);
            if (depth > 0 && (alias == "appForm" || Plugin(alias)?.CanNestInForm == false)) errors.Add("Forms and reports cannot be nested inside a form.");
            if (alias == "appActions") actionBlocks.Add(target);
            if (alias == AppBlockSchema.AntiSpamAlias) { checkBlocks.Add(target); AntiSpam(child, errors, warnings); }
            else if (alias == AppBlockSchema.EmailLinkAlias) emailLinks.Add((child, target));
            else if (alias == AppBlockSchema.AttachmentsAlias) Attachments(child, resource, errors);
            else if (alias == AppBlockSchema.ScheduleAlias) Schedule(child, errors);
            else if (alias == AppBlockSchema.TabularAlias) Tabular(child, resource, errors);
            else if (AppBlockSchema.FieldAliases.Contains(alias)) {
                var binding = Text(child, "field");
                if (!fields.TryAdd(binding, (child, target))) {
                    if (!duplicates.TryGetValue(binding, out var copies)) duplicates[binding] = copies = [fields[binding].Location];
                    copies.Add(target);
                }
                // A default of :APP.name that no provider declares would leave the field empty; a warning, as the provider is code that may come later.
                if (providers is not null && Text(child, "defaultValue").Trim() is var initial && initial.StartsWith(":APP.", StringComparison.OrdinalIgnoreCase)
                    && providers.UndeclaredWarning([initial[5..]]) is { } undeclared)
                    warnings?.Add($"The default of '{binding}': {undeclared}");
                var field = resource.Fields.FirstOrDefault(f => f.Key == binding);
                if (field is null) errors.Add($"'{binding}' is not a field of {resource.Alias}.");
                else if (!Compatible(alias, field, Text(child, "listOfValues").Length > 0, Flag(child, AppBlockField.MultipleAlias), Flag(child, AppBlockField.FreeTextAlias)))
                    errors.Add($"The block for '{binding}' does not match its {field.Kind} type.");
                if (definitions is not null && AppBlockField.UsesChoices(alias) && Text(child, "listOfValues") is { Length: > 0 } selected && definitions.FindList(selected) is null)
                    errors.Add($"Select a published list of values for '{binding}'.");
                if (alias == AppBlockSchema.SearchSelectAlias && Text(child, AppBlockField.MaxOptionsAlias).Trim() is { Length: > 0 } maxOptions
                    && (!int.TryParse(maxOptions, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var maxCount) || maxCount < 0))
                    errors.Add($"The maximum visible choices for '{binding}' must be a whole number of 0 or more.");
                if (alias == "appDisplay" && Text(child, AppBlockField.LinkTemplateAlias).Trim() is { Length: > 0 } template)
                {
                    ValidateLinkPage(child, template, errors);
                    links.Add((binding, template, target));
                }
            }
            else if (Plugin(alias) is AccountPickerBlock picker) {
                try { pickers.Add((picker.ReadSettings(key => Text(child, key).Trim()), target)); }
                catch (AccountPickerException error) { errors.Add(error.Message); }
            }
            else if (Plugin(alias) is { } plugin) plugin.Validate(BlockValueSets.Element(child), errors);
            else if (alias is not ("appForm" or "appFieldset" or "appActions") && !AppBlockSchema.LayoutAliases.Contains(alias) && !AppBlockSchema.ContentAliases.Contains(alias)) errors.Add("The form contains an unsupported block.");
            Locate(start, errors.Count, locations, target);
            var index = 0;
            foreach (var nested in BlockValueSets.Children(item)) Visit(nested, depth + 1, target.Label, ++index);
        }
        Visit(layout, 0, "", 1);
        var pickerFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (settings, target) in pickers) {
            var start = errors.Count;
            try {
                settings.CheckBindings(pickerFields,
                    field => fields.TryGetValue(field, out var item) && Alias(item.Block) == "appText" && !Flag(item.Block, "readOnly")
                        && resource.Fields.Any(f => f.Key == field && f.Kind == "text" && !f.ReadOnly),
                    field => fields.ContainsKey(field) && resource.Fields.Any(f => f.Key == field && f.Kind == "text" && f.ReadOnly));
            }
            catch (AccountPickerException error) { errors.Add(error.Message); }
            Locate(start, errors.Count, locations, target);
        }
        foreach (var (binding, copies) in duplicates) {
            locations?.Add(errors.Count, copies);
            errors.Add($"Field '{binding}' appears more than once.");
        }
        errors.AddRange(FieldRule.Problems(FieldRule.Authored(Text(block, "fieldRules"), resource.Fields), fields.Keys));
        var parent = Text(block, "parentForm");
        var linkField = Text(block, "linkField");
        if (parent.Length > 0 || linkField.Length > 0 || Text(block, "parentPage").Length > 0) {
            if (!Guid.TryParse(parent, out var parentKey) || !resource.Fields.Any(f => f.Key == linkField && !f.ReadOnly)) errors.Add("Choose a parent form and a writable detail link field.");
            if (parentKey == block.Key) errors.Add("Choose a different form as the parent.");
            if (data.TryGetValue(parentKey, out var master) && Alias(master) == "appForm") {
                var masterInterface = BlockValueSets.Parse(Text(master, "formInterface")).ContentData.FirstOrDefault();
                string Parameter(BlockItemData item, bool detail) => Text(item, "keyParameter") is { Length: > 0 } name ? name : detail ? "detailId" : "id";
                if (masterInterface is not null && Parameter(masterInterface, Text(master, "parentForm").Length > 0).Equals(Parameter(configuration, true), StringComparison.OrdinalIgnoreCase))
                    errors.Add("Give the parent and detail forms different record key query parameters.");
            }
        }
        if (actionBlocks.Count > 1) { locations?.Add(errors.Count, actionBlocks); errors.Add("Use one Form actions block per form."); }
        if (checkBlocks.Count > 1) { locations?.Add(errors.Count, checkBlocks); errors.Add("Use one Anti-spam block per form."); }
        // A link is built in the browser from the record the form loaded, which holds the form's own fields and no others.
        foreach (var (binding, template, target) in links) {
            var start = errors.Count;
            if (AppBlockField.LinkProblem(template) is { } unsafeLink) errors.Add($"The link template of '{binding}' is not one a page may link to. {unsafeLink}");
            foreach (var name in AppBlockField.LinkFields(template).Where(name => !fields.ContainsKey(name)))
                errors.Add($"The link template of '{binding}' names {{{name}}}, which is not a field on this form. Add a field block for it; it may be hidden.");
            Locate(start, errors.Count, locations, target);
        }
        foreach (var (emailBlock, target) in emailLinks) {
            var start = errors.Count;
            if (string.IsNullOrWhiteSpace(Text(emailBlock, "to")))
                errors.Add("Enter a To address or template for the Email link block.");
            var referenced = new[] { "to", "cc", "bcc", "subject", "body", "linkText" }
                .SelectMany(property => AppBlockField.LinkFields(Text(emailBlock, property)))
                .Distinct(StringComparer.Ordinal);
            foreach (var name in referenced.Where(name => !fields.ContainsKey(name)))
                errors.Add($"The Email link template names {{{name}}}, which is not a field on this form. Add a field block for it; it may be hidden.");
            Locate(start, errors.Count, locations, target);
        }
        // A form that edits some fields of existing records is a real use, so a required field it does not edit is a warning: the form
        // still updates, and a create is refused at run time, where FormDefinition.Validate asks for every required field.
        if (!Flag(configuration, "viewOnly")) {
            var missing = resource.Fields.Where(f => f.Required && !f.ReadOnly && !f.HasServerDefault)
                .Where(f => f.Key != linkField).Where(f => !fields.TryGetValue(f.Key, out var item) || Flag(item.Block, "readOnly") || Alias(item.Block) == "appDisplay").Select(f => f.Key).ToArray();
            if (missing.Length > 0) warnings?.Add($"This form cannot create records: the required field{(missing.Length == 1 ? "" : "s")} {string.Join(", ", missing.Select(m => "'" + m + "'"))} {(missing.Length == 1 ? "is" : "are")} not editable in it.");
        }
    }

    /// <summary>
    /// An Attachments block names whose files it shows, or sits in a form whose record owns them. Its limits may narrow
    /// the site's and never widen them; a block that asks for more than the site allows is refused rather than quietly capped.
    /// </summary>
    private void Attachments(BlockItemData block, IFormResource? form, List<string> errors)
    {
        Owner(block, form, AttachmentsOwner, AttachmentBlock.OwnerResourceAlias, AttachmentBlock.OwnerKeyAlias, errors);
        var settings = attachments?.Value ?? new AttachmentSettings();
        if (Text(block, AttachmentBlock.MaxSizeAlias) is { Length: > 0 } size)
        {
            if (!long.TryParse(size, out var megabytes) || megabytes < 1) errors.Add("The largest file must be a whole number of megabytes, at least 1.");
            else if (megabytes * 1024 * 1024 > settings.MaxFileSize) errors.Add($"The largest file cannot exceed the site's limit of {settings.MaxFileSize / (1024 * 1024)} MB.");
        }
        var extensions = AttachmentLimits.Parse(Text(block, AttachmentBlock.AllowedTypesAlias));
        foreach (var extension in extensions)
        {
            var normalized = (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
            if (normalized.Length < 2 || normalized.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))) errors.Add($"'{extension}' is not a file extension; write them as .pdf .png.");
            else if (settings.AllowedExtensions.Count > 0 && !settings.AllowedExtensions.Any(allowed => (allowed.StartsWith('.') ? allowed : "." + allowed).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"The site does not accept {normalized} files (Attachments:AllowedExtensions).");
        }
    }

    /// <summary>
    /// An Anti-spam block names a service this site offers and the public site key that goes with it. A secret key missing
    /// from the server is only a warning: it is an administrator's to add, and a site's content may well arrive before its keys.
    /// Without the service list, as in a test without a site, the block's own settings are all that is judged.
    /// </summary>
    private void AntiSpam(BlockItemData block, List<string> errors, List<string>? warnings)
    {
        var alias = Text(block, "service").Trim();
        var siteKey = Text(block, "siteKey").Trim();
        if (alias.Length == 0) { errors.Add("Choose a service for the Anti-spam block."); return; }
        if (Text(block, "scoreThreshold").Trim() is { Length: > 0 } threshold && AntiSpamBlock.Threshold(threshold) is null) errors.Add("The Anti-spam block's score threshold must be a number from 0 to 1, such as 0.5.");
        if (siteKey.Length > 0 && !AntiSpamBlock.IsSiteKey(siteKey)) errors.Add("A site key holds letters, digits, hyphens, underscores, dots and colons.");
        if (siteKey.Length > 0 && antiSpam?.IsSecretKey(siteKey) == true) errors.Add("The Anti-spam block's site key is one of this site's secret keys, which must never be in a block. Enter the public site key.");
        if (alias.Equals(AntiSpamBlock.Honeypot, StringComparison.OrdinalIgnoreCase) || antiSpam is null) return;
        if (antiSpam.Find(alias) is not { } service) { errors.Add($"'{alias}' is not an anti-spam service this site offers."); return; }
        if (siteKey.Length == 0 && antiSpam.SiteKey(new(Guid.Empty, service.Alias, null, AntiSpamBlock.DefaultScoreThreshold, null, null)) is null)
            errors.Add($"Enter the site key for {service.Name}, or set one in Settings, AppBlocks, Anti-spam.");
        if (antiSpam.SecretState(service.Alias) is not (AntiSpamSecretState.Configuration or AntiSpamSecretState.Saved))
            warnings?.Add($"{service.Name} has no secret key yet, so this form refuses every submission until an administrator adds it in Settings, AppBlocks, Anti-spam.");
    }

    /// <summary>The words an owner rule uses: the block's name with its article, what it holds, and what an owner owns.</summary>
    private sealed record OwnerWords(string Name, string Article, string Things, string Owned);

    private static readonly OwnerWords AttachmentsOwner = new("Attachments", "An", "files", "attachments");
    private static readonly OwnerWords TabularOwner = new("Tabular form", "A", "rows", "rows");

    /// <summary>
    /// The owner rules of a block that follows a record, such as Attachments or a tabular form: inside a form it may
    /// leave the owner to the form's record; otherwise it names a registered resource and a key the request can supply.
    /// </summary>
    private void Owner(BlockItemData block, IFormResource? form, OwnerWords words, string resourceAlias, string keyAlias, List<string> errors)
    {
        var owner = Text(block, resourceAlias);
        var key = Text(block, keyAlias);
        if (owner.Length == 0)
        {
            if (form is null) errors.Add($"Place the {words.Name} block inside a form, or select the resource that owns the {words.Things}.");
            if (key.Length > 0) errors.Add($"Leave the {words.Name} block's owner key empty when the {words.Things} belong to the form's record, or select the owner resource.");
        }
        else
        {
            if (resources.Find(owner) is null) errors.Add($"'{owner}' is not a registered form resource or a published data source, so it cannot own {words.Owned}.");
            if (key.Length == 0) errors.Add($"Give the {words.Name} block an owner key: :PARAM.name, :FIELD.alias or a literal key.");
            else if (key.StartsWith(":FIELD.", StringComparison.OrdinalIgnoreCase))
            {
                if (form is null) errors.Add($"{words.Article} {words.Name} block outside a form has no record to take :FIELD values from; use :PARAM.name or place it in a form.");
                else if (!form.Fields.Any(f => f.Key.Equals(key[7..], StringComparison.OrdinalIgnoreCase))) errors.Add($"'{key[7..]}' is not a field of {form.Alias}, so the {words.Name} block cannot take its owner key from it.");
            }
            else if (key.StartsWith(":PARAM.", StringComparison.OrdinalIgnoreCase) && key.Length == 7) errors.Add("Name the page parameter after :PARAM.");
        }
    }

    /// <summary>
    /// A tabular form edits the rows of a published data source that belong to one record, so the data source must be
    /// one the engine writes, the link field one it can set on every new row, and every column a field of it. A new
    /// row must be completable from the columns, the link and the defaults, unless the block never adds rows. Without
    /// a catalogue, as in a test without content, a data source the registry does not know is not judged.
    /// </summary>
    private void Tabular(BlockItemData block, IFormResource? form, List<string> errors)
    {
        Owner(block, form, TabularOwner, TabularBlock.OwnerResourceAlias, TabularBlock.OwnerKeyAlias, errors);
        var maxRows = tabular?.Value.MaxRows ?? TabularBlock.MaxRowsLimit;
        if (Text(block, TabularBlock.MaxRowsAlias) is { Length: > 0 } rows
            && (!int.TryParse(rows, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var limit) || limit < 1 || limit > maxRows))
            errors.Add($"The tabular form's row limit must be a whole number from 1 to {maxRows}.");
        var settings = TabularBlock.Read(alias => Text(block, alias), maxRows);
        if (settings.DataSource.Length == 0) { errors.Add("Select a published data source for the tabular form."); return; }
        var resource = resources.Find(settings.DataSource);
        if (resource is null && definitions is null) return;
        if (resource is null) { errors.Add($"'{settings.DataSource}' is not a published data source, so the tabular form cannot edit its rows."); return; }
        if (resource is not SqlDataSource source) { errors.Add("Select a published data source for the tabular form; the rows of a C# resource cannot be edited as a table."); return; }
        // Its fields come from its database; without them every name would look wrong.
        if (source.Problem is { } problem) { errors.Add($"The data source {source.Alias} is not available: {problem}"); return; }
        var fields = source.Fields;
        FormField? Field(string name) => fields.FirstOrDefault(f => f.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        var link = settings.LinkField.Length == 0 ? null : Field(settings.LinkField);
        if (settings.LinkField.Length == 0) errors.Add($"Name the tabular form's link field: the field of {source.Alias} that holds the record's key.");
        else if (link is null) errors.Add($"'{settings.LinkField}' is not a field of {source.Alias}, so it cannot link rows to their record.");
        else if (link.ReadOnly) errors.Add("The link field must be a field the data source writes; keys the database assigns and read-only fields cannot link rows to their record.");
        foreach (var name in settings.Fields)
        {
            if (Field(name) is null) errors.Add($"'{name}' is not a field of {source.Alias}, so the tabular form cannot show it.");
            else if (name.Equals(settings.LinkField, StringComparison.OrdinalIgnoreCase)) errors.Add("The link field cannot be a column of the tabular form; the server sets it on every row.");
        }
        if (settings.ReadOnly || settings.HideAdd) return;
        var shown = settings.Columns(fields).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(f => f.Required && !f.ReadOnly && !f.HasServerDefault && !shown.Contains(f.Key) && f != link))
            errors.Add($"Required field '{field.Key}' of {source.Alias} must be a column of the tabular form, or have a default.");
    }

    /// <summary>
    /// A Schedule block names two sources that must exist and carry the columns the layout reads, which the catalogue
    /// knows from the sources' schemas. Without a catalogue, as in a test without content, only the settings are checked.
    /// </summary>
    private void Schedule(BlockItemData block, List<string> errors)
    {
        var settings = ScheduleSettings.Read(alias => Text(block, alias));
        ValidateLinkPage(block, settings.LinkTemplate, errors);
        errors.AddRange(ScheduleValidation.Validate(settings, schedules is null || definitions is null ? null : schedules.Schema, Text(block, ScheduleSettings.ColumnsAlias), scheduleOptions?.Value));
    }

    private static void ValidateLinkPage(BlockItemData block, string? template, List<string> errors)
    {
        if (Text(block, "linkPage").Length > 0 && template is { Length: > 0 } && template[0] is not ('?' or '#'))
            errors.Add("With a link page selected, start the link template with ? for a query string or # for a fragment.");
    }

    internal static bool Compatible(string alias, FormField field, bool hasListOfValues, bool multiple, bool allowFreeText = false)
    {
        if (alias == AppBlockSchema.SearchSelectAlias && allowFreeText)
            return field.Kind == "text" || !multiple && field.Kind == "number";
        return Compatible(alias == AppBlockSchema.SearchSelectAlias && multiple ? AppBlockSchema.ShuttleAlias : alias, field, hasListOfValues);
    }

    public static bool Compatible(string alias, FormField field, bool hasListOfValues = false) => alias == "appDisplay" || alias switch {
        "appText" or "appTextArea" or AppBlockSchema.ColorAlias => field.Kind == "text",
        AppBlockSchema.ShuttleAlias => field.Kind == "text" && (hasListOfValues || field.Choices is { Count: > 0 }),
        "appSelect" or AppBlockSchema.SearchSelectAlias => hasListOfValues ? field.Kind is "text" or "number" : field.Kind == "text" && field.Choices is { Count: > 0 },
        "appNumber" => field.Kind == "number", "appDate" => field.Kind == "date", "appTime" => field.Kind == "time",
        "appDateTime" => field.Kind == "datetime", "appCheckbox" => field.Kind == "checkbox", _ => false,
    };
    private IAppBlockPlugin? Plugin(string alias) => plugins?.FirstOrDefault(p => p.Alias == alias);
    private Dictionary<Guid, string>? aliasesByKey;
    /// <summary>The package's and the plugins' aliases by content type key, worked out once, since each key is a hash of its alias.</summary>
    private string Alias(BlockItemData block)
    {
        if (aliasesByKey is null)
        {
            var keys = new Dictionary<Guid, string>();
            foreach (var alias in Aliases.Concat(plugins?.Select(p => p.Alias) ?? [])) keys.TryAdd(AppBlockSchema.Key(alias), alias);
            aliasesByKey = keys;
        }
        return aliasesByKey.GetValueOrDefault(block.ContentTypeKey) ?? "";
    }
    private static string Text(BlockItemData block, string alias) => BlockValueSets.Text(block, alias);
    private static bool Flag(BlockItemData block, string alias) => Text(block, alias) is "true" or "True" or "1";
}
