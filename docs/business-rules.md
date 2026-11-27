# Business rules for AppBlocks forms

Who may read and change the data behind a form, a report or a file, and where to state each rule: on the page, on a SQL data source, or in C#. The recipes are examples to adapt in your application.

**Settings → AppBlocks → Forms** offers a C# sample for each saved form, with its fields and example database reads and writes. AppBlocks never compiles or runs the sample. The sample stores its rows in a `SampleForm` table in SQLite, with dates and times as ISO strings, so replace the table with your application's storage. On a development site, the [Backing Classes](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/csharp-editor.md) wizard writes a form class or a backing class for a table with the checks of this guide in place.

Keep application data in the `AppBlocksDb` database, which is separate from Umbraco's. [Connections](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/connections.md) describes it and the upload store. The application owns its business tables and their schema changes.

## Where each rule belongs

Forms and reports are blocks on ordinary Umbraco pages, so start with Umbraco's own access rules. Protect the page with **Public Access**, by Member Group or by specific Members, and its blocks, their data and their files follow that page, including protection inherited from a parent page. Many sites need nothing more.

The other layers are there for rules a page cannot express. Requests pass through them in order, and each one can only narrow what the last allowed. Use the first layer that can state your rule, and remember that showing or hiding something in the browser is never one of them.

| Layer | What it decides | Set up in |
| --- | --- | --- |
| Sign-in | Who the caller is: an Umbraco Member or a backoffice User | Umbraco, including its external login providers |
| Public Access (the baseline) | Which Members may reach a page | The page's **Public Access** in the backoffice |
| `IProtectedPageAdmission` | Who the site admits to a page Umbraco's Public Access refuses, asked only when Umbraco refuses | C#, registered with dependency injection, such as a rule that admits a page's editors |
| Block Security and Read Only | Which blocks this visitor may see and which values they may edit | The block’s **Security** and **Read Only** condition lists |
| Read groups and Write groups | Which Member Groups may use a data source | The data source |
| Row restriction | Which rows a caller can see and write | The data source's **Row restriction** |
| Backing class (`IDataSourceHandler`) | What a data source writes and shows, rows it writes itself, refusals, and a form's rows saved with its record | C#, registered with dependency injection and picked on the data source's **Advanced** tab |
| `IDataSourcePolicy` | Any rule a data source's settings cannot state | C#, registered with dependency injection |
| `IFormResource` | Everything about a custom form's operations | C#, your resource |
| `IAttachmentOwner` | Who may change a record's files | C#, on your resource |
| `IAddOnlyAttachmentOwner` | Who may add files to a record without changing the ones there | C#, on your resource |
| `IReportAccessPolicy` | What may be done with reports and saved views | C#, or `AppBlocks:ReportAdministratorGroups` |

Two things apply to every layer.

**Umbraco's Public Access is the baseline.** A block under Public Access, the page's own or inherited from an ancestor, refuses whoever that rule refuses. The one exception is an `IProtectedPageAdmission` that the site registers. On a page without Public Access, an anonymous visitor reaches the block, and whatever is behind the block decides what they get. **A C# class with no check is public on an unprotected page.**

**AppBlocks recognizes Umbraco's Member and backoffice sign-ins only.** A caller authenticated another way, such as with a bearer token or an API key, counts as anonymous on an unprotected page and is refused under Public Access. To use another identity system, sign people in through it as Umbraco Members or Users.

| Block on an unprotected page | What an anonymous visitor gets |
| --- | --- |
| Form bound to your C# class | Whatever the class decides. A class returning `Denied()`, or one that sets `RequireMember`, shows them a sign-in notice |
| Form on the data source engine | Read-only access when its SQL data source is Public; otherwise a sign-in notice |
| Report | Public: they can view, query and export. Saved views are hidden. A report whose SQL uses `:USER_` bind variables shows a sign-in notice. `IReportAccessPolicy` and `IDataSourcePolicy` can still refuse |
| Attachments | They can read the files if the owning record loads for them. They can change the files only if the owner's class implements `IAttachmentOwner` and allows it |
| Tabular form | Read-only rows when the child data source is Public and the owning record loads for them |
| Schedule | Data when both sources are Public; otherwise a sign-in notice |
| Paper report | Requires a signed-in account at its download endpoint |

## Block Visibility and Security

Every block starts **Always visible** and **Always rendered**. Editable blocks and their containers also start **Never read only**. Open the block and add a condition to the corresponding list. For **Visibility** and **Security**, all conditions in a list must pass. For **Read Only**, **any** matching condition locks the block, so every Security or Read Only condition you add narrows access. Clearing a list restores its default.

| Setting | Unconditional choices | When a condition applies |
| --- | --- | --- |
| **Visibility** | Always visible (default), Never visible | When a condition fails, Razor renders the block with `display: none;`. Its fields still load, validate and submit (use **Field rules** or **Security** for conditionally required fields). |
| **Security** | Always rendered (default), Never rendered | When a condition fails, Razor omits the block and its children. Security also implies read only: the form API omits their bound values, skips field rules that depend on them, and rejects submissions that include those fields. |

For a public submission form with staff-only notes, put the notes and review controls in a **Fieldset**. On the fieldset, add a **Security** condition, choose **Member is in group**, and select **Staff** in the Umbraco Member Group picker. Staff can use the review fields under the form resource’s usual permissions. Other visitors can submit the public fields, and editing those fields preserves existing staff notes. For staff fields required only during review, use **Required on this form** and allow the resource to create a record before review.

An account picker checks each selection against its accounts. When Security omits the picker or one of the fields it writes, the other fields it writes become read only for that visitor and keep their stored values.

AppBlocks keeps responses that evaluate visitor or item-based Security or Read Only conditions out of shared caches. Site-owned plugin blocks can use the native condition lists by assigning **Application: Visibility conditions** to `blockVisibility`, **Application: Security conditions** to `blockSecurity`, and **Application: Read only conditions** to `blockReadOnly`. Custom editing controls and write endpoints must enforce Read Only when processing changes.

All three lists offer sign-in, anonymous, Member Group, backoffice User Group, and item comparisons. A Member Group condition passes for any of its selected groups. A backoffice User Group condition stores the selected group’s key and uses the site's website identity setting described below.

Use an item comparison for equality, inequality, ordering, an empty value or a populated value. Pick a page property under **Item** to compare its value. The stored property key follows changes to its name and alias. Inside a form, you can also choose **Form field** and enter a resource field key to compare the initialized or stored record for this visitor. On the form block itself, Visibility and Security read the page, and Read Only can also read the form record. For example, **Is at least** with item `amount` and value `100` shows a review section for records whose numeric amount is 100 or more. Use ISO dates and invariant numbers for comparison values. The template evaluates conditions when it renders. For a field that responds immediately as someone types, use the form's **Field rules**.

### Read Only

Open **Visibility, Security and Read Only** on a field, fieldset, form, layout, account picker, attachments block or tabular form. Choose **Always read only** to lock it for everyone, or add conditions that specify when to lock it. **Never read only** is the default and preserves the field, form and resource’s own write permissions. If any Read Only condition matches—or cannot be resolved—the block is locked, so you can combine conditions such as `status` Is `Closed` and **User is outside member groups** without weakening either rule.

For review notes that everyone can see but only staff can edit, add **User is outside member groups** to the field’s **Read Only** list and pick **Staff**. Visitors outside Staff can still submit the other fields. The server checks the condition again on every save and preserves the stored notes.

