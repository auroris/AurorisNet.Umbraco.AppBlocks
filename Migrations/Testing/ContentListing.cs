using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace AppBlocks.Testing;

/// <summary>What <see cref="ContentListing.OfAsync"/> lists.</summary>
public sealed class ContentListingOptions
{
    /// <summary>The documents to list; every document when null. A document left out is still named where a listed one refers to it.</summary>
    public Func<IContent, bool>? Include { get; init; }

    /// <summary>The keys of library elements to list after the documents, such as a notice that several pages show.</summary>
    public IReadOnlyCollection<Guid> Elements { get; init; } = [];
}

/// <summary>A listing that <see cref="ContentCheck"/> found changed.</summary>
public sealed class ContentCheckException(string message) : Exception(message);

/// <summary>
/// The content of a site as text, for a test that keeps what an application installs under source control and compares a new
/// installation, or one updated from an earlier release, with it. Each document has its path, key, type, template, state and access
/// rule, then its property values. A Block Grid, Block List or Single Block value is listed as its blocks in the order of the
/// layout, each with its type's alias, its key and its values, and the blocks in its areas indented under it. A value longer than
/// one line is listed once, at the end, under the first eight characters of its SHA-256, which stands for it wherever it occurs.
/// </summary>
/// <remarks>
/// Node ids, dates and version numbers are left out, since they differ between two sites with the same content. The key of a
/// document, content type, data type, template or listed element is written as its name where a value refers to it. Any other key
/// is written as it is, so a block installed without a stable key shows as a difference between two installations.
/// </remarks>
public static class ContentListing
{
    /// <summary>The listing of the documents <paramref name="options"/> include, in the order of the content tree.</summary>
    /// <param name="services">A service scope of the running site.</param>
    /// <param name="options">Which documents and library elements to list; every document and no element when null.</param>
    public static async Task<string> OfAsync(IServiceProvider services, ContentListingOptions? options = null)
    {
        options ??= new();
        var contents = services.GetRequiredService<IContentService>();
        var access = services.GetRequiredService<IPublicAccessService>();
        var templates = (await services.GetRequiredService<ITemplateService>().GetAllAsync(Array.Empty<string>())).ToDictionary(template => template.Id);
        var documents = new List<(IContent Node, string Path)>();
#pragma warning disable CS0618 // The content service's synchronous paging, which a listing of a whole tree needs no more than.
        void Walk(IEnumerable<IContent> nodes, string parent)
        {
            foreach (var node in nodes.OrderBy(node => node.SortOrder).ThenBy(node => node.Name, StringComparer.Ordinal))
            {
                var path = parent + "/" + node.Name;
                documents.Add((node, path));
                Walk(contents.GetPagedChildren(node.Id, 0, int.MaxValue, out _), path);
            }
        }
        Walk(contents.GetRootContent(), "");
#pragma warning restore CS0618

        var writer = new Writer();
        foreach (var (node, path) in documents) writer.Labels[node.Key] = "{" + path + "}";
        foreach (var type in services.GetRequiredService<IContentTypeService>().GetAll())
        {
            writer.Types[type.Key] = type.Alias;
            writer.Labels.TryAdd(type.Key, "{type " + type.Alias + "}");
        }
        foreach (var dataType in await services.GetRequiredService<IDataTypeService>().GetAllAsync())
        {
            writer.Labels.TryAdd(dataType.Key, "{data type " + dataType.Name + "}");
            if (dataType.ConfigurationObject is not BlockGridConfiguration grid) continue;
            foreach (var area in grid.Blocks.SelectMany(block => block.Areas))
                if (area.Alias is { Length: > 0 } alias) writer.Areas.TryAdd(area.Key, alias);
        }
        foreach (var template in templates.Values) writer.Labels.TryAdd(template.Key, "{template " + template.Alias + "}");
        var elements = new List<IElement>();
        var elementService = services.GetRequiredService<IElementEditingService>();
        foreach (var key in options.Elements)
        {
            if (await elementService.GetAsync(key) is not { } element) continue;
            elements.Add(element);
            writer.Labels[element.Key] = "{element " + element.Name + "}";
        }

        var paths = documents.ToDictionary(document => document.Node.Id, document => document.Path);
        foreach (var (node, path) in documents)
        {
            if (options.Include?.Invoke(node) == false) continue;
            writer.Line(0, path);
            var facts = new List<string> { "key " + node.Key, "type " + node.ContentType.Alias };
            if (node.TemplateId is { } templateId && templates.TryGetValue(templateId, out var template)) facts.Add("template " + template.Alias);
            facts.Add(State(node, node.ContentType.VariesByCulture()));
            writer.Line(1, string.Join("; ", facts));
            if (access.GetEntryForContent(node) is { } entry && entry.ProtectedNodeId == node.Id)
                writer.Line(1, "access: " + string.Join(", ", entry.Rules.Select(rule => rule.RuleType + "=" + rule.RuleValue).Order(StringComparer.Ordinal))
                    + "; sign in at {" + paths.GetValueOrDefault(entry.LoginNodeId, "?") + "}; refused at {" + paths.GetValueOrDefault(entry.NoAccessNodeId, "?") + "}");
            writer.Properties(node);
            writer.Line(0, "");
        }
        foreach (var element in elements.OrderBy(element => element.Name, StringComparer.Ordinal))
        {
            writer.Line(0, "ELEMENT " + element.Name);
            writer.Line(1, $"key {element.Key}; type {element.ContentType.Alias}; {State(element, element.ContentType.VariesByCulture())}");
            writer.Properties(element);
            writer.Line(0, "");
        }
        foreach (var (hash, text) in writer.Texts)
        {
            writer.Line(0, "TEXT " + hash);
            foreach (var line in text.Split('\n')) writer.Line(1, "| " + line);
            writer.Line(0, "");
        }
        return writer.ToString();
    }

