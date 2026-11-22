using System.Globalization;
using AppBlocks.Authentication;
using AppBlocks.Blocks;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using AppBlocks.Forms;

namespace AppBlocks.AccountPickers;

public enum UmbracoAccountType { Members, BackofficeUsers, Both }

public sealed record UmbracoAccountPickerSettings : AccountPickerSettings
{
    public UmbracoAccountType AccountType { get; init; }
}

public sealed class UmbracoAccountPickerBlock : AccountPickerBlock
{
    public const string AliasName = "appUmbracoAccountPicker";
    public override string Alias => AliasName;

    public override UmbracoAccountPickerSettings ReadSettings(Func<string, string> value)
    {
        var identity = value("identityField");
        var name = value("nameField");
        if (identity.Length == 0 || identity == name)
            throw new AccountPickerException("Choose an account key field and a separate display name field if needed.");
        return new() {
            IdentityField = identity, NameField = name,
            Label = value("label"),
            AccountType = value("accountType") switch {
                "" or "Members" => UmbracoAccountType.Members,
                "Backoffice Users" => UmbracoAccountType.BackofficeUsers,
                "Both" => UmbracoAccountType.Both,
                _ => throw new AccountPickerException("Choose Members, Backoffice Users, or Both.")
            }
        };
    }
}

internal sealed class UmbracoAccountPicker(IMemberService members, IUserService users, AppBlocksText text) : IAccountPickerProvider
{
    private const int Limit = 20;
    private const string UserPrefix = ApplicationAccessPolicy.BackOfficeKeyPrefix;

    public async Task<AccountPickerSearch> SearchAsync(AccountPickerDefinition picker, string? query, CancellationToken ct)
    {
        query = query?.Trim();
        if (query is not { Length: >= 2 and <= 256 }) throw new AccountPickerException("Enter between 2 and 256 characters.");
        var type = ((UmbracoAccountPickerSettings)picker.Settings).AccountType;
        var items = new List<AccountPickerItem>();
        ct.ThrowIfCancellationRequested();
        if (type is UmbracoAccountType.Members or UmbracoAccountType.Both)
        {
            var page = await members.FilterAsync(new MemberFilter { Filter = query, IsApproved = true, IsLockedOut = false },
                orderBy: "username", take: Limit);
            items.AddRange(page.Items.Select(Member));
        }
        if (type is UmbracoAccountType.BackofficeUsers or UmbracoAccountType.Both)
        {
            ct.ThrowIfCancellationRequested();
            var page = users.GetAll(0, Limit, out _, "username", Direction.Ascending,
                userState: [UserState.Active, UserState.Inactive, UserState.Invited], userGroups: null, filter: query);
            items.AddRange(page.Where(user => user.IsApproved && !user.IsLockedOut).Select(Backoffice));
        }
        return new(items.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Detail, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Key, StringComparer.Ordinal).Take(Limit).ToArray());
    }

    public async Task<AccountPickerSelection?> ResolveAsync(AccountPickerDefinition picker, string key, FormExecutionContext? submission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var type = ((UmbracoAccountPickerSettings)picker.Settings).AccountType;
        if (type != UmbracoAccountType.BackofficeUsers && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id.ToString(CultureInfo.InvariantCulture) == key && members.GetById(id) is { IsApproved: true, IsLockedOut: false } member)
            return new(Member(member));
        if (type != UmbracoAccountType.Members && key.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(key.AsSpan(UserPrefix.Length), "D", out var guid) && key == UserPrefix + guid.ToString("D")
            && await users.GetAsync(guid) is { IsApproved: true, IsLockedOut: false } user)
            return new(Backoffice(user));
        return null;
    }

    private AccountPickerItem Member(IMember person) => new(person.Id.ToString(CultureInfo.InvariantCulture),
        person.Name ?? person.Username, $"{text["pickerMember", "Member"]} · {person.Username}");
    private AccountPickerItem Backoffice(IUser user) => new(UserPrefix + user.Key.ToString("D"),
        user.Name ?? user.Username, $"{text["pickerUser", "Backoffice User"]} · {user.Username}");
}
