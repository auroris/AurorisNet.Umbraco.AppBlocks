using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Dictionary;
using AppBlocks.Blocks;

namespace AppBlocks.Tests;

/// <summary>
/// The languages AppBlocks ships: French has every text the blocks show and no other, with the English's placeholders; a page
/// served in French, or in a regional French, gets it; a site's dictionary and its own sources still win; and another assembly
/// adds a language, or a regional variant, the same way.
/// </summary>
public sealed partial class LocalisationTests
{
    [GeneratedRegex(@"^ {4}(\w+): ""((?:[^""\\]|\\.)*)"",", RegexOptions.Multiline)]
    private static partial Regex RuntimeText();
    // Text["id", "English"] in the views and text["id", "English"] in a view component; the English may be a constant.
    [GeneratedRegex(@"\b[Tt]ext\[""(\w+)"",\s*(?:""((?:[^""\\]|\\.)*)"")?")]
    private static partial Regex ServerText();
    // The schedule's Format("id", "English", values).
    [GeneratedRegex(@"\bFormat\(""(\w+)"",\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex ServerFormat();
    [GeneratedRegex(@"^\| `(\w+)` \| (.*) \|\r?$", RegexOptions.Multiline)]
    private static partial Regex DocumentedText();
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    private static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));

    private static AppBlocksTranslations Shipped(params AppBlocksTranslationResources[] more)
        => new([new(typeof(AppBlocksText).Assembly), .. more]);

    private static readonly AppBlocksTranslationResources Companion = new(typeof(LocalisationTests).Assembly, "AppBlocks.Tests.Localisation.");

    /// <summary>The browser runtime's texts by id, with their English, as runtime-text.js declares them.</summary>
    private static Dictionary<string, string> RuntimeTexts()
        => RuntimeText().Matches(File.ReadAllText(Path.Combine(Repository(), "src/AppBlocks/wwwroot/App_Plugins/ApplicationBlocks/runtime-text.js")))
            .ToDictionary(match => match.Groups[1].Value, match => Regex.Unescape(match.Groups[2].Value), StringComparer.Ordinal);