    private static string State(IPublishableContentBase content, bool variesByCulture)
    {
        static string Of(bool published, bool edited) => published ? edited ? "published, with unpublished changes" : "published" : "unpublished";
        if (!variesByCulture) return Of(content.Published, content.Edited);
        return string.Join("; ", content.AvailableCultures.Order(StringComparer.Ordinal)
            .Select(culture => $"{culture} \"{content.GetCultureName(culture)}\" {Of(content.IsCulturePublished(culture), content.IsCultureEdited(culture))}"));
    }

    private sealed class Writer
    {
        private static readonly Regex Keys = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?<=umb://[a-z-]+/)[0-9a-fA-F]{32}",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private readonly StringBuilder text = new();

        public Dictionary<Guid, string> Labels { get; } = [];
        public Dictionary<Guid, string> Types { get; } = [];
        public Dictionary<Guid, string> Areas { get; } = [];
        public SortedDictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        public override string ToString() => text.ToString();

        public void Line(int indent, string line) => text.Append(' ', indent * 2).Append(line).Append('\n');

        /// <summary>A document's or element's values: the published one where it is published, and the draft where that differs.</summary>
        public void Properties(IPublishableContentBase content)
        {
            foreach (var property in content.Properties.OrderBy(property => property.Alias, StringComparer.Ordinal))
                foreach (var value in property.Values.OrderBy(value => value.Culture, StringComparer.Ordinal).ThenBy(value => value.Segment, StringComparer.Ordinal))
                {
                    var name = property.Alias + (value.Culture is null ? "" : " (" + value.Culture + (value.Segment is null ? "" : ", " + value.Segment) + ")");
                    var published = value.Culture is null ? content.Published : content.IsCulturePublished(value.Culture);
                    Value(name, published ? value.PublishedValue : value.EditedValue, 1);
                    if (published && Plain(value.EditedValue) != Plain(value.PublishedValue)) Value(name + " draft", value.EditedValue, 1);
                }
        }

        private static string Plain(object? value) => value is DateTime date ? date.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        private void Value(string name, object? value, int indent)
        {
            if (value is null) return;
            if (value is string stored) Stored(name, stored, indent);
            else Scalar(name, Plain(value), indent);
        }

        /// <summary>A stored string, which is a block value, other JSON, or text.</summary>
        private void Stored(string name, string stored, int indent)
        {
            var start = stored.AsSpan().TrimStart();
            if (start.StartsWith("{") || start.StartsWith("["))
            {
                JsonNode? node = null;
                try { node = JsonNode.Parse(stored); } catch (JsonException) { }
                if (node is not null) { Json(name, node, indent); return; }
            }
            Scalar(name, Labelled(stored), indent);
        }

        private void Json(string name, JsonNode node, int indent)
        {
            if (node is JsonObject value && value["contentData"] is JsonArray && value["layout"] is JsonObject)
            {
                Line(indent, name + " =");
                Blocks(value, indent + 1);
                return;
            }
            // On one line where it is short, such as a picker's list of keys, and otherwise indented, so that a change shows by line.
            var compact = Labelled(node.ToJsonString(Compact));
            Scalar(name, compact.Length <= 160 ? compact : Labelled(node.ToJsonString(Indented)), indent);
        }

        private void Node(string name, JsonNode? node, int indent)
        {
            switch (node)
            {
                case null: return;
                case JsonValue scalar when scalar.TryGetValue<string>(out var stored): Stored(name, stored, indent); return;
                case JsonValue scalar: Scalar(name, scalar.ToJsonString(), indent); return;
                default: Json(name, node, indent); return;
            }
        }

        private void Scalar(string name, string value, int indent)
        {
            value = value.Replace("\r\n", "\n");
            if (value.Length <= 160 && !value.Contains('\n')) { Line(indent, name + " = " + value); return; }
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
            Texts[hash] = value;
            Line(indent, $"{name} = text {hash} ({value.Split('\n').Length} lines)");
        }