To close an entire form after review, set its **Read Only** condition to **Is**, enter the resource field key `status` under **Item**, and enter `Closed` under **Value** (use the form's **Read Only** list—not **Security** on **Form actions**—to enforce read-only at the API). Read Only on a form or its fields uses the initialized or stored record. A locked form can be opened and read. Create, update and delete are disabled, including changes to nested rows and files. A locked fieldset applies to its children. A locked account picker applies to all of its writable field bindings.

## The authenticated caller

In Umbraco, a **Member** signs in to the website and can belong to **Member Groups**. A **User** signs in to the backoffice and can belong to **User Groups**. A **backoffice administrator** is a User in the Administrators User Group.

Website requests carry only the Member identity by default. A standalone site that authorizes backoffice groups directly can enable `AppBlocks:WebsiteIdentity:IncludeBackOfficeUsers` to supply both accounts to its rules. The examples involving backoffice sessions below assume that option is enabled.

A C# `IFormResource` receives a `FormExecutionContext`. Despite its name, the `Member` property can contain an Umbraco Member's principal, a backoffice User's principal, both identities together or, on a page without Public Access, an unauthenticated principal for an anonymous visitor. It is never null. AppBlocks supplies the identities; your application decides which account types and groups may perform each operation. Never take the signed-in account's identity from submitted form fields.

Begin every operation with a sign-in check unless you mean that operation to be public, as generated samples do:

```csharp
using System.Security.Claims;
using AppBlocks.Forms;

var principal = context.Member;
if (principal.Identity?.IsAuthenticated != true)
    return FormResult.Denied();

var loginName = principal.Identity.Name;
var authenticationId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
```

A class meant for signed-in Members only can say so instead, and AppBlocks turns everyone else away before any operation runs. Set `RequireMember` to `true`: as a property of a class that implements `IFormResource`, or as an override on `FormResource<TCreate, TUpdate>`. The form endpoints then answer 401 to a caller with no account and 403 to a backoffice User with no Member session, without calling Initialize, Load, Create, Update or Delete. An anonymous visitor sees the form's sign-in notice in place of the form, and an Attachments or Tabular form block whose owner is the class shows them its own sign-in notice; so does a form whose Member session ends while the page is open. The class still decides which Members may do what.

```csharp
public sealed class LeaveRequests(/* ... */) : FormResource<LeaveInput, LeaveInput>
{
    public override string Alias => "leave-requests";
    public override bool RequireMember => true;
    // Every operation can now rely on context.Member holding a Member identity.
}
```

`RequireMember` is false unless a class sets it.

Member IDs and User IDs belong to different account stores. Use AppBlocks' resolver when you need a stable application key or current account information:

```csharp
using AppBlocks.DataSources;

// Inject IDataSourceUserResolver into your resource.
var account = await userResolver.ResolveAsync(context, cancellationToken);
if (account is null)
    return FormResult.Denied();

var ownerKey = account.Key;     // Member ID, or "backoffice:" followed by the User ID.
var name = account.Name;
var email = account.Email;
var groups = account.Groups;   // Member Group names, or backoffice User Group names.
var isMember = account.IsMember;
var isBackOfficeUser = account.IsBackOfficeUser;
var canEditPage = account.CanEditPage;
var canPublishPage = account.CanPublishPage;
var canDeletePage = account.CanDeletePage;
var isBackOfficeAdministrator = account.IsInUserGroup("admin"); // Umbraco's Administrators User Group alias.
```

A site using both account types can receive both sessions at once. In that case `account.IsMember && account.IsBackOfficeUser` is true. Each account has its own details; AppBlocks does not link them by matching names, emails or numeric IDs:

```csharp
var member = account.Member;                 // Null without a valid Member session.
var backOfficeUser = account.BackOfficeUser; // Null without a valid backoffice session.

var memberKey = member?.Key;
var memberEmail = member?.Email;
var memberGroups = member?.GroupNames;
var userKey = backOfficeUser?.Key;
var userEmail = backOfficeUser?.Email;
var userGroupNames = backOfficeUser?.GroupNames;
var userGroupAliases = backOfficeUser?.GroupAliases;
var canEdit = backOfficeUser?.CanEditPage == true; // Same answer as account.CanEditPage.
var canPublish = backOfficeUser?.CanPublishPage == true;
var canDelete = backOfficeUser?.CanDeletePage == true;
```

Both account objects also expose `Name` and `Sid`.

- The top-level `Key`, `Name`, `Email`, `Sid` and `Groups` describe the Member when both accounts are signed in, and otherwise the one signed-in account. `:USER_` bind variables, ownership and audit fields therefore stay on the Member while both sessions are active.
- `MemberGroupNames` always describes the Member. `UserGroupNames` and `UserGroupAliases` always describe the backoffice User. The two sets of groups are kept separate, even when names match.
- `Groups` holds display names. `UserGroupAliases` holds the current backoffice User Group aliases.
- `Sid` is the provider key of an external login. On some sites it is a Windows SID, and on others it is another provider's key.

The resolver reads each account's approval, lockout status and groups on every operation, so a change takes effect at once and old cookie claims are not relied on. A disabled or locked account contributes no identity, groups or permissions.

The resolver accepts approved, unlocked Members and backoffice Users, whatever their groups. `null` means there is no valid authenticated account. Resolving an account grants no business permission, and membership of the Administrators User Group grants no application access by itself.

`account.CanEditPage`, `account.CanPublishPage` and `account.CanDeletePage` check the signed-in backoffice User's permissions on the page that called your C# code, including when a Member is also signed in. Pass the whole `FormExecutionContext` to `ResolveAsync` as above: its `PageKey` identifies the hosting page, including for API requests. AppBlocks asks Umbraco for each permission independently, including group permissions and content start nodes:

| Property | Umbraco permission |
| --- | --- |
| `CanEditPage` | Update |
| `CanPublishPage` | Publish |
| `CanDeletePage` | Delete |

All three are false without a valid backoffice account, when no page is supplied (the `ResolveAsync(ClaimsPrincipal, ...)` overload), or when the page does not exist. Each permission is checked on its own, so permission to edit does not imply permission to publish or delete.

The same properties are on `account.BackOfficeUser`. In an `IDataSourcePolicy`, use `request.User.CanEditPage`, `request.User.CanPublishPage` or `request.User.CanDeletePage`, which AppBlocks has resolved for `request.Context.PageKey`. The results grant an application permission only when your rule uses them:

```csharp
var account = await userResolver.ResolveAsync(context, cancellationToken);
if (account is not { CanEditPage: true })
    return FormResult.Denied();
```

Umbraco's Public Access restrictions protect Member pages for everyone. Without a Member session, Umbraco refuses a protected page, a backoffice User included, unless the host registers an `IProtectedPageAdmission` that admits them; AppBlocks registers none, and the page and its block endpoints follow the same answer. When a Member session is present, its Public Access check alone decides admission to the page; a refused Member is never replaced by a backoffice session. Once admitted, both signed-in accounts are available to your business rules. On pages without Member Public Access restrictions, either account type can reach your resource, where your code must enforce the application's rules.

## Group membership

Choose the rule your application needs:

| Application rule | Identity check |
| --- | --- |
| Any Umbraco Member | `account.IsMember` |
| Any backoffice User | `account.IsBackOfficeUser` |
| Both accounts signed in | `account.IsMember && account.IsBackOfficeUser` |
| A Member with no backoffice account | `account.IsMember && !account.IsBackOfficeUser` |
| A backoffice User who can edit the calling page | `account.CanEditPage` |
| A backoffice User who can publish the calling page | `account.CanPublishPage` |
| A backoffice User who can delete the calling page | `account.CanDeletePage` |
| A specific Member Group | `account.IsInMemberGroup("Order editors")` |
| The Administrators User Group | `account.IsInUserGroup("admin")` |
| Another backoffice User Group | `account.IsInUserGroup("editor")` |

`IsInMemberGroup(string name)` checks the Member Group's **name**, for example `"Order editors"`. `IsInUserGroup(string alias)` checks the backoffice User Group's **alias**, for example `"admin"`, whose default display name is "Administrators". Both comparisons ignore case. Neither method accepts a numeric group ID or GUID, and neither treats the other account type as a match.

For a backoffice User Group, the alias is the second string shown with the lock control beside the editable name. Renaming the display name does not change the alias. An editable alias can be unlocked and changed deliberately; update any code or configuration that references the old alias when doing so. Backoffice group selections in AppBlocks fields store the group key.

Umbraco 18 Member Groups have a name and an entity key, but no separate alias field. This is why the Member Group method takes a name, while the User Group method takes an alias.

You can enumerate the signed-in account's current groups:

```csharp
foreach (var memberGroupName in account.MemberGroupNames)
{
    // A Member Group name, usable with account.IsInMemberGroup(memberGroupName).
}

foreach (var userGroupAlias in account.UserGroupAliases)
{
    // A backoffice User Group alias, usable with account.IsInUserGroup(userGroupAlias).
}

var userGroupDisplayNames = account.UserGroupNames;
```

`MemberGroupNames` is empty for a backoffice User. `UserGroupNames` and `UserGroupAliases` are empty for a Member. `Groups` remains the list of group display names for whichever account type was resolved. The name and alias lists are separate collections, not paired by position.

For example, this application chooses to allow Members in its Order editors Member Group:

```csharp
var canEdit = account.IsInMemberGroup("Order editors");
if (!canEdit)
    return FormResult.Denied();
```

Avoid granting a Member access by comparing their email address with a backoffice User's email. Sign-in, page access and permission to update a particular record are separate checks.

## Data source access

A SQL data source's **Access** setting, on its **Access** tab, says who may read through it. **Public** is the usual choice: **if you can see the page, you can read its data**. It follows the decision Umbraco made about the page, and the page and its data requests enforce the same Public Access rules.

A SQL data source created in the backoffice starts with **Public** selected. A source created in code or imported without a value has an empty Access, which is an error until a value is chosen:

| Access | Who may read |
| --- | --- |
| Public (where a new source starts) | Anyone admitted by the page, including visitors without an account: data access follows the page |
| Not public | Any signed-in Umbraco Member or backoffice User admitted by the page |
| Specific groups | Members in any selected Read group or Write group; at least one Read group is required |
| Empty or unknown | Configuration error: publishing is refused and the source cannot run |

### Read groups and Write groups

**Read groups** and **Write groups** select Umbraco Member Groups.

- Read groups apply with **Specific groups** only. Public and Not public ignore them, and selecting groups leaves Access as it is.
- Write groups limit writes in every mode. When empty, any signed-in reader may write. When set, only readers in a selected Write group may write.
- **Allow insert**, **Allow update**, **Allow delete** and every other check still apply.

Backoffice User Groups never satisfy a Member Group requirement, and administrators get no exception to Specific groups. When your rules involve backoffice Users, use Not public and enforce the rules in an `IDataSourcePolicy` with `request.User`. Each registered policy must allow the operation.

### Anonymous visitors

Page Public Access, row restrictions and every `IDataSourcePolicy` apply to public reads. A policy receives `DataSourceUser.Anonymous` for a visitor without an account.

- A query, row restriction or custom write statement that uses a `:USER_*` value requires sign-in, on a Public source too. An absent identity therefore cannot broaden a predicate such as `:USER_KEY IS NULL`.
- An authenticated account that fails resolution is refused. It never falls back to anonymous access.
- Anonymous row and attachment changes require **Allow anonymous writes** on the source, which is off by default. Publishing refuses the setting on a source that is not Public, that has Write groups, or whose insert defaults bind `:USER_*`.

### Access in C#

`DataSourceDefinition.Access` must be `DataSourceAccess.Public`, `NotPublic` or `SpecificGroups`. A null or unknown value fails validation before a connection is opened, and Read groups never stand in for a missing value.

Changing the access setting changes the definition fingerprint and the form contract version. Renaming the data source node changes neither, so forms already open on it stay current.

A custom `IFormResource` enforces its own rules on every operation.

## Row restriction and bind variables

**Row restriction** limits rows with no C#. It is added to every fetch and write, for example `OWNER_KEY = :USER_KEY` or `REGION IN (:USER_GROUPS)`, and a write that would leave the restriction is refused. It may use `:USER_KEY`, `:USER_SID`, `:USER_NAME`, `:USER_EMAIL`, `:USER_GROUPS`, `:NOW` and `:NOW_UTC`. `:USER_GROUPS` holds the account's group display names, so a Member and a backoffice User are matched against different lists.

### The clock

The clock has two bind variables, read once per operation so that they name the same moment. `:NOW` is the wall time of the data source's convention, the value the audit columns record. `:NOW_UTC` is that moment in UTC whatever the convention. It needs no zone conversion in SQL, so it stays correct in the autumn hour that repeats.

**Date-times** on the data source's **Definition** tab says what a date and time without a zone means in its database:

| Date-times | A zone-less date-time column holds | `:NOW` and the "at" audit columns write |
| --- | --- | --- |
| `ServerLocal` (the default, and the meaning of an empty value) | The web server's local time | The web server's local wall time |
| `Utc` | UTC | The UTC wall time |

A date-time field reads and writes its column in that convention, so a form shows the moment in the visitor's own time either way. A column that stores an offset, such as `datetimeoffset` or `timestamptz`, keeps it in both. Choose `Utc` for a database whose application stores UTC stamps, so the engine's date-time fields read and write them without a shift. Each dialect binds `:NOW_UTC` in the form UTC stamps compare with:

| Database | `:NOW_UTC` binds | Compare with |
|---|---|---|
| SQLite | ISO-8601 text with `Z`, such as `2026-11-01T07:30:00Z` | Text stamps in the same form, which sort in time order, or SQLite's date functions, which read the `Z` as UTC |
| SQL Server | A `datetime2` holding the UTC wall time, as `SYSUTCDATETIME()` returns | `datetime2` columns your SQL writes in UTC, as `SYSUTCDATETIME()` does; a `datetimeoffset` column compares as the same moment |
| PostgreSQL | A `timestamptz` | `timestamptz` columns |
| Oracle | A `DATE` holding the UTC wall time, as `SYS_EXTRACT_UTC(SYSTIMESTAMP)` returns | `DATE` or `TIMESTAMP` columns your SQL writes in UTC, as `SYS_EXTRACT_UTC(SYSTIMESTAMP)` does |

A posting window stored as UTC text in SQLite becomes a row source condition such as `WHERE POST_FROM <= :NOW_UTC AND (POST_UNTIL IS NULL OR POST_UNTIL > :NOW_UTC)`. Field defaults and insert defaults accept both names. As a default, `:NOW_UTC` fills a date or time field with the UTC date or time, a date-time field with the UTC moment written with `+00:00`, and a text field with the ISO form with `Z`. A date-time value carries its offset, so `:NOW` and `:NOW_UTC` give a date-time field the same moment, which its data source then stores in its own convention.

### Application bind variables

Application bind variables are values your own code computes for an operation, written `:APP.name`. Register an `IBindVariableProvider`. It receives the execution context, with the page key, the form key, the signed-in person and the request's parameters, and answers with named values. A name no provider answers binds NULL.

These values come from the application, so a row restriction may use them, as a row source and insert defaults may. A row restriction refuses `:PARAM.` and `:FIELD.` values, because the caller controls those.

A site that gives each tenant a copy of the same pages decides the tenant from the page in one place and writes one data source for every copy, with `WHERE TENANT_ID = :APP.tenant`. A field default of `:APP.tenant` hands the same value to a form class.

```csharp
using AppBlocks.DataSources;
using AppBlocks.Forms;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Extensions;

public sealed class TenantProvider(IPublishedContentCache pages, IDocumentNavigationQueryService navigation) : IBindVariableProvider
{
    public ValueTask<IReadOnlyDictionary<string, object?>?> ResolveAsync(FormExecutionContext context, CancellationToken ct)
    {
        // The tenant is a property of the nearest page at or above the calling page that has one, wherever that page sits in the tree.
        string? tenant = null;
        if (navigation.TryGetAncestorsOrSelfKeys(context.PageKey, out var keys))
            foreach (var key in keys)
                if (pages.GetById(key)?.Value<string>("tenantId") is { Length: > 0 } value) { tenant = value; break; }
        return ValueTask.FromResult<IReadOnlyDictionary<string, object?>?>(new Dictionary<string, object?> { ["tenant"] = tenant });
    }
}
```

Register it with `services.AddScoped<IBindVariableProvider, TenantProvider>()`. A property on the tenant's page type is the explicit way; matching the ancestor's content type alias instead works the same way. The providers are asked once per request for each signed-in person, page, form and set of request parameters, and every operation and every row of a save in that request binds the same answer. Field defaults are resolved once per page before any form, so there the context carries the page and an empty form key, and a block report's providers see its page and block. The [worked example](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/activity-schedule-example.md) builds a whole application on this rule.

A provider can also declare the names it answers, by implementing `Names`, which `IBindVariableProvider` otherwise answers with an empty list:

```csharp
public IReadOnlyCollection<string> Names => ["tenant"];
```

When every registered provider declares its names, publishing checks each `:APP.` name in a data source, an API endpoint's own SQL, a list of values' own SQL and a field default against them, and warns about one no provider declares, such as `:APP.tennant`. The publish goes ahead, because providers are code that may be deployed after the content that names their values, and the name binds NULL until a provider answers it. A provider that declares nothing may answer any name, so while one is registered nothing is checked. A data source's **Test** lists the declared names, so an editor can give them sample values such as `{"APP.tenant": "north"}`, and shows the same warning. Classes in the C# folder declare names the same way.

## Attachments

Whoever can read a record can read its files. Who may change them depends on what owns the record:

- For a C# resource, any signed-in caller who can read the record may change its files. Implement `IAttachmentOwner` on the resource when the rule is narrower. An anonymous visitor can change files only when `IAttachmentOwner` allows it.
- For a SQL data source, the engine limits changes to the source's write groups.

An anonymous visitor is told that a signed-in person filed a file, and is not told which account.

The attachment list shows each file with a download link and, when the caller may change it, **Rename** and **Remove**. A stored file is never replaced and AppBlocks keeps no revisions. Upload a new version of a document as a new file, and remove the old one if it should go.

### Uploading

**Upload** opens a dialog with the [Uppy](https://uppy.io/) file picker, which AppBlocks serves from the site. Uppy sends multipart requests with antiforgery protection to AppBlocks, and no external upload service is involved.

- Selecting or dropping files starts their upload at once.
- Optional purpose, label and notes sit above the picker. Their current values are copied onto each file when it is added, and a retry keeps them. They clear once the dialog is closed and its queue is empty.
- Files upload in the background while the page stays open, including after the dialog is closed with **Close** or Escape. The button that opened the dialog shows progress as a circle and a percentage, and clicking it reopens the same queue.
- A finished upload joins the attachment list. The button returns to **Upload** when every file has finished.
- While the list is read again after uploads, the dialog stays usable and more files can be added. **Rename** and **Remove** wait for the list.

A failed file stays in the queue with its error, and the button shows **Upload failed**. Reopen the dialog to **Retry failed uploads** or to remove the files. Adding another file starts that upload only. Failed uploads are never retried automatically, because repeating an upload after a lost response could store the file twice.

The dialog shows problems with uploading, including a list that could not be read after an upload. A refused rename or removal shows beside the list. A site translates the uploader's texts as [Localisation](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/localisation.md) describes.

### Renaming

**Rename** changes a file's display and download name. Its id, contents and other details are kept.

It uses `PATCH /api/attachments/{page}/{block}/{id}`, or `.../new/{submissionKey}/{id}` for a pending file, with the JSON body `{ "fileName": "invoice.pdf" }` and the list's antiforgery token. It requires permission to change the owner's files. The name is trimmed and must meet these rules:

- At most 260 characters.
- No path separators, control characters or filename punctuation such as `:`, `*` and `?`.
- A file type the block accepts.

### Saving a form while files are in progress

Saving a form while one of its Attachments blocks has queued, uploading or failed files shows a dialog and cancels that save. Finish the uploads or remove the outstanding files, then select **Save** again. Completing an upload never submits the form.

An upload, rename, removal, list refresh or adoption that is under way also holds back a save or a deletion, with a dialog that asks the user to wait. Queued files alone do not hold back a deletion, since they would belong to the record being deleted. Attachment controls start no new operation while the form is saving.

### Files for a record not saved yet

An Attachments block inside a form accepts files before the form's record exists, so a visitor can attach files while filling in a new record. The block offers this when all of these hold:

- The block is not read only, and the form is not read only.
- The form's resource offers this caller a new record: its `InitializeAsync` answers a record whose capabilities allow `Create`.
- The caller may write the record's files. `IAttachmentOwner` decides from that new record. Without it, the caller must be signed in.

The page marks the block `data-new-uploads="allowed"`, or `save-first` when it asks for a save first.

The files go to `/api/attachments/{page}/{block}/new/{submissionKey}`, with the key the runtime generates for the submission. That route lists, uploads, downloads, renames and removes them. Once the form has saved, the runtime posts `{ "key": "<record key>" }` to `.../new/{submissionKey}/adopt`. The files then belong to the record, judged by the record's own load for that caller.

Pending files are kept under the submission key and the uploader's account, so a signed-in caller sees and adopts only their own. An anonymous visitor's files are kept under the submission key alone, a random value that only their page holds.

Files that nobody adopts are deleted after `Attachments:PendingLifetimeHours`, 24 unless configured:

- An hourly Umbraco recurring job deletes expired pending uploads and their file data, with or without visits to the site. Deletion can come up to an hour after the lifetime expires.
- The job looks for the upload tables and creates nothing. It leaves a site alone when no upload has created the tables yet or the upload database cannot be reached.
- Pending-file requests run the same cleanup.
- Files that belong to records are kept.

### Files that become read-only at submission

For a form whose files become read-only when it is submitted, adopt the pending files inside the resource's create transaction:

1. Open the connection with the injected `AttachmentStore`'s `OpenConnectionAsync`. It opens the `Attachments:Connection` database with the attachment tables ensured. The form's tables must be in that database too.
2. Call `AttachmentStore.AdoptAsync(connection, transaction, pendingOwner, savedOwner, actor, ct)`. The pending owner is `AttachmentOwner.Pending(alias, submissionKey, context.Member)`, with the submission key from `context.SubmissionKey`. The saved owner is `new AttachmentOwner(alias, key)`. Get the owner identifier from `AttachmentOwner.ResourceId(resource)` for both calls. It returns a document UDI for a SQL data source and the registered alias for a C# resource.
3. Commit the record and the adoption together.
4. Deny later file writes through `IAttachmentOwner`.

Such a resource also implements `ISubmittedAttachmentOwner.ConfirmSubmissionAsync`. The browser's later adoption request calls it to confirm the saved key, the submission key and the caller against what the resource stored. A successful confirmation lists the files already adopted and moves no later pending uploads. The endpoint still checks page access, the saved record's load and antiforgery.

Store the submission key with the record, so that a retry returns the same record and confirmation still works after a restart.

### Adding files only

A resource can let a caller add files to a saved record while the files already there stay as filed. Implement `IAddOnlyAttachmentOwner.MayAddAttachmentsAsync` beside `IAttachmentOwner`. A requester who was asked for a missing document can then send it.

- AppBlocks asks it only about a caller the write rule refuses, since whoever may write may add. It is never asked for a block set to read only.
- The list answers `canAdd` beside `canWrite`, and the block shows such a caller **Upload** with no Rename or Remove.
- A rename or removal by that caller is refused with 403, for their own file too.
- Adopting the files a caller sent ahead of a new record's first save needs the same permission to add. A form can therefore let a requester attach and remove files while filling in a new record, through `IAttachmentOwner` on that new record, and only add files once it is saved.

## Report permissions

Interactive Reports exposes `IReportAccessPolicy` for report permissions. By default, any signed-in account that can reach a report's page can view, query and export it, and manage its own saved views.

A saved view belongs to one identity: the Member when a Member is signed in, otherwise the backoffice User. Someone signed in to both saves and owns views as the Member on the website and as the User on the backoffice routes.

Backoffice Users in the Administrators group (`admin`), or in an Interactive Reports Administrator group (`interactiveReportsAdministrator`) that the site creates, can manage public and private saved reports through **Settings → AppBlocks → Interactive Reports**. `AppBlocks:ReportAdministratorGroups` gives further User Group aliases the same access. Member Groups never grant report administration. To change the rule, supply your own policy.

## Common scenarios

These are starting points, and they combine. Check the rule on every operation it applies to, because a resource's `Load`, `Create`, `Update` and `Delete` are separate requests.

| Scenario | Approach |
| --- | --- |
| Open to anyone, with no account | Leave the page unprotected and write a class with its own checks. See Public contact form |
| Members only | Protect the page with Public Access, or check `account.IsMember` |
| Backoffice Users only | Leave the page unprotected and check `account.IsBackOfficeUser` |
| Editors of the calling page only | Resolve with `context` and check `account.CanEditPage` |
| Members and backoffice Users together | Leave the page unprotected and check each account type's groups |
| Certain Member Groups | Read groups and Write groups, or `account.IsInMemberGroup(...)` |
| Certain User Groups | `account.IsInUserGroup(...)`, in a resource or policy |
| Single sign-on | Umbraco external logins, then any rule here. See below |
| Roles kept in your own database | A role table keyed by `account.Key`. See below |
| Groups from another system | A resolver that adds them. See below |
| Roles assigned on each Member | A custom Member property. See User Group selections on a Member |
| Each person sees only their own rows | Row restriction, or filter in your resource |
| Rules that depend on the record | `IDataSourcePolicy` or your resource. See Ownership and record status |
| Several tenants in one site, each with its own pages and editors | The tenant from the page tree, a bind variable provider, a policy and a form class together. See the [worked example](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/activity-schedule-example.md) |

A person signed in as both a Member and a backoffice User can satisfy either account-type check or a rule requiring both. Use the account-specific properties when a rule depends on one identity's key, email or groups. Reports embedded in the backoffice itself continue to use only the backoffice account, including for saved-view ownership.

### Single sign-on

Configure the provider, such as Microsoft Entra ID, OpenID Connect or Windows authentication, as an Umbraco external login for Members or for Users. AppBlocks then sees the Umbraco account the login is linked to, and every rule in this guide applies to it. The provider's key for the person is available as `account.Sid` and `:USER_SID`.

AppBlocks reads groups from Umbraco, not from the provider. For provider groups to count, assign them to the account's Member Groups or User Groups when it signs in, or add them with a resolver as shown below. Other claims your sign-in keeps are on `context.Member`, but they are only as current as the cookie, so don't rely on them for a permission that must take effect immediately.

### Choosing an account key

Store something stable to identify an account in your own tables:

| Value | Use it for | Watch out for |
| --- | --- | --- |
| `account.Key` | Owners and roles within one site | It holds a Member ID, or `backoffice:` and a User ID, so it won't match across separate databases. Store it as text |
| `account.Sid` | People who sign in through a provider | Null without an external login. With several logins, it is the first one found |
| `account.Name` or `account.Email` | Display | Both can change, and may be reused by a later account, so don't use them to grant access |

### Roles in your own database

Your application can keep its own roles. Read them on each request so that a revoked role takes effect immediately:

```csharp
using System.Threading;
using System.Threading.Tasks;
using AppBlocks.Connections;
using AppBlocks.DataSources;

public sealed class ApplicationRoles(IApplicationConnectionFactory connections)
{
    public async Task<bool> HasRoleAsync(DataSourceUser account, string role, CancellationToken ct)
    {
        await using var connection = connections.CreateConnection("AppBlocksDb");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM APP_ROLE WHERE ACCOUNT_KEY = @key AND ROLE_NAME = @role";
        foreach (var (name, value) in new[] { ("key", account.Key), ("role", role) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return System.Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }
}
```

Register it with `builder.Services.AddScoped<ApplicationRoles>()`, then call it from a resource or policy after resolving the account. Create and maintain `APP_ROLE` as you would any other business table, and adjust the parameter syntax if your database needs another form.

### Adding groups from another system

To bring in groups from a directory or your own tables, wrap the resolver. Read groups, Write groups, `:USER_GROUPS`, `account.IsInMemberGroup(...)` and your own code then see the combined list:

```csharp
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AppBlocks.DataSources;

public interface IExternalGroups
{
    Task<IReadOnlyList<string>> GetAsync(DataSourceUser account, CancellationToken ct);
}

public sealed class ResolverWithExternalGroups(UmbracoDataSourceUsers umbraco, IExternalGroups external) : IDataSourceUserResolver
{
    public async Task<DataSourceUser?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct)
        => await AddGroupsAsync(await umbraco.ResolveAsync(principal, ct), ct);

    public async Task<DataSourceUser?> ResolveAsync(AppBlocks.Forms.FormExecutionContext context, CancellationToken ct)
        => await AddGroupsAsync(await umbraco.ResolveAsync(context, ct), ct);

    private async Task<DataSourceUser?> AddGroupsAsync(DataSourceUser? account, CancellationToken ct)
    {
        if (account is null) return null;
        return account with { Groups = [.. account.Groups, .. await external.GetAsync(account, ct)] };
    }
}
```

Register `builder.Services.AddScoped<UmbracoDataSourceUsers>()` and `builder.Services.AddScoped<IDataSourceUserResolver, ResolverWithExternalGroups>()`, with your `IExternalGroups` implementation. AppBlocks asks the resolver on every operation, so cache a slow lookup briefly; `UmbracoDataSourceUsers` itself reads each account once per request. Public Access still uses the account's real Member Groups, and `IsInUserGroup` still uses `UserGroupAliases`, which this example does not change.

Forward both resolver overloads as shown to preserve `CanEditPage`, `CanPublishPage` and `CanDeletePage` for the calling page. A resolver that implements only the principal overload resolves no page, so the three page permissions stay false.

## User Group selections on a Member

A Member Type can have a custom property that picks backoffice User Groups. This is a valid application-defined authorization model: your application can use those selections as roles, just as it can use Member Groups, actual User Group membership or another rule. Umbraco stores the selections as property data; your C# resource or policy gives them their application meaning.

Umbraco 18.3's built-in **User Group Picker** cannot be used for this directly. It isn't offered when creating a Data Type, and it fails to render a property that has no value yet, which leaves a new Member with a blank field and no way to choose. A small wrapper in your site solves both. Add these two files under `wwwroot/App_Plugins/ApplicationUserGroups/`:

```json
{
  "name": "Application User Group Picker",
  "version": "1.0.0",
  "extensions": [
    {
      "type": "propertyEditorUi",
      "alias": "Application.PropertyEditorUi.UserGroupPicker",
      "name": "Application User Group Picker",
      "element": "/App_Plugins/ApplicationUserGroups/user-group-picker.js",
      "meta": {
        "label": "Application User Group Picker",
        "icon": "icon-users",
        "group": "people",
        "propertyEditorSchemaAlias": "Umbraco.Plain.Json"
      }
    }
  ]
}
```

Save that as `umbraco-package.json`, and this as `user-group-picker.js`:

```javascript
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { html } from "@umbraco-cms/backoffice/external/lit";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";

export default class ApplicationUserGroupPicker extends UmbLitElement {
    static properties = { value: { attribute: false } };

    render() {
        return html`<umb-user-group-input
            .selection=${(this.value ?? []).map(item => item.unique)}
            @change=${event => {
                this.value = event.target.selection.map(unique => ({ unique }));
                this.dispatchEvent(new UmbChangeEvent());
            }}></umb-user-group-input>`;
    }
}
customElements.define("application-user-group-picker", ApplicationUserGroupPicker);
```

After restarting the site:

1. In **Settings → Data Types**, create a Data Type and choose **Application User Group Picker**.
2. In **Settings → Member Types**, add a property using it, with the alias `applicationUserGroups`.
3. Leave the property's member-editing and profile-display options off, so Members can't grant themselves roles.

Anyone who can edit Members can now assign these roles, so treat access to the **Members** section accordingly.

The picker stores [Umbraco's own value format](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Web.UI.Client/src/packages/user/user-group/property-editor/user-group-picker/property-editor-ui-user-group-picker.element.ts): a JSON array of references, each containing a `unique` value with the User Group's GUID key. These stored values are keys, not names or aliases:

```json
[
  { "unique": "765ef0cc-1932-4c10-9f10-25e7e138c406" }
]
```

The following application-owned C# class resolves the current Member from the authenticated principal, reads the property, and loads the referenced groups through `IUserGroupService`. It returns each group's key, current display name and current alias. Empty or malformed selections return no groups, so a bad value refuses access rather than granting it; deleted groups are omitted.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AppBlocks.DataSources;
using Umbraco.Cms.Core.Services;

public sealed record SelectedUserGroup(Guid Key, string Name, string Alias);

public sealed class MemberUserGroupSelections(
    IDataSourceUserResolver identities,
    IMemberService members,
    IUserGroupService userGroups)
{
    private const string PropertyAlias = "applicationUserGroups";
    private sealed record Reference([property: JsonPropertyName("unique")] Guid Key);

    public async Task<IReadOnlyList<SelectedUserGroup>> GetAsync(
        ClaimsPrincipal principal, CancellationToken ct)
    {
        var account = await identities.ResolveAsync(principal, ct);
        if (account is not { IsMember: true }) return [];

        var member = int.TryParse(account.Key, out var id) ? members.GetById(id)
            : Guid.TryParse(account.Key, out var key)
                ? (await members.GetByKeysAsync(key)).FirstOrDefault() : null;
        if (member is not { IsApproved: true, IsLockedOut: false }) return [];

        var json = member.GetValue<string>(PropertyAlias);
        if (string.IsNullOrWhiteSpace(json)) return [];

        Guid[] keys;
        try
        {
            var references = JsonSerializer.Deserialize<Reference?[]>(json) ?? [];
            if (references.Any(item => item is null || item.Key == Guid.Empty)) return [];
            keys = references.Select(item => item!.Key).Distinct().ToArray();
        }
        catch (JsonException) { return []; }
        if (keys.Length == 0) return [];

        ct.ThrowIfCancellationRequested();
        var groups = await userGroups.GetAsync(keys);
        return groups.Select(group => new SelectedUserGroup(
            group.Key, group.Name ?? group.Alias, group.Alias)).ToArray();
    }
}
```

Register this class with `builder.Services.AddScoped<MemberUserGroupSelections>()` in your application's startup or composer, then inject it into your resource. This example application accepts either a backoffice User in the `orderApprovers` User Group or a Member whose custom property selects that same group:

```csharp
var account = await userResolver.ResolveAsync(context, cancellationToken);
if (account is null) return FormResult.Denied();

var selectedGroups = await memberUserGroupSelections.GetAsync(context.Member, cancellationToken);
var canApprove = account.IsInUserGroup("orderApprovers")
    || selectedGroups.Any(group => group.Alias.Equals("orderApprovers", StringComparison.OrdinalIgnoreCase));
if (!canApprove) return FormResult.Denied();

// Perform this application's approval operation.
```

The same helper can be injected into an `IDataSourcePolicy` and called with `request.Context.Member`. Enforce the chosen rule on every relevant operation. A developer can also enumerate `selectedGroups` or compare `group.Key` when a particular group's identity matters independently of its editable alias, which is the safer choice for a rule that must survive an alias change.

These selections describe this application's roles; they do not alter Umbraco's native Member Groups, backoffice User membership or backoffice permissions. `account.IsInUserGroup(...)` continues to check actual User Group membership, while `MemberUserGroupSelections` reads the Member's application-defined selections. A group rename preserves the stored GUID reference; changing its alias requires updating application rules that compare that alias.

## Ownership and record status

Read the stored record before deciding whether it can change. Submitted owner and status values are not evidence of permission. An owner rule can look like this inside a resource's update operation:

```csharp
if (stored.OwnerKey != account.Key)
    return FormResult.Denied();

if (stored.Status != "Draft")
    return FormResult.Invalid("", "Submitted records cannot be edited.");
```

Read these values and perform the write in the same transaction, using the engine's concurrency checks or a checksum of the row's values compared inside that transaction. Apply ownership to reads and deletes too. Filter list queries at the database so records belonging to other accounts are never returned.

For the built-in data-source engine, implement `IDataSourcePolicy`. Every registered policy must allow the operation. Use `DataSourceKey` when a rule belongs to one published definition. Policies, handler operations and tabular details all expose this key.

```csharp
using AppBlocks.DataSources;

public sealed class DraftOrderPolicy : IDataSourcePolicy
{
    // Use the key of the Orders document installed by this application.
    private static readonly Guid Orders = new("59457055-42ca-4f92-9e1f-e18061332965");

    public ValueTask<bool> IsAllowedAsync(DataSourceRequest request, CancellationToken ct)
    {
        if (request.DataSourceKey != Orders) return ValueTask.FromResult(true);

        // This policy adds an edit rule. Configure read/query access separately.
        if (request.Action is not (DataSourceAction.Update or DataSourceAction.Delete))
            return ValueTask.FromResult(true);

        var current = request.Current;
        var allowed = current is not null
            && current.TryGetValue("ownerKey", out var owner)
            && string.Equals(Convert.ToString(owner), request.User.Key, StringComparison.Ordinal)
            && current.TryGetValue("status", out var status)
            && string.Equals(Convert.ToString(status), "Draft", StringComparison.Ordinal);
        return ValueTask.FromResult(allowed);
    }
}
```

Register it with `builder.Services.AddScoped<IDataSourcePolicy, DraftOrderPolicy>()` in your application's startup or composer. Write policies run inside the engine's transaction. A policy may query `request.Connection` with `request.Transaction`; it must not close, commit or roll back either one. Protect ownership fields from reassignment in your resource or a policy comparing `Current` with `Proposed`.

AppBlocks asks every registered policy about every data source on every page, which is why `DraftOrderPolicy` returns early for anything other than `orders`. A policy can state its scope instead, and AppBlocks then skips it for requests outside that scope. There are two ways:

- Register the class with a scope. `builder.Services.AddDataSourcePolicy<DraftOrderPolicy>(new DataSourcePolicyScope("orders"))` asks the policy only about data sources whose alias starts with `orders`, ignoring case. Add page keys, as in `new DataSourcePolicyScope("notices.", [noticesPage, archivePage])`, to ask it only about operations from those pages. The class is made per request scope through dependency injection, as with `AddScoped`.
- Implement `IScopedDataSourcePolicy`, which adds `bool Applies(DataSourceRequest request)` to `IDataSourcePolicy`, when the scope needs code. AppBlocks calls `Applies` first and calls `IsAllowedAsync` only when it answers true. Classes in the C# folder can implement it too.

A scope narrows where a rule holds, so choose it with the data source's other routes in mind. An [API endpoint](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/api-endpoints.md) call has no page, so a policy scoped to pages never governs it. Scope by alias alone when the rule must hold wherever the data source is used. A policy registered the usual way keeps deciding every request.

## How a SQL data source writes

**Key columns** identify the row a form opens and writes, and are required when the source allows writes. Each must be a column of the written table: the table the row source names, or the one table a SELECT's key columns come from. They are normally its primary key or a unique key. A view, or a key the database does not declare, publishes with a warning: a view takes writes only if it accepts them, through an INSTEAD OF trigger or as an updatable view, and a record whose key matches several rows cannot be opened or changed. A generated update or delete that would change more than one row is refused and rolled back.

**Audit columns** name, one per line, the columns that record who created or changed a row and when: `createdBy = CREATED_BY`, `createdAt = CREATED_AT`, `updatedBy = UPDATED_BY`, `updatedAt = UPDATED_AT`. A "by" column records the username unless its line names another of the caller's values after a second `=`: `updatedBy = UPDATED_BY = :USER_KEY`, or `:USER_SID`, `:USER_EMAIL` or `:USER_NAME`. The account key suits ownership, since names change and are reused. An "at" column records `:NOW`, in the source's **Date-times** convention. Forms cannot change any of them.

**Custom statements**, on the **Advanced** tab, replace the generated insert, update or delete with a section that starts with a line `-- insert`, `-- update` or `-- delete`, run whole with every bind variable a parameter. The engine still checks the checksum, the row restriction and the policies around them. A custom update runs only when a field changed, as the generated one does, so saving an unchanged form writes nothing and leaves the audit columns and the checksum as they were. A section may be a block or a procedure call:

- Publishing compiles each statement without running it. An Oracle block (`BEGIN` or `DECLARE`) or `CALL`, and a PostgreSQL `CALL` or `DO`, cannot be compiled without running, so publishing warns that it did not check it.
- On Oracle, a custom insert whose new key the form and the insert defaults do not supply gets `RETURNING ... INTO` appended, which only an `INSERT` takes. A block or call must therefore leave the key to the form or an insert default; otherwise the create is refused. On the other databases the statement returns the new key as its first value, with `RETURNING`, `OUTPUT INSERTED` or a final `SELECT`.
- A statement cannot use `SET NOEXEC` or `SET PARSEONLY`, which publishing sets on SQL Server to compile a statement without running it.

Every statement the engine runs has `AppBlocks:DataSources:CommandTimeoutSeconds`, 30 unless configured. A statement that runs out of time is rolled back and answered with 503, "The database did not answer in time. Try again in a moment."; a statement the database refuses is a 422 whose details go to the log.

## The backing class of a data source

A SQL data source writes its rows with statements it generates, or with the custom statements an editor writes. When a rule needs code, such as a status the server decides, a procedure that does the writing, or a log row beside every change, give the data source a **backing class**: a C# class implementing `IDataSourceHandler`, registered with dependency injection, and picked by its alias in **Backing class** on the data source's **Advanced** tab. The engine asks it at each step of an operation, after its own checks and before the policies, and the class can change the values, write the row itself in the engine's transaction, or refuse.

```csharp
builder.Services.AddScoped<IDataSourceHandler, OrderRules>();
```

The site checks the registered classes when it starts. Two classes with one alias, or an alias that is not one, stop the site from starting, with a message naming each.

Every hook passes through by default, so a class implements only the ones it needs:

| Hook | When the engine asks | What the class can do |
| --- | --- | --- |
| `InitializeAsync` | A new record's starting values, after the insert defaults | Change what the form starts with, or refuse |
| `LoadAsync` | A record read by key through the row restriction | Change what the form shows, or refuse |
| `CreateAsync` | A new row, once the submitted values are parsed and the defaults filled | Change what is written, write the row itself, or refuse |
| `UpdateAsync` | A changed row, locked, after any enabled checksum check | The same, with the stored row beside the row as it would be stored |
| `DeleteAsync` | A row about to be deleted, locked, after any enabled checksum check | Delete it its own way, such as marking it cancelled, or refuse |
| `ListAsync` | The rows a Tabular form block shows for one record, once read | Change what the rows show, or refuse |
| `QueryAsync` | A report, API endpoint, list of values, schedule or paper report about to read | Refuse |
| `ReadAsync` | An API endpoint, list of values, schedule or paper report reading, once access, `QueryAsync` and the policies allow it | Answer with rows of its own, keyed by the row source's column names, or return `null` to read the row source. The row restriction cannot filter rows from code, so the class applies its rule itself. See [API endpoints](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/api-endpoints.md#answering-from-code) |

Each hook receives one `DataSourceOperation`. `Action`, `User` (an anonymous visitor, a Member, a backoffice User or both, as policies see them), `Context` and `Key` say who asks for what. `Current` is the record as stored. `Values` holds what the engine is about to write or show, by field alias, in the form's JSON shape: text, numbers as text, true or false, ISO dates and times. Change, add or remove entries; a field removed from an update stays as stored. A value the class sets is converted and checked by its field's kind, length, range and choices, as a submitted value is. The class may set a read-only field of the written table, which a form never can, but not a key once the record exists, an audit column or a column the database computes. `Rows` holds a list's rows, and `Details` a form's rows saved with the record, described in the next section. `Connection` and `Transaction` are the engine's, and `Dialect` names the database; a class never opens, commits, rolls back or disposes either.

A hook answers with a `DataSourceOutcome`:

- `DataSourceOutcome.Continue`: the engine carries on with the values as the class left them.
- `DataSourceOutcome.Handled(key)`: the class wrote the row itself, so the engine writes nothing and only reads the row back. A create returns the new row's key, as a form shows it, unless the values already hold every key field. On a read hook it means the same as `Continue`.
- `DataSourceOutcome.Refuse(status, errors)`, or `DataSourceOutcome.Invalid(field, message)` for a 422: nothing is written, whatever the class wrote is rolled back, and the caller gets the status and the field errors as a `FormResult` would give them.

`operation.Sql` runs the class's own SQL: a statement or a block, whole, as one command, on the engine's connection and inside its transaction, with every value a bound parameter. It takes the bind variables of a custom statement: `:USER_*`, `:PARAM.name`, `:APP.name`, `:NOW`, `:NOW_UTC`, and `:FIELD.alias` from the operation's values as they stand, or the stored record on a delete. A value the class supplies itself is `:ARG.name`, passed as a dictionary or an object whose properties are the names; editor SQL cannot use `:ARG.`. `ExecuteAsync` returns the rows affected, `ScalarAsync` the first value, and `QueryAsync` the rows as dictionaries. An Oracle anonymous block runs as written. `:USER_*` values need a signed-in account, as everywhere, and only a create, update or delete may change data: `ExecuteAsync` refuses on a read. The list returned by a tabular save shares the save's transaction, so exceeding the block's row limit or refusing the list rolls back the save, including its master when they share a transaction.

This class refuses to change a shipped order and otherwise writes the change through the application's own procedure, as the signed-in person and with a reason of its own:

```csharp
using AppBlocks.DataSources;

public sealed class OrderRules : IDataSourceHandler
{
    public string Alias => "orders.rules";

    public async ValueTask<DataSourceOutcome> UpdateAsync(DataSourceOperation operation, CancellationToken ct)
    {
        if (Equals(operation.Current!["status"], "SHIPPED"))
            return DataSourceOutcome.Invalid("status", "A shipped order cannot change.");

        await operation.Sql.ExecuteAsync("""
            BEGIN
              orders_api.revise(p_order => :FIELD.orderId, p_customer => :FIELD.customer, p_by => :USER_KEY, p_reason => :ARG.reason);
            END;
            """, new { reason = "Changed on the website" }, ct);
        return DataSourceOutcome.Handled();
    }
}
```

Everything else still applies around the class. Before it is asked, the engine checks Access, the read and write groups and the allow flags, requires the source to opt in to anonymous writes, and checks the submitted values and, when the source opts in, the row checksum. After it answers, every `IDataSourcePolicy` runs and sees the values as the class left them, and a refusal there rolls back what the class wrote. The written row is read back through the row restriction, so a class cannot place a row outside what the caller may see. Publishing refuses a backing class that is not registered, and a data source whose class is no longer registered is unavailable, like one whose column is gone, rather than running without it. A form class registered in code still wins an alias over a data source.

Two limits follow from where the class sits. A value `LoadAsync` changes is what the form shows and sends back, so a form that saves it writes the changed value: change only fields the form cannot edit, or expect that. And `QueryAsync` can only refuse: a report runs its own SELECT in Interactive Reports, so its rows never pass through AppBlocks, and lists of values, schedules and paper reports get the same check so that every reader is judged alike. Put a rule about which rows they may read in the row restriction, which every reader applies.

## What a Tabular form block offers

A Tabular form block offers each operation its data source allows and the caller may perform. Four settings narrow that for one block, and leave the data source as it is for every other block, form and report that uses it:

| Setting | The block |
| --- | --- |
| **Without adding** | Offers no new rows |
| **Without editing** | Shows saved rows as text. A new row is still filled in before it is saved |
| **Without deleting** | Offers no deletions |
| **Read only** | Shows the rows and writes nothing |

**Without editing** on a block that adds rows keeps them as a log: the notes on a case, the steps of an approval. Each save appends, and what was saved before stays as it was written. Add **Without deleting** for a log nobody shortens.

The server enforces each setting. A change the block does not offer is refused with 403 and the batch it came in is not written, whether the rows were saved by the block or with the form's record. To hold a rule for every block and form, clear **Allow update** or **Allow delete** on the data source, or state it in an `IDataSourcePolicy`.

## A form and its rows in one save

A Tabular form block inside a form edits the rows of the form's record. When the caller can save that record, the block shows no save control of its own: its pending changes go with the form's save, in the same request. A record not saved yet can take rows too, and they are linked to its key once it has one. The block lists them from `/api/tabular/{page}/{block}/new`, below `AppBlocks:ApiRoot` when the site moves it. A block on its own, or inside a form the caller cannot save, has its own save control and saves its rows itself.

When the form's Form Interface selects a published SQL data source and every block's data source uses the same connection, the save is one transaction. The record is written first, so a new record's key becomes its new rows' link value, whatever a change says. Its backing class sees the record's values and every block's pending changes in `operation.Details`: each `DataSourceDetail` names the block, its data source, the link field and the record's key, which is still null while a new record's hook runs unless the class writes the record itself. The class may apply a change itself and remove it, replace it, add one, or refuse the whole save. The engine then applies what is left to each block's rows with the checks a block's own save makes: the allow flags, at most 200 changes (`AppBlocks:DataSources:MaxChanges`), the row checksums, the row restriction, and the block's data source's own backing class and policies. One commit keeps the record and all of its rows, or none of them, and a refused change is named by its block and its place in the batch.

When the form is a C# class, or a block's data source uses another connection, the record and the rows cannot share a transaction, so the save happens in steps. The record is saved first through its class, then each block's rows in a transaction of their own. A block whose rows fail leaves the record, and any block before it, saved: the answer carries the saved record with the refusal, the form shows it, and the next save is an update.

Each batch keeps its own submission key, so a request retried after a lost answer saves nothing twice, within the limits [create receipts](#concurrency-and-duplicate-submissions) have. A developer sending the request without the runtime adds `details`, a list of `{ block, revision, submissionKey, changes }`, to the form's submission, where `revision` is the one the block's rows were listed under and `changes` takes the shape a block's own save does.

## Public contact form

A contact form needs no account, so it needs a page with no Public Access, on the page or any ancestor, and a class that makes its own checks. Nothing else is switched on. This class only creates: there is no record to load, and no key on the page's address. The antiforgery token works for a visitor with no identity, including on their first request, and validation messages show as usual.

Everything a developer would check for a signed-in person is still the class's job. The Anti-spam block can take the spam checks off its hands with no code, as described under Anti-spam. This recipe is for a class that checks for itself, with a honeypot and a captcha token. Neither is a field of the form, so they arrive in `FormExecutionContext.FormData`, a read-only dictionary of the form's other named controls. `FormData` is apart from the submitted values, so field validation and the strict binding of `ContactMessage` never see it.

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AppBlocks.Forms;

public sealed record ContactMessage(
    [property: Required, StringLength(100)] string Name,
    [property: Required, EmailAddress, StringLength(200)] string Email,
    [property: Required, StringLength(4000)] string Message);

public interface IContactInbox
{
    Task SaveAsync(ContactMessage message, string? submissionKey, CancellationToken ct);
}

/// <summary>Create-only and public: every other operation is refused, and creating needs a verified captcha token.</summary>
public sealed class ContactFormResource(ContactCaptcha captcha, IContactInbox inbox) : FormResource<ContactMessage, ContactMessage>
{
    public override string Alias => "site.contact";

    public override IReadOnlyList<FormField> Fields { get; } = [
        new("name", "Your name", "text", Required: true, MaxLength: 100),
        new("email", "Your email", "text", Required: true, MaxLength: 200),
        new("message", "Message", "text", Required: true, MaxLength: 4000),
    ];

    /// <summary>An empty form, ready for the next message. With no key, the page's address stays as it is.</summary>
    private static FormRecord Blank() => new(null, null, new Dictionary<string, object?>(), new FormCapabilities(Create: true));

    public override Task<FormResult> InitializeAsync(FormExecutionContext context, CancellationToken ct) => Task.FromResult(FormResult.Ok(Blank()));

    public override Task<FormResult> LoadAsync(string key, FormExecutionContext context, CancellationToken ct) => Task.FromResult(FormResult.Denied());

    public override Task<FormResult> DeleteAsync(string key, string? checksum, FormExecutionContext context, CancellationToken ct) => Task.FromResult(FormResult.Denied());

    protected override Task<FormResult> UpdateAsync(string key, ContactMessage input, string? checksum, FormExecutionContext context, CancellationToken ct) => Task.FromResult(FormResult.Denied());

    protected override async Task<FormResult> CreateAsync(ContactMessage input, FormExecutionContext context, CancellationToken ct)
    {
        // A person never fills in the field they cannot see. Look successful, so a robot learns nothing.
        if (!string.IsNullOrEmpty(context.FormData.GetValueOrDefault("website"))) return FormResult.Ok(Blank());

        if (!await captcha.VerifyAsync(context.FormData.GetValueOrDefault("g-recaptcha-response"), ct))
            return FormResult.Invalid("", "Please confirm you are not a robot.");

        await inbox.SaveAsync(input, context.SubmissionKey, ct);
        return FormResult.Ok(Blank());
    }
}

/// <summary>Asks the captcha service about a token. The visitor's address comes from the request, not from the form.</summary>
public sealed class ContactCaptcha(IHttpClientFactory clients, IHttpContextAccessor http, IConfiguration configuration)
{
    public async Task<bool> VerifyAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var fields = new Dictionary<string, string> { ["secret"] = configuration["Captcha:Secret"] ?? "", ["response"] = token };
        if (http.HttpContext?.Connection.RemoteIpAddress is { } address) fields["remoteip"] = address.ToString();
        try
        {
            using var response = await clients.CreateClient().PostAsync("https://www.google.com/recaptcha/api/siteverify", new FormUrlEncodedContent(fields), ct);
            if (!response.IsSuccessStatusCode) return false;
            using var answer = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return answer.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
        }
        // No answer is no proof. Refuse, and let the visitor try again.
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return false; }
    }
}
```

Register them, and add `IHttpContextAccessor` if your site does not have it already. Behind a proxy, `RemoteIpAddress` is the proxy's address unless you configure ASP.NET Core's forwarded headers. `IContactInbox` is your own storage or mail:

```csharp
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ContactCaptcha>();
builder.Services.AddScoped<IContactInbox, MyContactInbox>();
builder.Services.AddScoped<IFormResource, ContactFormResource>();
```

The honeypot is an ordinary named control inside the form, hidden from people. A captcha widget adds its own hidden control there in the same way, under a name such as `g-recaptcha-response`:

```html
<input type="text" name="website" tabindex="-1" autocomplete="off" aria-hidden="true" style="position:absolute;left:-9999px">
```

The browser runtime sends what the browser itself would submit from the form's named `input`, `select` and `textarea` controls, apart from the fields: it leaves out bound fields, disabled controls, password and file inputs, button-type inputs (`submit`, `button`, `reset`, `image`), unchecked checkboxes and radio buttons, and the antiforgery field, which travels in a header. A multiple select sends each selected value under its name. On the server, `FormData` is bounded the way `Parameters` is: at most 50 names of letters, digits, `_`, `-`, `.` and `:`, up to 64 characters, each with one value of at most 16,384 characters, which leaves room for a real captcha token. A repeated name is left out rather than guessed. AppBlocks stores nothing from it and validates nothing in it, and like any other browser input it can say anything, so verify what matters.

Your class runs twice for each anonymous page view, once when the page renders and once when the runtime loads the form, because the page shows a class's refusal as a sign-in notice in place of the form. Keep `InitializeAsync` and `LoadAsync` cheap and free of side effects.

AppBlocks does not limit how often anyone calls a public form or report. Add rate limiting in your application if the page will be exposed to the internet. Limiting how often, under Anti-spam, has a recipe for ASP.NET Core's rate limiter on the form endpoints.

A select field using a published list of values reads it through the data source engine. Select Public on its SQL source to follow the form's page access, or impose additional access restrictions. A list with SQL of its own has an **Access** of its own: **Public**, the default and the meaning of an empty value, or **Not public**, which also requires a signed-in Member or backoffice User. A list whose readers need groups selects a data source with Specific groups instead. The list's own SQL reads the AppBlocks database (`AppBlocksDb`) when its connection is empty, as a data source does. You can also give the field its choices in your class's `FormField.Choices`.

## Anti-spam

Anonymous visitors can use forms on open pages, and so can bots. The **Anti-spam** block protects a form with no code. An editor places it inside the form, in any area of it, and chooses a service. AppBlocks renders the service's widget and script, then checks every create and update on the server before your class is called. A class gets this protection without writing any spam code.

| Service | What the editor sets | What a visitor sees |
| --- | --- | --- |
| Honeypot | Nothing | Nothing. It is an empty control kept out of sight, needing no account and loading nothing. |
| Cloudflare Turnstile | Site key, theme, size | A small widget, which usually completes by itself. |
| Google reCAPTCHA v2 | Site key, theme, size | The "I'm not a robot" checkbox, sometimes with a picture puzzle. |
| Google reCAPTCHA v3 | Site key, score threshold | Nothing in the form, only Google's badge. The score threshold defaults to 0.5, and a lower score is refused. |
| hCaptcha | Site key, theme, size | A checkbox, sometimes with a picture puzzle. |

Use one block per form. Publishing refuses a second one, a block outside a form, an unknown service, a score outside 0 to 1, and a site key that is really one of the site's secret keys. A page using both reCAPTCHA versions needs them in separate pages, because Google's script loads for one of them at a time.

### Keys

A site key is public, so it can go on the block. Leave it empty there to use a site-wide one. A secret key stays on the server and never appears in block content, exports, API responses or logs. Set each in configuration or in **Settings → AppBlocks → Anti-spam**:

```json
{
  "AppBlocks": {
    "AntiSpam": {
      "turnstile": { "SiteKey": "public-site-key", "SecretKey": "secret-key" }
    }
  }
}
```

The sections are `turnstile`, `recaptchaV2`, `recaptchaV3` and `hcaptcha`. Configuration wins, and the screen cannot change what it sets. A secret key saved on the screen is encrypted with this installation's Data Protection keys, as a connection string is, in `App_Data/AppBlocks/anti-spam.json`. If those keys change, the screen says the secret can no longer be read, and you save it again. The screen shows whether a secret is set, never its value.

### What happens to a submission

The server checks the antiforgery token, then the fields, then the Anti-spam block, then runs your class. Fields come first so that a typo does not spend a token, which works only once. The runtime gets a fresh token after each attempt.

- A failed check, a missing token and a filled-in honeypot all return the same 422 and the same message. The class is not called, and nothing tells a bot which it was.
- A request cannot remove the block, because the server reads it from the published page.
- A token the service says was solved on another host than the one the form was sent to fails the check. Set `AppBlocks:AntiSpam:VerifyHostname` to `false` behind a proxy that hides the public host from the site, or while using a service's test keys.
- If the service cannot be reached within 5 seconds, or the secret or site key is missing, or the service rejects the site's keys, the submission is refused with a friendly message to try later. The problem is logged once, shown on the settings screen and cleared when the service next answers. Publishing also warns when a secret is missing.
- The token is not stored, and your class does not receive it. Your class still gets the form's other controls in `FormData`, and a form without the block still gets everything, so a class can keep checking a token itself as in Public contact form.
- Deleting is not checked, because it only acts on a record your class allows.
- AppBlocks does not send the visitor's address to the service.

Signed-in Members and backoffice Users go through the same check, because an account can be stolen or scripted too. The one exception is an editor in Umbraco's preview. Preview shows a note in place of the widget, including anything that would turn visitors away, loads no third-party script and skips the check for that editor. The preview cookie alone earns nothing: it has to hold a token Umbraco issued to a backoffice User.

### Third-party scripts

Turnstile, reCAPTCHA and hCaptcha load third-party scripts in the visitor's browser and may need cookie consent on some sites. Consent handling stays with the site, and until the script loads the visitor is told the check could not load and cannot submit. The honeypot loads nothing from anywhere else.

A site with its own Content-Security-Policy has to allow the scripts and frames. If it sets a nonce in `HttpContext.Items` under `CspNonce`, the block's script tag carries it. Each service's own documentation has the current list, which is at least:

| Service | `script-src` and `frame-src` |
| --- | --- |
| Turnstile | `https://challenges.cloudflare.com` |
| reCAPTCHA | `https://www.google.com` and, for `script-src`, `https://www.gstatic.com` |
| hCaptcha | `https://hcaptcha.com` and `https://*.hcaptcha.com`, also for `style-src` and `connect-src` |

### Adding another service

A service is a small class implementing `IAntiSpamService`, registered with `builder.Services.AddSingleton<IAntiSpamService, MyService>()`. It says which script to load, which form control holds the token and how to verify a token. The block's picker then offers it, and its keys go under its alias like the others. Derive from `SiteVerifyService` if the service takes a form post and answers with `success` and `error-codes`. Post only to the service's own fixed address, never one that content supplies.

### Limiting how often

The Anti-spam block is not a rate limit. It decides whether one submission looks like a person, and anyone can still call a public form as often as they like. Each call with a token also costs your server a request to the service. ASP.NET Core's rate limiter covers this in the site's `Program.cs`. This allows 10 submissions a minute from each address, and leaves loading a form alone:

```csharp
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        // The form endpoints, below "/api" unless AppBlocks:ApiRoot moves them.
        http.Request.Path.StartsWithSegments("/api/forms") && !HttpMethods.IsGet(http.Request.Method)
            ? RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("not limited"));
});

var app = builder.Build();

// Before app.UseUmbraco(), so it runs ahead of the form endpoints.
app.UseRateLimiter();

app.Run();
```

Behind a proxy, use ASP.NET Core's forwarded headers first, or every visitor counts as the proxy's address.

A form submission is also limited to 1 MB, which leaves room for long text fields because files go through Attachments. A larger one gets a 413 before it is parsed. Change it with `AppBlocks:Forms:MaxBodySize`, in bytes.

## Validation and trusted defaults

Use field settings for required values, lengths and numeric limits. A SQL data source makes a field required when its column is NOT NULL and nothing else fills it: no database default, no identity or computed value, and no insert default. A NOT NULL column with a default is optional on the form. When a new record leaves it blank, the insert default or the database's default fills it. An update has no default to fall back on, so a blank for that column is refused with "A value is required." against the field. The mapping's `"required": true` or `"required": false` replaces the column's own rule.

A form may edit only some of a record's fields, such as a status or a note on existing records. When a required field without a server default is missing from the form, read-only in it or shown as display text, publishing warns that the form cannot create records and publishes it: updates merge the fields the form leaves out, and a create through it is refused at run time for the missing values. Mark the Form Interface **Read only** for a form that changes nothing.

### Conditional field rules

On the **Form** block, open **Field rules** and add a rule. Choose its target field and an action. **Show**, **Hide**, **Require** and **Make read only** apply when the conditions match. **Validate** runs for a filled-in field and displays your **Validation message** when the conditions fail. Choose **All** or **Any** and add comparisons. Each comparison names a field and either a fixed value or a second field on the form.

- To ask for a cancellation reason, add a **Show** rule and a **Require** rule on **Reason**, both with the condition **Status Is Cancelled**.
- To check dates, add a **Validate** rule on **End date**. Choose **End date**, **Is at least**, and **Start date** as the comparison field. Enter “End date must be on or after start date.”

The browser updates the fields as the visitor types. The server evaluates the published rules again before saving, including stored values on an update. Hiding or locking a field preserves its saved value. Resource and database requirements still apply.

### Adding a rule in C#

Register a validation in the site’s Umbraco composer to add a server-side rule to forms using one resource. Return a field error to stop the save, or `null` to continue. The configured rules and the normal data source save still run on accepted input.

```csharp
using AppBlocks.Forms;

// In Compose(IUmbracoBuilder builder)
builder.Services.AddFormValidation("orders.lines", (values, context) =>
    values.TryGetProperty("item", out var item) && item.GetString() == "Discontinued"
        ? FormResult.Invalid("item", "Choose a stocked item.")
        : null);
```

A rule needing asynchronous services can implement `IFormSubmissionProcessor` and register it with scoped dependency injection. A database rule that must run in the write transaction belongs in the data source’s backing class. For C# detail resources, `context.ParentScope` supplies the verified link field and parent key. Include them in the update and delete predicates alongside the record key. SQL data sources do this automatically.

Set audit values on the server after resolving the signed-in account:

```csharp
var createdBy = account.Key;
var createdAt = DateTimeOffset.UtcNow;
```

A hidden field, a disabled control, a query parameter (`FormExecutionContext.Parameters`, `:PARAM.name`) or another named control (`FormExecutionContext.FormData`) is still browser input. Do not use it as the authority for an owner, approval, price, permission or audit value. Bind SQL parameters for submitted values; never concatenate them into SQL.

## Concurrency and duplicate submissions

Last save wins by default. A SQL data source's **Concurrency** choices map to the `DataSourceConcurrency` enum: `LastSaveWins` and `Checksum`. Select `Checksum` to detect conflicting edits without adding a table column. These modes apply to individual records, tabular batches and master/detail saves.

### Checksums in a C# resource

Return a `FormRecord` whose `Checksum` is null for last-save-wins. Returning a checksum of the row's values opts into conflict detection. The resource must then compare the caller's checksum with the current row and perform the update or delete in the same transaction, and a missing checksum must not pass that check. An update must never recreate a deleted record.

- A form class the Backing Classes wizard writes uses last-save-wins. Set its `UseChecksum` field to `true` to opt in.
- The downloadable sample applies the same protection to its SQLite table. `ReadAsync` returns a checksum of the row's values. `SaveAsync` compares it with the current row inside a write transaction and returns `false` for a stale change.

### Create receipts

Every create through a SQL data source carries a submission key that the runtime generates. The engine remembers each successful create by that key for `AppBlocks:DataSources:ReceiptLifetimeMinutes`, 15 unless configured. A request retried within that time, after a lost answer, returns the record the first request created. Two requests with one key wait for each other.

The default store keeps receipts in the web server's memory, up to `AppBlocks:DataSources:ReceiptCapacity` (10,000). That covers a single web server. On a load-balanced site, a retry that reaches another server finds no receipt and inserts again, and so does any retry after a restart.

A load-balanced site registers a store that its servers share, with `AddSingleton<IDataSourceReceiptStore, YourStore>()`. `EnterAsync` holds the submission keys across servers, and `FindAsync` and `AddAsync` read and write receipts. Keep each receipt at least the configured lifetime.

A C# resource should make its create safe to repeat when a retry could create a duplicate. `FormExecutionContext.SubmissionKey` identifies the submission. Store the receipt and the new record together.

## Wiring a custom resource

Implement `IFormResource`, or derive from `FormResource<TCreate, TUpdate>` for typed input and data-annotation validation. Register it as an `IFormResource`, select its alias in the form's Form Interface and bind the field blocks to its fields. The downloadable sample is a persistence example to call from those operations. Register a form resource of your own to use it.

| Return | For |
| --- | --- |
| `FormResult.Ok(record)` | Success |
| `FormResult.Denied()` | A refused operation |
| `FormResult.Missing()` | An absent record |
| `FormResult.Conflict()` | A stale update |
| `FormResult.Invalid(field, message)` | A validation error |
| `new FormResult(204)` | Success with no record, as a delete answers |

A success without a record means "done, go back". The runtime takes the visitor to the **Return page** of the form's Form Interface, or to the site's root when it has none. The endpoint sends a successful delete that carries no record as 204 No Content. A create or update that answers success without a record sends the visitor back the same way.

Set the returned `FormCapabilities` to the actions the caller may see, and enforce the same rules again when an action is submitted.

## An anonymous SQL contact form

For a contact form backed by SQL:

1. Set the source's **Access** to **Public**.
2. Turn on **Allow anonymous writes** and **Allow insert**.
3. Leave **Write groups** empty, and **Allow update** and **Allow delete** off.
4. Put the form on a page without Public Access.
5. Keep `:USER_*` variables out of the source's SQL and defaults. The visitor's name and email are ordinary form fields.

Antiforgery and any Anti-spam block run on each submission.

Public access also permits reads. To keep submitted messages private, register a policy that allows only initialization and creation for this source, or enforce the same rule in its backing class:

```csharp
public sealed class ContactSubmissionPolicy : IDataSourcePolicy
{
    public ValueTask<bool> IsAllowedAsync(DataSourceRequest request, CancellationToken ct)
        => ValueTask.FromResult(request.DataSource != "contact"
            || request.Action is DataSourceAction.Initialize or DataSourceAction.Create);
}
```

Register it with `builder.Services.AddScoped<IDataSourcePolicy, ContactSubmissionPolicy>()` and use the alias `contact` for the source. This refuses loads and queries of saved messages as well as updates and deletes. Use a separately authorized source for staff to read submissions.
