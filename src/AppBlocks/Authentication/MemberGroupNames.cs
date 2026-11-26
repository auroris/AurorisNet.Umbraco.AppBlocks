using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AppBlocks.Authentication;

/// <summary>
/// Resolves Umbraco Member Group picker values (comma-separated integer IDs, GUID keys, or names) into member group names.
/// </summary>
public sealed class MemberGroupNames(IReadOnlyList<IMemberGroup> all)
{
    /// <summary>Resolves <paramref name="picked"/> against the preloaded groups; returns null if any picked group is missing.</summary>
    public IReadOnlyList<string>? Resolve(string picked) => Resolve(all, picked, failOnMissing: true);

    /// <summary>
    /// Resolves <paramref name="picked"/> against <paramref name="all"/>. When <paramref name="failOnMissing"/> is true,
    /// returns null if any picked group no longer exists; otherwise omits missing groups.
    /// </summary>
    public static IReadOnlyList<string>? Resolve(IReadOnlyList<IMemberGroup> all, string? picked, bool failOnMissing = false)
    {
        if (string.IsNullOrWhiteSpace(picked)) return [];
        var names = new List<string>();
        foreach (var item in picked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var group = int.TryParse(item, out var id) ? all.FirstOrDefault(g => g.Id == id)
                : Guid.TryParse(item, out var key) ? all.FirstOrDefault(g => g.Key == key)
                : all.FirstOrDefault(g => string.Equals(g.Name, item, StringComparison.OrdinalIgnoreCase));
            if (group?.Name is not { } name)
            {
                if (failOnMissing) return null;
                continue;
            }
            names.Add(name);
        }
        return names;
    }

    /// <summary>Reads all member groups from <paramref name="groups"/> and resolves <paramref name="picked"/> into their names.</summary>
    public static async Task<IReadOnlyList<string>> ResolveAsync(IMemberGroupService groups, string? picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return [];
        var all = (await groups.GetAllAsync()).ToArray();
        return Resolve(all, picked, failOnMissing: false) ?? [];
    }
}