        /// <summary>The blocks of a block value in the order of its layout, then any that the layout leaves out.</summary>
        private void Blocks(JsonObject value, int indent)
        {
            static Dictionary<string, JsonObject> ByKey(JsonNode? rows) => (rows as JsonArray ?? []).OfType<JsonObject>()
                .Where(row => (string?)row["key"] is not null).GroupBy(row => (string)row["key"]!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var content = ByKey(value["contentData"]);
            var settings = ByKey(value["settingsData"]);
            var exposed = (value["expose"] as JsonArray ?? []).Select(entry => (string?)entry?["contentKey"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Item(JsonNode? item, int depth)
            {
                var key = (string?)item?["contentKey"] ?? "";
                placed.Add(key);
                var block = content.GetValueOrDefault(key);
                var columns = item?["columnSpan"] is JsonValue span && span.TryGetValue<int>(out var count) && count != 12 ? $"; {count} columns" : "";
                Line(depth, (block is null ? "missing block" : TypeAlias(block)) + " " + key.ToLowerInvariant() + columns + (exposed.Contains(key) ? "" : "; not exposed"));
                if (block is not null) Values(block, "", depth + 1);
                if ((string?)item?["settingsKey"] is { } settingsKey && settings.GetValueOrDefault(settingsKey) is { } setting) Values(setting, "settings ", depth + 1);
                foreach (var area in item?["areas"] as JsonArray ?? [])
                {
                    if (area?["items"] is not JsonArray { Count: > 0 } items) continue;
                    var areaKey = (string?)area["key"] ?? "";
                    Line(depth + 1, "[" + (Guid.TryParse(areaKey, out var id) && Areas.TryGetValue(id, out var alias) ? alias : areaKey) + "]");
                    foreach (var child in items) Item(child, depth + 2);
                }
            }
            foreach (var (_, items) in (JsonObject)value["layout"]!)
                foreach (var item in items as JsonArray ?? []) Item(item, indent);
            foreach (var block in (value["contentData"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var key = (string?)block["key"] ?? "";
                if (placed.Contains(key)) continue;
                Line(indent, "not placed: " + TypeAlias(block) + " " + key.ToLowerInvariant());
                Values(block, "", indent + 1);
            }
        }

        private string TypeAlias(JsonObject block)
        {
            var type = (string?)block["contentTypeKey"] ?? "?";
            return Guid.TryParse(type, out var key) && Types.TryGetValue(key, out var alias) ? alias : type;
        }

        private void Values(JsonObject block, string prefix, int indent)
        {
            foreach (var entry in (block["values"] as JsonArray ?? []).OfType<JsonObject>().OrderBy(entry => (string?)entry["alias"], StringComparer.Ordinal))
            {
                var culture = (string?)entry["culture"];
                Node(prefix + ((string?)entry["alias"] ?? "?") + (culture is null ? "" : " (" + culture + ")"), entry["value"], indent);
            }
        }

        private string Labelled(string value) => Keys.Replace(value, match => Guid.TryParse(match.Value, out var key) && Labels.TryGetValue(key, out var label) ? label : match.Value);
    }
}

/// <summary>
/// Compares a <see cref="ContentListing"/> with the listing kept under source control, or writes that file. An application's test
/// calls it with the listing of a new installation, and again with the listing of a site updated from an earlier release, so that
/// both ways of reaching the current content are held to one file.
/// </summary>
public static class ContentCheck
{
    /// <summary>Set to <c>write</c>, this environment variable has <see cref="Matches(string, string, string)"/> write the file it would compare.</summary>
    public const string ListingVariable = "APPBLOCKS_CONTENT_LISTING";

    /// <summary>
    /// Throws when <paramref name="listing"/> differs from the file at <paramref name="listingPath"/>, with the lines that differ.
    /// <paramref name="description"/> opens the file and says what it lists and how to change it.
    /// </summary>
    /// <exception cref="ContentCheckException">The listing differs, or the file is missing.</exception>
    public static void Matches(string listing, string listingPath, string description)
        => Matches(listing, listingPath, description, string.Equals(Environment.GetEnvironmentVariable(ListingVariable), "write", StringComparison.OrdinalIgnoreCase));

    internal static void Matches(string listing, string listingPath, string description, bool write)
    {
        var document = description.Replace("\r\n", "\n").Trim() + "\n"
            + $"AppBlocks.Testing's ContentListing wrote this file, and the tests compare it with the site. To change the content, change the\n"
            + $"code that installs it, then write this file again by running the tests with {ListingVariable}=write.\n\n\n" + listing.Replace("\r\n", "\n");
        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(listingPath))!);
            System.IO.File.WriteAllText(listingPath, document, new UTF8Encoding(false));
            return;
        }
        if (!System.IO.File.Exists(listingPath)) throw new ContentCheckException($"{listingPath} is missing. Run the tests with {ListingVariable}=write to write it.");
        var recorded = System.IO.File.ReadAllText(listingPath).Replace("\r\n", "\n");
        if (recorded != document)
            throw new ContentCheckException($"The site's content is no longer what {listingPath} lists.\n" + ListingDifference.Of(recorded, document)
                + $"If the change is intended, run the tests with {ListingVariable}=write and review the file's diff.");
    }
}
