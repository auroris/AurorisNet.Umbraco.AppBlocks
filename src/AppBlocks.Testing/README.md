# AppBlocks Testing

`AurorisNet.Umbraco.AppBlocks.Testing` drives the [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) endpoints of a running site over HTTP, as the browser runtime does. A test can load, create, update and delete form records, upload attachments, save tabular rows and read reports. The package also has helpers for running Umbraco in a test process, checks for the scripts of an application database, and a listing of a site's content that a test compares with a file under source control.

## Install

Add the package to the site's test project. It depends on `AurorisNet.Umbraco.AppBlocks`, so use the version that matches the site's AppBlocks version. It is an alpha and is not on nuget.org yet. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#install) describes how to reference it until it is.

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.Testing --version 0.1.0-alpha.1
```

The package references no test framework. A call that cannot be made, such as a write before the load that supplies its antiforgery token, throws `TestSiteException` with what was observed. A schema check that fails throws `SchemaCheckException`.

## Clients

Each client takes an `HttpClient` that keeps cookies and the site's virtual path, which is empty at the root. The form, attachment and tabular clients also take the keys of the page and the block they drive.

| Client | Endpoint | Methods |
|---|---|---|
| `FormsClient` | `/api/forms/{page}/{form}` | `LoadAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`. Answers are a `FormResponse` with `RecordKey`, `RecordChecksum`, `Values` and `Errors` |
| `AttachmentsClient` | `/api/attachments/{page}/{block}` | `ListAsync`, `UploadAsync` with an `AttachmentFile`, `RenameAsync`, `DownloadAsync`, `RemoveAsync` |
| `TabularClient` | `/api/tabular/{page}/{block}` | `ListAsync`, and `SaveAsync` with a batch of `TabularChange` |
| `ReportsClient` | `/api/reports`, `/api/download` | `SchemaAsync`, `RowsAsync`, and `DownloadAsync` in a format such as `csv` |
| `PaperReportsClient` | `/api/paper-reports/{node}` | `DownloadAsync` of a Document node of the Documents package |

Load or list before the first write, and again wherever a browser would reload the page. A write carries the antiforgery token and the definition version that the last load returned. Page parameters, such as the record key a block follows, go in the `query` argument of each call.

```csharp
using AppBlocks.Testing;

var forms = new FormsClient(client, pathBase: "", pageKey, formKey);
var loaded = await forms.LoadAsync();
var created = await forms.CreateAsync(new { customer = "Ada", quantity = 2 });
var updated = await forms.UpdateAsync(created.RecordKey!, created.RecordChecksum, new { quantity = 3 });
```

## Running Umbraco in a test process

`UmbracoTestSupport` works with any way of starting the site:

- `UseSingleServer(services)` replaces server election, whose writes can race a SQLite startup import.
- `WaitForInstallAsync(services)` waits, after the host has started, for the unattended install or upgrade, and then for `RuntimeLevel.Run` and content routing.
- `ResetBackOfficePath()` and `ResetPublishedContentServices()` clear static state that Umbraco keeps from the first site, so a later site in the same process starts clean. Run one site at a time.

A test process that starts Umbraco must exclude its test runner's assemblies from Umbraco's type scan. For xUnit, add `xunit.` to `Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries`.

To start a whole AppHost site, use [AurorisNet.Umbraco.AppHost.Testing](https://github.com/auroris/AurorisNet.Umbraco.AppHost/blob/main/docs/getting-started.md#test-a-host), whose `TestSite` creates these clients for its own address and virtual path.

## Schema checks

`SchemaCheck` tests the scripts of an [application database](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/connections.md#application-databases) from a test, with no site running. The Connections guide describes [the layout and the rules](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/connections.md#a-schema-that-changes-between-releases) these checks hold the scripts to.

| Method | What it checks |
|---|---|
| `SchemaCheck.Sqlite(schema, listingPath, description)` | Ensures the schema on a new SQLite file at two starts. The second start must change nothing, and the result must equal the listing file |
| `SchemaCheck.SqlServer(schema, server, listingPath, description)` | The same on a new SQL Server database, which it creates on `server` and drops afterwards. Every constraint must also have a name |
| `SchemaCheck.Shipped(schemaRoot, listPath)` | Each run-once script in the list of shipped scripts is there under its name with its text unchanged, and every other script sorts after it |
| `SchemaCheck.SameScriptsInEveryDialect(schemaRoot)` | Every dialect folder has run-once scripts of the same file names |

A check that fails throws `SchemaCheckException`. For a listing, the message has the lines that differ, each under its table, view or rows heading.

```csharp
using AppBlocks.Testing;

