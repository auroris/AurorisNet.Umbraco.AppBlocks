using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AppBlocks.Blocks;

/// <summary>
/// An assembly that ships translations of AppBlocks texts: one embedded JSON object of id → text per culture, named
/// <paramref name="Prefix"/> followed by the culture's name and <c>.json</c>, such as <c>AppBlocks.Localisation.fr.json</c> or
/// <c>AppBlocks.Localisation.fr-CA.json</c>. Register it with <see cref="AppBlocksTranslationsExtensions.AddAppBlocksTranslations"/>.
/// </summary>
public sealed record AppBlocksTranslationResources(Assembly Assembly, string Prefix = AppBlocksTranslations.DefaultPrefix);

/// <summary>
/// The translations AppBlocks, its companions and any other registered assembly ship, by culture. A culture's table holds its
/// own texts and then its parents' for the ids it leaves out, so <c>fr-CA</c> reads <c>fr-CA.json</c> and then <c>fr.json</c>.
/// Each id belongs to one package; when two registered assemblies translate the same id for the same culture, the first registered wins.
/// </summary>
public sealed class AppBlocksTranslations(IEnumerable<AppBlocksTranslationResources> resources)
{
    public const string DefaultPrefix = "AppBlocks.Localisation.";
    private const string Extension = ".json";

    private readonly AppBlocksTranslationResources[] resources = resources.Distinct().ToArray();
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> tables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The texts for <paramref name="culture"/> and its parents, most specific first; empty for a culture nothing translates.</summary>
    public IReadOnlyDictionary<string, string> For(CultureInfo culture) => tables.GetOrAdd(culture.Name, _ => Merge(culture));

    /// <summary>The names of the cultures the registered assemblies ship a file for, such as <c>fr</c>.</summary>
    public IReadOnlyList<string> Cultures => resources.SelectMany(Files).Select(file => file.Culture)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The texts one culture's file carries, without its parents'; empty when there is none.</summary>
    public IReadOnlyDictionary<string, string> File(string culture)
    {
        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in resources)
            foreach (var file in Files(resource).Where(file => string.Equals(file.Culture, culture, StringComparison.OrdinalIgnoreCase)))
                foreach (var (id, text) in Read(resource.Assembly, file.Name))
                    read.TryAdd(id, text);
        return read;
    }

    private IReadOnlyDictionary<string, string> Merge(CultureInfo culture)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
            foreach (var (id, text) in File(current.Name))
                merged.TryAdd(id, text);
        return merged;
    }

    private static IEnumerable<(string Culture, string Name)> Files(AppBlocksTranslationResources resource)
        => resource.Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resource.Prefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                && name.Length > resource.Prefix.Length + Extension.Length)
            .Select(name => (name[resource.Prefix.Length..^Extension.Length], name));

    private static Dictionary<string, string> Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The translation {name} is not embedded in {assembly.GetName().Name}.");
        try
        {
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            return texts.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(StringComparer.Ordinal);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"The translation {name} in {assembly.GetName().Name} is not a JSON object of id to text.", error);
        }
    }
}

/// <summary>
/// The shipped translations, for the current request's UI culture, which Umbraco sets to the culture it serves the page in.
/// <see cref="AppBlocksText"/> asks it after every other source, so a site's dictionary items and its own sources still win.
/// </summary>
public sealed class ShippedTextSource(AppBlocksTranslations translations) : IAppBlocksTextSource
{
    public string? Get(string id) => All().GetValueOrDefault(id);

    public IReadOnlyDictionary<string, string> All() => translations.For(CultureInfo.CurrentUICulture);
}

public static class AppBlocksTranslationsExtensions
{
    /// <summary>
    /// Adds the translations <paramref name="assembly"/> embeds, named <paramref name="prefix"/> + culture + <c>.json</c>, to
    /// those AppBlocks shows. AppBlocks registers its own; a companion or a site registers its assembly the same way. Safe to repeat.
    /// </summary>
    public static IServiceCollection AddAppBlocksTranslations(this IServiceCollection services, Assembly assembly, string prefix = AppBlocksTranslations.DefaultPrefix)
    {
        var resources = new AppBlocksTranslationResources(assembly, prefix);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(AppBlocksTranslationResources) && !descriptor.IsKeyedService && Equals(descriptor.ImplementationInstance, resources))) services.AddSingleton(resources);
        return services;
    }
}