    /// <summary>The server-rendered texts by id, with their English where the code writes it as a literal, from AppBlocks' views and code.</summary>
    private static Dictionary<string, string?> ServerTexts()
    {
        var source = Path.Combine(Repository(), "src/AppBlocks");
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cshtml", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !Path.GetRelativePath(source, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "obj" or "bin"));
        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            foreach (Match match in ServerText().Matches(code).Concat(ServerFormat().Matches(code)))
            {
                var english = match.Groups[2].Success ? match.Groups[2].Value.Replace("\\\"", "\"") : null;
                if (!texts.TryGetValue(match.Groups[1].Value, out var known) || known is null) texts[match.Groups[1].Value] = english;
            }
        }
        return texts;
    }

    /// <summary>Every id and its English as docs/localisation.md lists them, in its two tables.</summary>
    private static Dictionary<string, string> DocumentedTexts()
        => DocumentedText().Matches(File.ReadAllText(Path.Combine(Repository(), "docs/localisation.md")))
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Replace("\\|", "|"), StringComparer.Ordinal);

    private static string[] Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void The_code_declares_the_ids_and_English_the_documentation_lists()
    {
        var runtime = RuntimeTexts();
        var server = ServerTexts();
        Assert.True(runtime.Count > 90, "runtime-text.js was not read.");
        Assert.True(server.Count > 60, "The views were not read.");
        var documented = DocumentedTexts();
        var code = runtime.Keys.Union(server.Keys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(code.Except(documented.Keys).Order());
        Assert.Empty(documented.Keys.Except(code).Order());
        foreach (var (id, english) in runtime) Assert.True(documented[id] == english, $"{id}: the documentation says \"{documented[id]}\", runtime-text.js \"{english}\".");
        // An id the browser runtime and a view share, such as saved, takes the runtime's English in the documentation.
        foreach (var (id, english) in server.Where(text => text.Value is not null && !runtime.ContainsKey(text.Key)))
            Assert.True(documented[id] == english, $"{id}: the documentation says \"{documented[id]}\", the view \"{english}\".");
    }

    [Fact]
    public void French_translates_every_text_the_blocks_show_and_nothing_else()
    {
        var french = Shipped().File("fr");
        var ids = RuntimeTexts().Keys.Union(ServerTexts().Keys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[] { "fr" }, Shipped().Cultures);
        Assert.Empty(ids.Where(id => !french.ContainsKey(id)).Order());
        Assert.Empty(french.Keys.Where(id => !ids.Contains(id)).Order());
        Assert.All(french, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void Each_French_text_carries_the_placeholders_of_its_English()
    {
        var french = Shipped().File("fr");
        var english = DocumentedTexts();
        foreach (var (id, text) in RuntimeTexts()) english[id] = text;
        foreach (var (id, text) in french)
        {
            Assert.True(Placeholders(english[id]).SequenceEqual(Placeholders(text)),
                $"{id}: the English \"{english[id]}\" has {string.Join(", ", Placeholders(english[id]))}, the French \"{text}\" {string.Join(", ", Placeholders(text))}.");
            // Numbered placeholders are .NET composite formatting, which the French must survive.
            if (Placeholders(text) is { Length: > 0 } names && names.All(name => char.IsDigit(name[1])))
                _ = string.Format(CultureInfo.GetCultureInfo("fr-CA"), text, names.Select(_ => (object)"x").ToArray());
        }
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("fr-CA")]
    [InlineData("fr-FR")]
    public void A_page_served_in_French_or_a_regional_French_shows_the_French(string culture)
    {
        using var _ = new UiCulture(culture);
        var text = new AppBlocksText([Dictionary(), new ShippedTextSource(Shipped())]);
        Assert.Equal("Enregistré.", text["saved", "Saved."]);
        Assert.Equal("Ajouter une rangée", text["tabularAddRow", "Add row"]);
        Assert.Equal("T{0} {1}", text["scheduleQuarter", "Q{0} {1}"]);
        // An id AppBlocks does not know keeps the English the view gives it.
        Assert.Equal("Unknown", text["notAnAppBlocksText", "Unknown"]);
        var strings = text.Overrides();
        Assert.Equal(Shipped().File("fr").Count, strings.Count);
        Assert.Equal("Téléverseur de fichiers", strings["uploaderTitle"]);
        Assert.Equal("{count} fichiers sélectionnés", strings["uploaderFilesSelected"]);
    }

    [Theory]
    [InlineData("en-CA")]
    [InlineData("en")]
    [InlineData("de-DE")]
    public void A_page_in_a_language_nothing_ships_keeps_the_English_and_carries_no_strings(string culture)
    {
        using var _ = new UiCulture(culture);
        var text = new AppBlocksText([Dictionary(), new ShippedTextSource(Shipped())]);
        Assert.Equal("Saved.", text["saved", "Saved."]);
        Assert.Empty(text.Overrides());
    }

    [Fact]
    public void The_Umbraco_dictionary_and_a_sites_own_source_still_win_over_the_shipped_French()
    {
        using var _ = new UiCulture("fr-CA");
        // The shipped source is asked last wherever it was registered.
        var text = new AppBlocksText([new ShippedTextSource(Shipped()), Dictionary(new() { ["AppBlocks.saved"] = "C’est sauvegardé." }),
            new FixedTexts(new Dictionary<string, string> { ["saved"] = "Not asked", ["cancel"] = "Laisser tomber" })]);
        Assert.Equal("C’est sauvegardé.", text["saved", "Saved."]);
        Assert.Equal("Laisser tomber", text["cancel", "Cancel"]);
        Assert.Equal("Supprimer", text["delete", "Delete"]);
        var strings = text.Overrides();
        Assert.Equal("C’est sauvegardé.", strings["saved"]);
        Assert.Equal("Laisser tomber", strings["cancel"]);
        Assert.Equal("Enregistrement…", strings["saving"]);
    }

    [Fact]
    public void Another_assembly_adds_a_language_and_a_regional_variant_the_same_way()
    {
        var translations = Shipped(Companion);
        Assert.Equal(new[] { "es", "fr", "fr-CA" }, translations.Cultures);
        // fr-CA's own file first, then fr's for the ids it leaves out.
        Assert.Equal("Enregistré (Canada).", translations.For(CultureInfo.GetCultureInfo("fr-CA"))["saved"]);
        Assert.Equal("Bonjour du compagnon", translations.For(CultureInfo.GetCultureInfo("fr-CA"))["companionGreeting"]);
        Assert.Equal("Enregistrement…", translations.For(CultureInfo.GetCultureInfo("fr-CA"))["saving"]);
        // fr and fr-BE do not read fr-CA's file.
        Assert.Equal("Enregistré.", translations.For(CultureInfo.GetCultureInfo("fr-BE"))["saved"]);
        Assert.False(translations.For(CultureInfo.GetCultureInfo("fr")).ContainsKey("companionGreeting"));
        Assert.Equal("Guardado.", translations.For(CultureInfo.GetCultureInfo("es-MX"))["saved"]);
        Assert.Equal(2, translations.For(CultureInfo.GetCultureInfo("es-MX")).Count);
    }

    [Fact]
    public void AppBlocks_registers_its_French_after_the_dictionary_and_a_companion_adds_its_own_once()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        var assemblies = new DefaultUmbracoAssemblyProvider(typeof(LocalisationTests).Assembly, NullLoggerFactory.Instance);
        var loader = new TypeLoader(new TypeFinder(NullLogger<TypeFinder>.Instance, assemblies, null, null), NullLogger<TypeLoader>.Instance, [typeof(UmbracoBuilder).Assembly]);
        new UmbracoBuilder(services, configuration, loader).AddAppBlocks();
        services.AddAppBlocksTranslations(Companion.Assembly, Companion.Prefix);
        services.AddAppBlocksTranslations(Companion.Assembly, Companion.Prefix);
        services.AddAppBlocksTranslations(typeof(AppBlocksText).Assembly);
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(AppBlocksTranslationResources)));
        services.AddSingleton(TestStubs.Stub<ICultureDictionaryFactory>((_, _) => DictionaryOf(new() { ["AppBlocks.cancel"] = "Laisser tomber" })));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var _ = new UiCulture("fr-CA");
        var text = scope.ServiceProvider.GetRequiredService<AppBlocksText>();
        Assert.Equal("Enregistré (Canada).", text["saved", "Saved."]);
        Assert.Equal("Laisser tomber", text["cancel", "Cancel"]);
        Assert.Equal("Ouvrez une session pour voir l’horaire.", text["scheduleSignIn", "Sign in to see the schedule."]);
    }

    [Fact]
    public void The_page_carries_the_French_to_the_browser_runtime()
    {
        using var _ = new UiCulture("fr-CA");
        var assets = new AppPageAssets();
        assets.AddStylesheet("/App_Plugins/ApplicationBlocks/runtime.css");
        using var services = new ServiceCollection().AddSingleton(assets)
            .AddSingleton(new AppBlocksText([Dictionary(), new ShippedTextSource(Shipped())])).BuildServiceProvider();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        var output = new Microsoft.AspNetCore.Razor.TagHelpers.TagHelperOutput("body", [],
            (_, _) => Task.FromResult<Microsoft.AspNetCore.Razor.TagHelpers.TagHelperContent>(new Microsoft.AspNetCore.Razor.TagHelpers.DefaultTagHelperContent()));
        new AppPageAssetsTagHelperComponent(new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = context })
            .Process(new Microsoft.AspNetCore.Razor.TagHelpers.TagHelperContext("body", [], new Dictionary<object, object>(), "id"), output);
        var body = output.PostContent.GetContent();
        Assert.StartsWith("<script data-appblocks-strings=\"\" type=\"application/json\">", body);
        var json = body[(body.IndexOf('>') + 1)..body.IndexOf("</script>", StringComparison.Ordinal)];
        var strings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("Enregistré.", strings["saved"]);
        Assert.Equal("Déposez des fichiers ici ou {browse}", strings["uploaderDropOrBrowse"]);
        Assert.Subset(strings.Keys.ToHashSet(), RuntimeTexts().Keys.ToHashSet());
    }

    private static UmbracoDictionaryTextSource Dictionary(Dictionary<string, string>? items = null)
        => new(TestStubs.Stub<ICultureDictionaryFactory>((_, _) => DictionaryOf(items ?? [])));

    private static ICultureDictionary DictionaryOf(Dictionary<string, string> items)
        => TestStubs.Stub<ICultureDictionary>((method, _) => method.Name == "GetChildren" ? items : throw new NotSupportedException(method.Name));

    private sealed class FixedTexts(IReadOnlyDictionary<string, string> texts) : IAppBlocksTextSource
    {
        public string? Get(string id) => texts.GetValueOrDefault(id);
        public IReadOnlyDictionary<string, string> All() => texts;
    }

    /// <summary>Serves the test in a culture, as Umbraco serves a page in its culture, and restores the previous one.</summary>
    private sealed class UiCulture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
        public UiCulture(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
