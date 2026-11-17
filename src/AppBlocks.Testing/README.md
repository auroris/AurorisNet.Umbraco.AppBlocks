# AppBlocks Testing for Umbraco

`AurorisNet.Umbraco.AppBlocks.Testing` drives a running site's AppBlocks endpoints over HTTP the way the browser runtime does, so a test can load, create, update and delete form records, upload attachments, save tabular rows and read reports. It also holds the helpers a test process needs to run Umbraco: single-server registration, a wait for the unattended install, and resets for the static state Umbraco keeps between sites.

The package references no test framework. A call that cannot be made, such as a write before the load that supplies its antiforgery token, throws `TestSiteException` with what was observed. It is proven with xUnit v2 and v3.

## Clients

Each client takes an `HttpClient` that keeps cookies, the site's virtual path (empty at the root), and the keys of the page and block it drives.

| Client | Endpoint | What it does |
|---|---|---|
| `FormsClient` | `/api/forms/{page}/{form}` | `LoadAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`; answers are `FormResponse` with `RecordKey`, `RecordChecksum`, `Values` and `Errors` |
| `AttachmentsClient` | `/api/attachments/{page}/{block}` | `ListAsync`, `UploadAsync` with an `AttachmentFile`, `RenameAsync`, `DownloadAsync`, `RemoveAsync` |
| `TabularClient` | `/api/tabular/{page}/{block}` | `ListAsync`, and `SaveAsync` with a batch of `TabularChange` |
| `ReportsClient` | `/api/reports`, `/api/download` | `SchemaAsync`, `RowsAsync`, `DownloadAsync` in a format such as `csv` |
| `PaperReportsClient` | `/api/paper-reports/{node}` | `DownloadAsync` of a Paper report node (AppBlocks.Documents) |

Load or list before the first write, and again wherever a browser would reload the page: the writes carry the antiforgery token and definition version the last load returned. Page parameters, such as the record key a block follows, go in the `query` argument of every call.

```csharp
using AppBlocks.Testing;

var forms = new FormsClient(client, pathBase: "", pageKey, formKey);
var loaded = await forms.LoadAsync();
var created = await forms.CreateAsync(new { customer = "Ada", quantity = 2 });
var updated = await forms.UpdateAsync(created.RecordKey!, created.RecordChecksum, new { quantity = 3 });
```

## Running Umbraco in a test process

`UmbracoTestSupport` works with any way of booting the site:

- `UseSingleServer(services)` replaces server election, whose writes can race a SQLite startup import.
- `WaitForInstallAsync(services)` waits, after the host has started, for the unattended install or upgrade, then for `RuntimeLevel.Run` and content routing.
- `ResetBackOfficePath()` and `ResetPublishedContentServices()` clear the static state Umbraco caches from the first site, so a later site in the same process starts clean. Run one site at a time.

A test process that boots Umbraco needs its runner's assemblies excluded from Umbraco's type scan, as `Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries` with `xunit.` does for xUnit.

To boot a whole UmbracoAppHost site, use [AurorisNet.Umbraco.AppHost.Testing](https://github.com/auroris/UmbracoAppHost/blob/main/docs/getting-started.md#test-a-host), whose `TestSite` creates these clients for its own address and virtual path.

Use the version of this package that matches `AurorisNet.Umbraco.AppBlocks`; the package depends on that version.
