# Adding AppBlocks to a site

How AppBlocks installs into an Umbraco site, how its blocks render and are styled, how pages with blocks are cached, every setting, and the extension points a companion package uses.

## Installation

Reference the `AurorisNet.Umbraco.AppBlocks` package, or `src/AppBlocks/AppBlocks.csproj` during development. Its Umbraco composer registers the services, backoffice screens and routes.

At startup AppBlocks installs its element types and data types through Umbraco's package importer.

- Installation needs no synchronization package and no import configuration.
- The definitions a site lacks, and their folders, are installed in one transaction. A failed installation is tried again at the next startup.
- Definitions that already exist are left as they are, with their keys.
- Automatic installation follows `Umbraco:CMS:PackageMigration:RunSchemaAndContentMigrations`.
- The types sit under **AppBlocks** folders in the Data Types and Document Types trees, with subfolders for Forms, Data sources and Interactive Reports.

**Settings → AppBlocks → Definitions** shows which package definitions are installed, and which of them the installed version of the package has changed since. **Reinstall** resets every definition to the installed package, and **Uninstall** removes them all. [Developing AppBlocks](developing.md#who-owns-the-definitions) describes both.

Install `AurorisNet.Umbraco.AppBlocks.InteractiveReports` for interactive reports and `AurorisNet.Umbraco.AppBlocks.Documents` for documents. Each depends on AppBlocks and registers itself. See the [Interactive Reports](interactive-reports.md) and [Documents](documents.md) guides.

## Rendering

A site that renders blocks with Umbraco's `blockgrid/Components` or `blocklist/Components` partials shows the Forms container with no change, because AppBlocks ships a partial for it in both folders.

A site with another rendering convention invokes the `AppBlocksBlock` view component with `block`, a `BlockGridItem`, or `listItem`, a `BlockListItem`. Pass `page` when rendering outside the current routed page.

```cshtml
@await Component.InvokeAsync("AppBlocksBlock", new { block = Model })
```

## Styling

`App_Plugins/ApplicationBlocks/runtime.css` styles forms, attachments, tabular forms and schedules. The blocks inherit the site's font and base size. Colours, radii and shadows are custom properties with an `--app-` prefix.

The defaults are declared on `:where(:root)`, which has no specificity. A site's own rule that sets a token on `:root` or on `.application-blocks` therefore wins wherever its stylesheet loads. Restyle the blocks by setting tokens:

```css
.application-blocks {
    --app-accent: #5143a3;        /* buttons, links, focus rings and today's column */
    --app-accent-strong: #3f3482; /* the primary button on hover */
    --app-accent-soft: #eeebf8;   /* tinted backgrounds: new rows, today's column */
    --app-radius: .5rem;          /* the form card and regions */
    --app-radius-sm: .25rem;      /* controls, buttons and tables */
}
```

The top of the stylesheet lists every token: the surfaces (`--app-surface`, `--app-surface-2`, `--app-surface-3`), the borders, the text colours (`--app-text`, `--app-muted`), the danger colours for errors and the Delete button, `--app-shadow` and `--app-focus`.

`--app-focus` is a focus ring computed from `--app-accent` on the root. A site that sets `--app-accent` on `.application-blocks` only must set `--app-focus` there as well, such as `--app-focus: 0 0 0 3px color-mix(in srgb, #5143a3 28%, transparent)`.

### The form's frame

| Token | Default |
|---|---|
| `--app-form-max-width` | `72rem` |
| `--app-form-padding` | `1.75rem`, and `1.15rem` on narrow screens |
| `--app-form-background` | `--app-surface` |
| `--app-form-border` | `1px solid` in `--app-border` |
| `--app-form-radius` | `--app-radius`, and `--app-radius-sm` on narrow screens |
| `--app-form-shadow` | `--app-shadow` |

A form with no card around it sets three of them:

```css
.application-blocks { --app-form-padding: 0; --app-form-border: 0; --app-form-shadow: none; }
```

A form whose **Label position** is `left` puts each label beside its input. A field narrower than 26rem, which includes every field on a phone, stacks its label above the input.

### Schedules and layout classes

A schedule's bar colours are `--ah-schedule-kind-1` to `--ah-schedule-kind-8`, and `--ah-schedule-marker-height` is the height of a marker band. The [Schedule block guide](schedule.md) describes the columns that colour rows and bars from data.

The `ah-*` classes (`ah-grid`, `ah-layout`, `ah-region`, and each block's title and help) are for the site's theme to style. Inside `.appblocks-native` the stylesheet styles them itself, so a site with no theme still gets a grid, column layouts and framed regions.

### Scripts for a site's own pages

Two runtime modules are public. Importing either one mounts nothing.

- `App_Plugins/ApplicationBlocks/data-api.js` sends requests to AppBlocks' endpoints with the visitor's cookies and the site's path base. The [API endpoints guide](api-endpoints.md#calling-endpoints-from-a-page) lists its exports.
- `App_Plugins/ApplicationBlocks/schedule.js` builds a schedule's bars and row labels on a table the page lays out. The [Schedule block guide](schedule.md#a-schedule-of-your-own) describes the markup.

`runtime.js` is the module the blocks load. It mounts every block on the page.

### Texts

The blocks' own texts, such as **Saved.** and **Delete this record?**, come in English and French, chosen by the culture Umbraco serves the page in. [Localisation](localisation.md) lists every text and how to replace it.

## Previews in the backoffice

In a block grid, each AppBlocks block shows a preview in place of Umbraco's default card. Full width and Three columns keep Umbraco's card and their areas.

A preview keeps Umbraco's header, with the block's icon, label, **Not published** tag and edit link. Beneath it the preview draws the block's content, then the block's areas:

- A field's card shows its authored label or, when that is empty, its binding. The preview shows a disabled control with its placeholder, default value and help text.
- Rich text and HTML appear as authored. CSS and JavaScript show a line count. Open the block to edit the full source.
- A form region shows its heading. Its disclosure button folds the region for the current editor tab. Editors can fold a region and navigate away without saving.
- A form shows its publish validation errors above its fields. Follow a location to open that block in Umbraco. A duplicate binding lists every occurrence under its enclosing region.
- Forms, tabular forms, schedules, attachments and the anti-spam check appear as representative markup that names the form class or data source they use.
- A Forms container shows the forms inside it. A form's Form Interface shows the same preview inside its single-block property.
- With Interactive Reports installed, a report shows its heading, buttons and the columns of its starting view.

The backoffice sends the working form to the AppBlocks publish validator when its layout or values change. Rendering the preview never submits a form or runs an authored script or report action. Authored HTML is sanitized and drawn in a sandboxed frame with same-origin stylesheets and images.

### Replacing a preview

Each preview is a `blockEditorCustomView` extension registered for `block-grid`, with the alias `AppBlocks.Preview.<content type alias>`, such as `AppBlocks.Preview.appHtml`. The Form Interface's single-block preview is `AppBlocks.Preview.appFormInterface.Single`, and the report preview is `AppBlocks.Reports.Preview.appReport`.

AppBlocks leaves each view's `weight` unset, which is 0. A block editor shows the custom view with the highest weight, so a site replaces a preview by registering its own view for the same `forContentTypeAlias` and `forBlockEditor` with a higher weight:

```json
{
  "type": "blockEditorCustomView",
  "alias": "MySite.Preview.appHtml",
  "name": "HTML preview",
  "element": "/App_Plugins/MySite/html-preview.js",
  "forContentTypeAlias": "appHtml",
  "forBlockEditor": "block-grid",
  "weight": 100
}
```

Naming `AppBlocks.Preview.appHtml` in the view's `overwrites` removes AppBlocks' view. To return a block to Umbraco's default card, an `appEntryPoint` excludes the preview: its `onInit(host, extensionRegistry)` calls `extensionRegistry.exclude("AppBlocks.Preview.appHtml")`.

### Making previews look like the site

`App_Plugins/ApplicationBlocks/block-preview.js` exports two arrays that every preview reads when it draws. A site adds to them from an `appEntryPoint`:

- `previewStylesheets` holds stylesheet URLs. It starts with AppBlocks' runtime stylesheet, and a site adds its frontend stylesheet.
- `previewStyles` holds CSS text applied after the stylesheets and the preview's own rules. A site adds preview-only overrides, such as showing every panel of a component that its pages reveal with script.

Relative URLs resolve against the site root, path base included.

```js
// App_Plugins/MySite/preview-styles.js, registered as an appEntryPoint
import { previewStylesheets, previewStyles } from "../ApplicationBlocks/block-preview.js";

export function onInit() {
    previewStylesheets.push("css/site.css");
    previewStyles.push(".tab-panel{display:block}.carousel-controls{display:none}");
}
```

### Previews for another package's blocks

A package previews its own blocks by extending `BlockPreview` from the same module, which gives them the same header, frame, sanitizer and areas.

A subclass overrides `renderContent(alias, content, settings)`. It receives the block's content and settings values by property alias and returns markup as a string, or an empty string to show the header and areas alone. `App_Plugins/ApplicationBlocks/preview-model.js` has the helpers AppBlocks' previews use, such as `escape`, `heading`, `note`, `button` and `table`. `this.mediaImage(value)` draws a media picker's image.

The package defines its element and registers manifests for its own content type aliases only:

```js
// App_Plugins/MyPackage/block-preview.js
import { BlockPreview } from "../ApplicationBlocks/block-preview.js";
import { heading, note, escape } from "../ApplicationBlocks/preview-model.js";

export default class QuotePreview extends BlockPreview {
    renderContent(alias, content, settings) {
        return heading(content.title) + `<blockquote>${escape(content.quote)}</blockquote>` + note(content.author || "Add an author.");
    }
}
customElements.define("my-package-block-preview", QuotePreview);
```

```json
{
  "type": "blockEditorCustomView",
  "alias": "MyPackage.Preview.myQuote",
  "name": "Quote preview",
  "element": "/App_Plugins/MyPackage/block-preview.js",
  "forContentTypeAlias": "myQuote",
  "forBlockEditor": "block-grid"
}
```

The header follows the manifest's `forBlockEditor`: `block-list` and `block-single` get those editors' headers and no areas, and any other value gets the grid's. These are Umbraco's `UMB_BLOCK_GRID`, `UMB_BLOCK_LIST` and `UMB_BLOCK_SINGLE` values. They differ from property editor aliases such as `Umbraco.BlockGrid`, which stored block layouts use.

A package whose blocks can sit inside a Forms container also registers how to draw them there. From an `appEntryPoint` it calls `nestedBlockPreviews.set("<content type key>", (content, settings) => markup)`, with `nestedBlockPreviews` imported from the same module.

## Access and caching

### Who reaches a block

Umbraco's Public Access on the page is the baseline. A block on a protected page refuses whoever the page refuses. On a page without Public Access, an anonymous visitor reaches the page's interactive blocks, and whatever is behind each block decides what they get:

- **A C# form class with no check of its own is public.**
- A SQL data source has an **Access** setting: **Public**, which follows the page, **Not public** or **Specific groups**. Anonymous writes also need **Allow anonymous writes** on the source.
- A query or row restriction that uses a `:USER_*` value requires sign-in.
- Paper report downloads require a signed-in account.

Blocks also work for a signed-in Umbraco Member or backoffice User. Backoffice administrators receive no application permissions automatically. The [business rules guide](business-rules.md#where-each-rule-belongs) describes every layer, and how a site admits someone Umbraco refuses with an `IProtectedPageAdmission`.

### Page cache headers

`AppBlocks:NoStorePages` controls the cache headers AppBlocks adds to pages:

| Value | Behavior |
| --- | --- |
| `AppBlocksPages` (default) | Sends `no-store` and prevents output-cache storage for a page with a block whose output depends on who asks: a block with Visibility, Security or Read Only conditions, a form, attachments, a schedule, a tabular form, or a companion's block that requires a user or varies by data. The page's Public Access login and denied responses get the same headers. A page of content blocks alone is cached as Umbraco and the site decide. |
| `AllPages` | Applies that behavior to every page Umbraco routes. |
| `Off` | The site and Umbraco control caching. Evaluating visitor or item-based block Security or Read Only conditions still sets `no-store` and prevents output-cache storage. |

```json
{ "AppBlocks": { "NoStorePages": "AppBlocksPages" } }
```

### Endpoint cache headers

The blocks' own endpoints, below `/api` or `AppBlocks:ApiRoot`, send `no-store` in every mode. An output cache policy the site adds still applies to them, so exclude them from it. [API endpoints](api-endpoints.md), below `/api/data` or `AppBlocks:ApiBasePath`, send `no-store` too.

Antiforgery and other middleware of the site may set cache headers of their own, including when the setting is `Off`.

A cookie for a disabled or locked-out account is refused on every page, open pages included. AppBlocks never treats such a caller as an anonymous visitor.

## Settings

AppBlocks runs with no configuration. `src/AppBlocks/appsettings-schema.json` describes these settings for an editor that reads JSON schemas.

| Setting | Default | What it does |
| --- | --- | --- |
| `AppBlocks:NoStorePages` | `AppBlocksPages` | Which pages AppBlocks keeps out of caches. See [Page cache headers](#page-cache-headers). |
| `AppBlocks:ApiRoot` | `/api` | Where forms, attachments, tabular forms and the antiforgery token endpoint answer, and the path `IsApiRequest` treats as API-style. Pages with blocks carry it in an `appblocks-api-root` meta tag, which the runtime and `data-api.js` read. The companions' report, download and paper report routes stay under `/api`. |
| `AppBlocks:ApiBasePath` | `/api/data` | Where [API endpoints](api-endpoints.md#base-path) answer. |
| `AppBlocks:ApiEndpoints:MaxRouteSegments` | `8` | The most segments an API endpoint's route may have. |
| `AppBlocks:AdministratorGroups` | none | Backoffice User Group aliases admitted beside Umbraco's Administrators to AppBlocks' Settings screens, the Backing Classes editor, and the data source and API endpoint **Test** actions. Umbraco's section and node permissions still apply. |
| `AppBlocks:CodeDirectory` | `AppBlocks/Code` | The [Backing Classes](csharp-editor.md) folder. |
| `AppBlocks:DataRoot` | the content root | The folder whose `App_Data` holds AppBlocks' settings files and its fallback database files. |
| `AppBlocks:Definitions:UpdateAtStartup` | `false` | Whether a start resets each package definition that the installed version changed. See [Definitions a new version changes](developing.md#definitions-a-new-version-changes). |
| `AppBlocks:Forms:MaxBodySize` | `1048576` | The largest form submission, in bytes. A larger one is answered with 413. |
| `AppBlocks:DataSources:CommandTimeoutSeconds` | `30` | How long a data source statement may run. `0` waits without limit. A statement that runs out of time is answered with 503. |
| `AppBlocks:DataSources:MaxChanges` | `200` | The most row changes in one tabular save, or in one block's rows of a form's save. |
| `AppBlocks:DataSources:ReceiptLifetimeMinutes` | `15` | How long a create receipt is kept, so that a retried create returns its record. See [Concurrency and duplicate submissions](business-rules.md#concurrency-and-duplicate-submissions). |
| `AppBlocks:DataSources:ReceiptCapacity` | `10000` | How many create receipts the in-memory store holds. |
| `AppBlocks:Tabular:MaxRows` | `1000` | The most rows a Tabular form block lists for one record. A block's own limit is cut to it. |
| `AppBlocks:Schedule:MaxDays` | `400` | The longest window of day columns. |
| `AppBlocks:Schedule:MaxHourDays` | `31` | The longest window of hour columns, in days. |
| `AppBlocks:Schedule:MaxRows` | `2000` | The most rows a schedule reads. |
| `AppBlocks:Schedule:MaxBars` | `5000` | The most bars a schedule reads. |
| `AppBlocks:Schedule:WeekendDays` | Saturday and Sunday | The days a schedule shades as the weekend, such as `["Friday", "Saturday"]`. |
| `AppBlocks:AntiSpam:<service>:SiteKey`, `SecretKey` | none | [Anti-spam](anti-spam.md) keys. The settings screen cannot change a key set here. |
| `AppBlocks:AntiSpam:VerifyHostname` | `true` | Refuses a token that the service says was solved on another host. Set it to `false` behind a proxy that hides the request's host from the site, or with a service's published test keys. |
| `Attachments:Connection` | `AppBlocksDb` | The database that stores uploads. See [Connections](connections.md). |
| `Attachments:MaxFileSize` | `26214400` (25 MB) | The largest file any Attachments block accepts, in bytes. |
| `Attachments:AllowedExtensions` | any | The extensions every Attachments block is limited to, such as `[".pdf", ".png"]`. |
| `Attachments:RetentionDays` | `30` | How long a removed file stays before an administrator's purge deletes it. |
| `Attachments:PendingLifetimeHours` | `24` | How long a file uploaded to an unsaved record waits for the save. An hourly Umbraco job deletes expired pending uploads. |

The companions add settings of their own: `AppBlocks:ReportAdministratorGroups`, `AppBlocks:IncludeBackOfficeReports` and `AppBlocks:InteractiveReports:PaperSizes` in the [Interactive Reports](interactive-reports.md) guide, and `AppBlocks:Documents:*` in the [Documents](documents.md) guide.

## Companion packages

A companion package adds blocks, controllers, content nodes and pages of its own beside the ones AppBlocks provides.

### Blocks

A companion adds a block by registering an `IAppBlockPlugin`. Two of its members decide how AppBlocks treats a page that has the block:

| Member | When true |
| --- | --- |
| `RequiresUser` | The block acts for the signed-in person, as a report does. Publishing refuses it inside a form, and its page is kept out of caches. |
| `VariesByData` | The block's output can change without a publish, such as a public list read from a database. Its page is rendered for each request and kept out of caches. |

`VariesByData` answers `RequiresUser` unless the plugin implements it. A public block that reads a database sets `VariesByData` and leaves `RequiresUser` false.

```csharp
public sealed class NoticesBlock : IAppBlockPlugin
{
    public string Alias => "noticeBoard";
    public string ViewPath => "~/Views/Partials/Notices/Board.cshtml";
    public bool RequiresUser => false;
    // The notices come from the database, so a cached copy of the page would go stale.
    public bool VariesByData => true;
    public void Validate(JsonElement block, List<string> errors) { }
}
```

### Form submissions

A companion whose blocks sit in a form can check and rewrite the submitted values on the server by registering an `IFormSubmissionProcessor`. `ProcessAsync` receives the form, the submitted values, the stored record and the execution context. It runs after admission, antiforgery and field validation, and before the record is saved. It returns the values to save, or a `FormResult` that refuses the submission. A processor changes only the fields its own blocks bind.

### Controllers for signed-in Members

A companion's API controller for signed-in Members uses the `AppBlocks.Member` authorization policy, `AppBlocksAuthorization.MemberPolicy`. The policy reads Umbraco's Member cookie and admits a Member whose account is approved and not locked out. It reads the account on every request.

The policy answers an API-style request with a status a script can act on:

| Caller | API-style request | Any other request |
| --- | --- | --- |
| No Member session | 401 | Umbraco's redirect to the login page |
| A Member who is locked out or unapproved | 403 | Umbraco's access-denied redirect |

A backoffice User without a Member session counts as having no Member session.

A request is API-style when one of these holds, and `AppBlocksAuthorization.IsApiRequest(httpContext)` gives the same answer to a site's code:

- Its path is under the blocks' API root, `/api` unless `AppBlocks:ApiRoot` moves it.
- Its controller is an `[ApiController]`.
- It accepts JSON.
- It carries `X-Requested-With: XMLHttpRequest`.

**Antiforgery.** A signed-in Member's script gets a token from `GET /api/antiforgery/token`, below the API root. `AppBlocksAuthorization.AntiforgeryTokenPath(configuration)` gives the path. The endpoint answers `{ "token": "...", "headerName": "RequestVerificationToken" }` with `Cache-Control: no-store`, and 401 to anyone else. The token is bound to the Member, so it validates on a controller under the policy. Send it in the named header with each change:

```csharp
using AppBlocks.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/notices")]
[Authorize(Policy = AppBlocksAuthorization.MemberPolicy)]
public sealed class NoticesController(NoticeStore notices) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(notices.ForMember(User.FindFirstValue(ClaimTypes.NameIdentifier)));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Post(NoticeInput input) => Ok(notices.Add(input));
}
```

`memberRequest` in `App_Plugins/ApplicationBlocks/data-api.js` does this for a page's script. It fetches the token once per page and sends it with every method except GET and HEAD. A refusal rejects with an `ApiError` whose `status` is 401 or 403:

```js
import { memberRequest, errorText } from "/App_Plugins/ApplicationBlocks/data-api.js";

const status = document.getElementById("notice-status");
try {
    await memberRequest("/api/notices", { method: "POST", body: { title: "The car park is closed on Friday" } });
} catch (error) {
    status.textContent = errorText(error, "The notice could not be posted.");
}
```

A mapped endpoint takes the same policy with `RequireAuthorization(AppBlocksAuthorization.MemberPolicy)`. The policy decides only that the caller is a current Member. The controller decides which Members may do what.

### Backoffice controllers for administrators

A companion's management API controller for administrators takes the `AppBlocks.Administrator` policy, `AppBlocksAuthorization.AdministratorPolicy`, as AppBlocks' own Settings screens do.

The policy admits a backoffice User in Umbraco's Administrators group, or in a group whose alias `AppBlocks:AdministratorGroups` lists, whose account is approved and not locked out. It reads the account on every request and answers anyone else with 403. It adds to the backoffice access that every management controller already requires, and it can sit on a controller or on single actions:

```csharp
using AppBlocks.Authentication;
using Microsoft.AspNetCore.Authorization;

[VersionedApiBackOfficeRoute("notices/settings")]
[Authorize(Policy = AppBlocksAuthorization.AdministratorPolicy)]
public sealed class NoticeSettingsController(NoticeSettings settings) : ManagementApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(settings.Read());
}
```

Where the policy applies in AppBlocks itself:

- The connection list, database types and business rules guide are open to every backoffice user. The table list requires `AppBlocks.Administrator`.
- The data source and API endpoint **Test** actions require the policy, because they run the current SQL before it is saved or published. They also require Content section access and permission to update the node, or to create under the parent of a new node. An editor who is not an AppBlocks administrator sees a note in place of the Test button.
- The Backing Classes editor requires the policy and Settings section access.

`GET /umbraco/management/api/v1/appblocks/administrator` answers `{ "allowed": true }` or `false` for the signed-in User. A companion's screen can use it to hide what only administrators may use.

### Names a companion owns

A companion registers the names it owns from its own registration, through `builder.AddAppBlocks(appBlocks => ...)`:

- `AddKeySegmentContentType("alias")` gives the nodes of a document type a URL segment made from their own key, as data source nodes have. A definition node named like a page then never takes that page's URL. The reports companion registers its report definitions this way.
- `ReserveConnectionName("name")` stops the Connections screen from saving a connection under a name the companion opens through its own factory.

### Nodes a package generates

API endpoints, lists of values and data sources are content nodes that editors publish. A package that generates some of them from code installs them with `OwnedNodeInstaller`. Each node is an `OwnedNode`: a fixed key, a name, the document type alias and the property values by alias, as the backoffice stores them.

```csharp
public sealed class NoticeDefinitions(IServiceScopeFactory scopes) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        // A scope of its own, as the publish checks the installer runs use scoped services.
        using var scope = scopes.CreateScope();
        var installer = scope.ServiceProvider.GetRequiredService<OwnedNodeInstaller>();
        var dataSources = installer.EnsureDataSourcesRoot();
        installer.Install([
            new OwnedNode(NoticesEndpointKey, "Notices API", "apiEndpoint", new Dictionary<string, object?> {
                ["alias"] = "notices.current", ["route"] = "notices", ["connection"] = "NoticesDb", ["access"] = "Public",
                ["sql"] = NoticeRules.CurrentSql, ["shape"] = "Collection",
            }, Parent: dataSources),
        ]);
    }
}
```

`EnsureDataSourcesRoot` returns the key of the published **Data sources** node at the content root, which is the parent of data sources, lists of values and API endpoints. It publishes an existing node that is unpublished. On a site with none, it creates and publishes one under the key `OwnedNodeInstaller.DataSourcesRootKey`.

The installer stores a SHA-256 fingerprint of each node's definition in Umbraco's key-value table, under `AppBlocks:OwnedNode:<key>`:

- When the definition has the stored fingerprint, the installer changes nothing and the node keeps its published version.
- When the definition has changed, the installer updates the node and publishes it. Publishing runs the checks of the node's type, so an endpoint's SQL is checked against its database at that start.
- When the checks refuse the definition, the installer throws an `OwnedNodeException` with their reasons. It publishes nothing and records nothing, so the next start tries again.

`Install` answers `Created`, `Updated`, `Unchanged` or `LeftDeleted` for each node.

The package owns the properties its definition names. An editor's change to one of them lasts until the package's definition next changes. A property the definition leaves out keeps whatever an editor gives it. A node that an editor deleted or moved to the recycle bin comes back at the next start, unless the call passes `recreateDeleted: false`. For a document type that varies by culture, give each culture's name and values in `Cultures`.

Call the installer once the things its nodes are checked against exist: the database and tables of a list or endpoint, and the data source a node selects. Install a parent before its children. `Install` with a list installs the nodes in order.

### Pages built from code

A package that installs pages with a form on them builds the block values with `AppBlockValues`, from element type aliases and field aliases. The package writes no element type key, area key or block key. Every key comes from the package's schema. A block given an `id` keeps the same key each time the page is installed, so an unchanged definition writes an unchanged value.

```csharp
using AppBlocks.Blocks;

var forms = new AppBlockValues();
var form = forms.Form("notices", [
    forms.Field("appText", "title", new { label = "Title" }, id: "notice.title"),
    forms.Field("appDate", "postFrom", id: "notice.from", columnSpan: 6),
    forms.Field("appDate", "postUntil", id: "notice.until", columnSpan: 6),
], returnPage: noticesPageKey, actions: new { saveLabel = "Post notice" }, values: new { heading = "Notice" }, id: "notice.form");
// Any other block by its alias, with its property values by alias, placed on the grid in order.
forms.Place(forms.Add(AppBlockSchema.RichTextAlias, id: "notice.intro"), form);

string json = forms.ToJson();
```

- `ToJson` gives the value of a property that uses the **AppBlocks: Forms container** data type, such as a Forms container block's `forms` property. Use it in `OwnedNode` values or a content service save.
- `Form` takes the alias of a registered C# class or the document key of a published SQL data source. It writes the Form Interface with the key parameter, `id` unless given, and the return page. It places the fields in the form's `fields` area and an Actions block in its `actions` area.
- `Field` refuses an alias that is not a field block.
- Use `appFormFullWidth`, `appFormColumns`, `appFormThreeColumns` or `appFormRegion` to arrange fields inside a Form. For example, `forms.Add("appFormColumns").Place("left", title).Place("right", date)` places the two fields side by side. The page layouts accept standalone blocks such as rich text and attachments.
- `AppBlockItem.Place(area, ...)` refuses an area the block's grid entry lacks.

**Allowing a block in a Block Grid.** A package or site allows a block in one of the site's Block Grids from its registration, so no editor has to configure the grid:

```csharp
builder.AddAppBlocks(appBlocks => appBlocks.AllowInBlockGrid("Main content", AppBlockSchema.FormsContainerAlias));
```

`AllowInBlockGrid(blockGridDataTypeName, elementTypeAlias, columnSpan, rowSpan, areaKey)` adds the element to the named Block Grid data type once both exist: at startup, after Umbraco finishes installation, and when the element type is saved. Without `areaKey` the block may sit at the grid's root and in any area. With one, named by key or alias, it may sit in that area only. An element the grid already lists keeps the settings the site gave it. A host that puts a Block Grid data type back at a point of its own, such as an import of its saved definitions, applies the registrations again with `BlockGridAllowances.ApplyAsync`.

**Page-wide CSS and JavaScript.** A site whose document types carry page-wide CSS and JavaScript in properties hands them to the blocks' asset writer from its layout, with `AppPageAssets.RegisterPage(page, navigation, published, httpContext, cssAlias, scriptAlias)`. The code of the page and of each ancestor, root first, comes before the CSS and JavaScript blocks' own. The aliases default to `css` and `javascript`.

### Pages that change between releases

An application that installs pages from code, and lets editors change them afterwards, carries those pages from one release to the next with `ContentStepRunner`. A new site gets every page as the current build writes it. A site that an earlier build installed runs each change it has not run yet, once, in order.

```csharp
public sealed class NoticePages(IServiceScopeFactory scopes) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        // The package's definitions first, as the handlers of this notification run in no set order.
        await scope.ServiceProvider.GetRequiredService<SchemaInstaller>().EnsureAsync(ct);
        var contents = scope.ServiceProvider.GetRequiredService<IContentService>();
        var patcher = scope.ServiceProvider.GetRequiredService<ContentPatcher>();
        await scope.ServiceProvider.GetRequiredService<ContentStepRunner>().RunAsync("Notices",
            install: _ => { NoticeInstaller.InstallPages(contents); return Task.CompletedTask; },
            steps: [
                new ContentStep("0001-v1.0.1-lead-time-notice", _ =>
                {
                    patcher.Patch(contents.GetById(NoticesPageKey)!, "blocks", NoticeBlocks.WithLeadTimeNotice);
                    return Task.CompletedTask;
                }),
            ], ct);
    }
}
```

| Site | What `RunAsync` does |
|---|---|
| The plan has installed nothing here | Runs `install`, and records the site at the last step. `install` writes the pages as this build has them, with the result of every step in them |
| An earlier build installed the pages | Runs each step after the site's position, in order |
| The site is at the last step | Nothing |

The plan is the application's name for its content, and it stays the same across releases. The site's position is one entry in Umbraco's key-value table, `AppBlocks:ContentSteps:<plan>`.

Name each step with the next number, the release that ships it and the change, as [run-once scripts](connections.md#a-schema-that-changes-between-releases) are named. Steps run in the ordinal order of their names, and `RunAsync` refuses a list whose names are out of order. A shipped step keeps its name and what it does, and stays in the list, so a site that skipped releases runs the steps of each.

Each step runs inside one Umbraco scope, together with the record that it ran:

- **A step that throws stops the run** with a `ContentStepException`. Its changes to Umbraco's database roll back, the site stays at the step before, and the next start runs the step again.
- **What a step writes elsewhere stays written**, such as rows in the application's own database. Write those so that a second run changes nothing.
- **A site that another build took further is left alone.** When the site has run a step that sorts after the build's last one, `RunAsync` runs nothing and throws a `ContentStepException` with `SiteFromAnotherBuild` set. Install that build, or restore the backup of Umbraco's database taken before it was installed.
- **`RunAsync` opens each step's scope itself**, so call it where no Umbraco scope is open, as startup code is.

`install` runs outside those scopes, so a page it publishes is in the published cache for the pages it publishes next. When `install` throws, nothing is recorded and the next start runs it again. Write it to find what it already created, as an installer that looks its pages up by key does.

**Changing a page an editor may have changed.** `ContentPatcher.Patch(content, property, change)` applies a change to the stored value of one property, such as the JSON of a page's blocks. `change` takes the value and returns the new one, or null for a value that already has the change. The published version takes the change and is published again. A draft that an editor saved since the last publish stays a draft and takes the same change, so the editor's unpublished work is neither published nor lost. Pass `culture` for a document type that varies by culture, and `name` to rename the node. `Patch` answers false when both versions already were as the change leaves them.

**A site installed before its application had steps.** `Record(plan, "")` marks the site's content as installed. `RunAsync` then runs the steps on it and leaves `install` alone.

**Nodes with no editor's changes to keep** need no steps. [`OwnedNodeInstaller`](#nodes-a-package-generates) rewrites them whenever their definition changes.

**Keeping the two routes equal.** A new site reaches its pages through `install` and an updated one through the steps, so a step and the matching change to `install` are written together. `AppBlocks.Testing` lists a site's pages as text and compares the listing with a file under source control, for a new installation and for a site updated from the previous release. See [Content checks](../src/AppBlocks.Testing/README.md#content-checks).