[Fact]
public void Sqlite_scripts_make_the_listed_schema()
    => SchemaCheck.Sqlite(Notices.Schema(root), Path.Combine(root, "current", "sqlite.txt"), "Notices database");

[Fact]
public void Shipped_scripts_keep_their_text_and_name()
    => SchemaCheck.Shipped(root, Path.Combine(root, "shipped.txt"));
```

Pass the schema as the application registers it, with the steps that ensure its other schemas, so the test builds the database a site would.

**The listing** is a text file the application keeps under source control, such as `Schema/current/sqlite.txt`. It holds every table with its columns, constraints and indexes, the columns of every view, every trigger, and the rows a new database starts with. Run the tests with the environment variable `APPBLOCKS_SCHEMA_LISTING=write` to write it from the scripts. A later change to a script fails the test until the listing is written again, and the listing's diff shows a reviewer what the change does to the schema. `SchemaListing.Of(connection)` returns the same text for any open SQLite or SQL Server connection.

**The list of shipped scripts** is a text file such as `Schema/shipped.txt`, with one `dialect/migrations/file hash` to a line and `#` comments. Before the first release it holds comments only, and every script may still change. At each release, run the tests with `APPBLOCKS_SCHEMA_SHIPPED=write` to list every run-once script with the hash AppBlocks records for it, and commit the file with the release.

The SQL Server check needs an account that may create and drop a database on the server. It creates one of its own, so the databases already there stay as they are.

## Content checks

`ContentListing` writes the content of a running site as text, and `ContentCheck` compares that text with a file the application keeps under source control. An application that [installs pages from code](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/site-integration.md#pages-that-change-between-releases) uses the two to keep those pages in view: the file changes only when the installation does, and its diff shows a reviewer the change block by block.

```csharp
using var scope = site.CreateScope();
var listing = await ContentListing.OfAsync(scope.ServiceProvider, new ContentListingOptions {
    Include = node => node.ContentType.Alias is "webPage" or "website",
    Elements = [NoticeKey],
});
ContentCheck.Matches(listing, Path.Combine(projectFolder, "Pages", "current.txt"), "The pages a new Notices site holds.");
```

The listing holds, in the order of the content tree:

| Part | What is listed |
|---|---|
| Each document | Its path, key, type, template, published state and Public Access rule, then its property values. A draft that differs from the published value is listed beside it |
| A Block Grid, Block List or Single Block value | Its blocks in the order of the layout: each block's type alias, key and values, and the blocks in its areas indented under the area's alias. A block the layout leaves out or the value does not expose is marked |
| A value longer than one line, such as a script | Once, at the end, under the first eight characters of its SHA-256. That name stands for it wherever it occurs |
| Library elements | The ones `Elements` names, after the documents |

Node ids, dates and version numbers are left out, because they differ between two sites with the same content. Where a value refers to a document, content type, data type, template or listed element, the listing writes its name. It writes any other key as it is, so a block installed without a stable `id` shows as a difference between two installations.

Run the tests with the environment variable `APPBLOCKS_CONTENT_LISTING=write` to write the file. A check that fails throws `ContentCheckException` with the lines that differ, each under the document it belongs to.

Compare two sites with the one file: a new installation, and a copy of the previous release's site that the current build has updated. The first shows what the installation writes, and the second shows that the update steps reach the same pages.

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE)
