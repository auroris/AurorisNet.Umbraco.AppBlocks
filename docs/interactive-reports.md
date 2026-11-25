# Interactive Reports package

`AurorisNet.Umbraco.AppBlocks.InteractiveReports` adds the **Interactive Report** block, powered by [Interactive Reports](https://github.com/auroris/AurorisNet.InteractiveReports). This page covers the block's source, who may see a report, its links to a form page, saved views, downloads, printing, storage and administration.

The package supplies the report block and its rendering partials, the report editors, the browser runtime, the JSON and download endpoints, saved views and report authorization. AppBlocks supplies the connections, data sources, forms and page access.

## Adding a report

Install the package. Its composer registers it, and at startup it installs the report element type and data types beside AppBlocks' own.

1. Add **Interactive Report** to the allowed blocks of a Block Grid or Block List.
2. Add the block to a page and choose its source in **Table or SQL query**.
3. Configure the block's **Starting view**: columns, sorting, filters, paging and presentation.

| Source | What to enter |
|---|---|
| Table | A table or view of a connection from **Settings → AppBlocks → Connections**, listed as `DataSourceName.TableName`, with the schema where it is needed |
| SQL query | A connection and a SELECT. The query may use `:PARAM.name` and the signed-in user's bind variables, such as `:USER_KEY` |

The source and the starting view are saved on the block. Connection credentials stay in AppBlocks' configuration.

An administrator can preview an unpublished source to configure its view. A preview is private to its creator and expires after ten minutes.

## Who may see a report

A report block follows AppBlocks' page access and the registered data source policies.

**A report on a page without Public Access is public, export included.** An anonymous visitor can view, query and download it. Protect the page when its rows are not for everyone.

- Saved views are hidden from an anonymous visitor, and printing through Documents is not offered.
- A report whose SQL uses a `:USER_` bind variable never runs for an anonymous visitor. The page shows a sign-in notice and the endpoints answer 401.
- A custom `IReportAccessPolicy` or `IDataSourcePolicy` can refuse anonymous visitors, who then get the sign-in notice. A replacement policy that wants the standard rule calls `AnonymousReportAccess.Allows`.

### Report definition nodes and configured reports

A report that is not a block, such as a report definition node or a report configured in the `InteractiveReport` section, requires an account. A report definition node's **Access** tab narrows that:

| Access | Who may open the report |
|---|---|
| **Signed-in accounts** (the default, and the meaning of an empty value) | Any signed-in Member or backoffice User |
| **Specific groups** | Members of the Member Groups picked in **Access groups** |

Report administrators keep their access, and an authorization policy named in the report's options still applies. The package lets report definition nodes sit under the **Data sources** root.

## Endpoints

| Route | Purpose |
|---|---|
| `/api/reports` | Report schema and rows for the website |
| `/api/download` | Downloads for the website |
| `/umbraco/reports`, `/umbraco/report-download` | The same for the backoffice |

These routes stay under `/api` whatever `AppBlocks:ApiRoot` says. That setting moves the base package's endpoints only.

## Linking a report to a form

On the Interactive Report block, open **Form links** and choose where the buttons go.

| Workflow | Settings |
|---|---|
| Edit beside the report | Add a Form block to a Forms container on the same page. Turn on **Form is a sidebar** on the form and **Form on this page** on the report. |
| Open a separate form page | Use the **Form page** picker. Both actions open that Umbraco page. If the target form is a sidebar, it opens on arrival. |
| Create on a different page | Set **Create form page**. This overrides the destination for Create only. |

Set **Record key column** to the report column that identifies the row, such as `LINE_ID`. **Open** passes that value to the form. **Create** opens a new record. **Open label** and **Create label** change the text shown by the report.

A report automatically targets the only sidebar on a page, even beside an inline master form. A single form also supplies its own **Record key query parameter**. To select a different form or choose between sidebars, give each Form Interface a distinct parameter and enter the intended one on the report. Same-page form links and links to sidebars require an unambiguous match. Leave the destinations empty and **Form on this page** off for a report without form links.

### Master and detail on one page

On the detail Form block, open **Master/detail**. Choose the **Parent form** and the **Link field** on the detail resource. If the parent lives on another page, choose its **Parent page** first. The parent key defaults to `id` and the detail key to `detailId`. You can give either Form Interface a different **Record key query parameter**. Configure the report’s form links to target the detail form.

For an order and its lines, the parent form might use `orderId` and the line form `lineId`. Pick the order form and the line resource’s `orderId` field. At `/orders/?orderId=42` the report lists lines for order 42. Create assigns that order to the new line. Open, Save and Delete check that an existing line belongs to it. The report source must expose the link field or its mapped database column. It can still be a custom SELECT. A form needs no field block for the link. If one is added, the value is read only.

The parent form must be able to load a saved record for this visitor before the report offers detail rows or actions. The detail resource applies its own access checks. A sidebar save writes the detail immediately. For a new master and draft lines that should save together, use a Tabular form block inside the master. Existing reports with a manually configured parameter filter can keep using it.

The sidebar keeps the master’s unsaved fields in place. Saving or deleting closes it and refreshes the originating report with its current view. Cancel, Close and Escape dismiss it. A validation error leaves the sidebar open. A form used on its own has a launcher labeled with its heading. On a page where a report targets the sidebar, the report supplies the controls.

### Button visibility

Each of the two has a setting for who sees it:

| Choice | Who sees the link or button |
|---|---|
| **Public** | Everyone the report's page admits |
| **Form page decides** (the default, and the meaning of an empty value) | Those the form page lets add or change records. See below |
| **Groups** | Those in a backoffice User Group or Member Group picked under the setting. At least one group is required |
| **Hidden** | Nobody |

Every Open link choice except Hidden needs the record key column. A published block whose Open link lacks its key column offers no Open link, and the log says so once.

The backoffice group pickers store group aliases separated by commas. Membership is checked against the caller as AppBlocks resolves them for data sources, for Members and backoffice Users alike.

### Form page decides

The create button appears for a caller the form page lets add a record, and the open link for a caller it lets change records. Both are worked out for each request, as that caller.

| The form's Form Interface | How it decides |
|---|---|
| Selects a SQL data source | From the source's **Allow insert** or **Allow update**, its write groups, its anonymous-writes setting and any `:USER_` binding in its writes |
| Is marked **Read only** | Offers neither |
| Selects a C# form class | Asks the class's `InitializeAsync` for that caller, with the master page’s parameters for a same-page target. The button shows when the new record it returns allows `Create`. The link shows whenever it answers with a record |

A C# class decides about changing an existing record when that record loads, so the link cannot know in advance. A class that refuses the caller hides both, and a class that throws keeps both. A link to a particular form asks that form. For an ordinary page link with several inline forms, one form that allows the action is enough. A page whose forms do not resolve keeps the link.

Registered `IDataSourcePolicy` rules and a data source's backing class decide at the write itself. A caller that only they refuse sees the link, and the form then refuses the save. **The form endpoints check every write, so these settings are a convenience for readers and the form remains the rule.**

## Saved views

A saved view belongs to one identity: the Member when a Member is signed in, otherwise the backoffice User.

- Website routes use the Member session. A site that explicitly enables `AppBlocks:WebsiteIdentity:IncludeBackOfficeUsers` also accepts a backoffice session and uses the Member for ownership when both are active.
- The backoffice routes authenticate the backoffice User only, so views saved there belong to the User.
- A report block's page access applies on the backoffice routes too. The page resolves its user from every session on the request, the Member cookie included, and admits the backoffice caller when the User is among them.
- The owner key is read through `InteractiveReport:IdentityClaim` when the site sets it, as the engine stores owners. The default `IReportAccessPolicy` compares owners the same way.

### Where saved views are stored

Saved views go in `IR_SAVED_REPORTS` on the `AppBlocksDb` connection, which opens `AppBlocks.sqlite.db` beside Umbraco's SQLite database. To store them elsewhere, set one of these. Either takes precedence over `AppBlocksDb`:

| Setting | Value |
|---|---|
| `InteractiveReport:SavedReports:Connection` | A registered connection |
| `InteractiveReport:SavedReports:DataSource` | A configured connection-string name |

The saved report administration page at `/api/reports/admin` also reads `IR_ADMINISTRATORS`. AppBlocks decides who is an administrator itself, so nothing a reader does needs that table.

Where the account may, the report store creates the tables on first use. On Oracle 11.2 that includes a sequence and a trigger behind the key. Where the account may not, **Settings → AppBlocks → Database setup** shows the permission to grant or a script to run, and either takes effect on the next request. [Database setup](database-setup.md) has the permissions for each database type.

- The script works for an account with no DDL rights. The account needs read and write on the tables, which the script grants on SQL Server and PostgreSQL.
- On PostgreSQL and Oracle, an account with no DDL rights is refused the store's DDL even when the objects exist. The store then checks that it can read the table and carries on.
- `InteractiveReport:SavedReports:AutoCreate` set to `false` stops the store from trying to create the tables.

Until the tables exist, reports load, query and export on their starting view. The **Report** actions and the saved-view selector are absent, and readers see no error. The reason is logged once, and a backoffice administrator sees a line on the block that links to the setup screen. The administration page keeps reporting failures to administrators.

A site that replaces `ISavedReportStore` with its own answers for that store's storage, and the setup screen says so.

## Downloads

**Actions → Download → HTML** downloads a self-contained HTML file of all matching rows in A4 portrait, with the current report's filters, columns, labels, masks, sorting and totals. CSV is available beside it.

- The `InteractiveReport.Client.FileDownload` package generates the HTML. Documents and Pressroom are not needed.
- HTML follows the report's `download` control and the server's export authorization, as CSV does.
- `POST /api/download/{name}/html`, and `/umbraco/report-download/{name}/html`, serve the file as an attachment.
- A truncated or oversized result returns an error in place of an incomplete file.

## Printing

**Actions → Print** uses the optional `AppBlocks.DocumentOutput.IDocumentOutputService` contract of the base AppBlocks assembly. Install `AurorisNet.Umbraco.AppBlocks.Documents` and add Pressroom configurations in its settings to offer it.

The menu item is absent when the service is missing or has no PDF or printer destination. It also follows the report's `download` control and export authorization. This package has no dependency on Documents or Pressroom and handles no Pressroom URLs, API keys or printer discovery.

The dialog offers the configured destinations, all matching rows (the default) or the current page, a paper size, an orientation and a preview.

- **Paper sizes.** A4, Letter and Legal, with the first selected. `AppBlocks:InteractiveReports:PaperSizes` lists others, in order, from `Letter`, `Legal`, `Tabloid`, `A3`, `A4` and `A5`. A print request may name any of those six.
- **Content.** The document prints the authorized server query's columns, labels, masks, filtering, sorting, grouped and pivot rows, breaks and totals. A chart prints its underlying data table, and interactive renderers become text. The footer of a current-page print identifies aggregates that cover all matching rows.
- **Formatting.** The document uses the culture of the request that prepared the preview and the web server's time zone. The dialog takes its colours, borders and radii from the blocks' `--app-` tokens.
- **Limits.** An export limit produces an error. The report is never printed incomplete.

### Print endpoints

| Route | Purpose |
|---|---|
| `GET /api/reports/{name}/print/options` | The destinations, paper sizes and an antiforgery token |
| `POST /api/reports/{name}/print/preview` | Stores an HTML snapshot for five minutes, bound to the user, the report and the page query |
| `POST /api/reports/{name}/print/submit` | Checks export access and the data again, consumes the preview token once and calls Documents |

Both POSTs require the antiforgery token that `options` returns. Changed data or destinations require a new preview. No client-supplied HTML or report rows are submitted to Documents.

Preview tokens live in a bounded memory cache. A site with several instances needs session affinity for this flow, and an eviction or a restart requires another preview.

A PDF destination downloads a file, and Documents' HTML fallback is identified as such. A printer destination returns a submission receipt, which does not confirm physical printing. **Neither the browser nor the server retries a print request**, uncertain failures included.

A form's or report's record-level **Paper report** link is separate from printing the interactive report.

## Connections

The package uses AppBlocks' connections. Connections registered with `AddAppBlocks`, before or after this package, are available to reports, appear read-only under **Settings → AppBlocks → Connections** and can be chosen by a report block:

```csharp
using AppBlocks;

umbraco.AddAppBlocks(connections =>
    connections.AddConnection("Business", services => new SqlConnection(connectionString)));
```

Customize the report engine itself with `AddAppBlocksInteractiveReports`:

```csharp
using AppBlocks.InteractiveReports;

umbraco.AddAppBlocksInteractiveReports(reports =>
{
    // Interactive Reports authorization, row restrictions, context parameters or logging.
});
```

Both calls are safe to repeat. A connection added directly to the report builder is available to configured reports, report definition nodes and forms. It stays off the Connections list and a report block refuses it, so register a site's connections with `AddAppBlocks`.

To hand a builder's connections to a report engine composed without this package, call `reports.AddConnectionsFrom(connections)`.

**Oracle 11.2** and **Oracle 12+** select Interactive Reports' Oracle SQL compatibility modes, which AppBlocks shares across forms and report blocks. [Connections](connections.md#oracle-modes) describes both modes and the driver the site installs.

## Administration

**Settings → AppBlocks → Interactive Reports** lists public and private saved reports. Administrators can:

- Reassign a saved report's owner. Owner choices search backoffice Users and Members, and an account identity can be typed in.
- Delete user-created reports.
- Create public reports from configured reports, published report definitions or published report blocks.

Ownership and deletion act on saved-report metadata, including reports whose source page is protected or removed. Viewing report data and creating public reports follow the source's page and data access rules. Configured report documents are read-only. After a concurrent change, refresh before trying again.

### Report administrators

The default report policy recognizes two backoffice User Groups, and either is enough:

- **Administrators** (`admin`).
- **Interactive Reports Administrator** (`interactiveReportsAdministrator`), when the site creates it.

The package creates no User Groups and grants no section access. Manage groups, Settings section access and memberships in Umbraco's Users section. Member Groups never grant report administration. Replace `IReportAccessPolicy` to apply the application's own rules.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `AppBlocks:ReportAdministratorGroups` | none | Further backoffice User Group aliases that grant report administration |
| `AppBlocks:IncludeBackOfficeReports` | `false` | Turns on the optional backoffice reports. `AppBlocksOptions.IncludeBackOfficeReports`, through `Configure<AppBlocksOptions>`, does the same |
| `AppBlocks:InteractiveReports:PaperSizes` | `A4`, `Letter`, `Legal` | The paper sizes the Print dialog offers, in order |
| `InteractiveReport:SavedReports:Connection`, `DataSource` | `AppBlocksDb` | Where saved views are stored |
| `InteractiveReport:SavedReports:AutoCreate` | `true` | Whether the store creates its tables |
| `InteractiveReport:IdentityClaim` | none | The claim the owner key of a saved view is read through |

Configured report definitions stay in the `InteractiveReport` configuration section. The report definition APIs are in `PublishedReportCatalog`. The base package's `PublishedDataCatalog` supplies sources and lists of values.

## Building and testing

```powershell
dotnet pack src/AppBlocks.InteractiveReports -c Release -o artifacts/packages
dotnet run --project tests/AppBlocks.InteractiveReports.Tests -- -noLogo
```

The build restores `AurorisNet.InteractiveReports`, `AurorisNet.InteractiveReports.AspNetCore` and `AurorisNet.InteractiveReports.Client.FileDownload` from NuGet, browser assets included. [Developing AppBlocks](developing.md#dependencies-from-sibling-checkouts) describes building against a source checkout.

To run the print tests alone:

```powershell
dotnet run --project tests/AppBlocks.InteractiveReports.Tests -c Release -- -noLogo -class '*ReportPrintTests'
node --test tests/ui/report-print.browser.test.mjs
```
