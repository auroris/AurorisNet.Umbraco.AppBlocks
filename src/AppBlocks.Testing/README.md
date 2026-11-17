# AppBlocks Testing

`AurorisNet.Umbraco.AppBlocks.Testing` drives the [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) endpoints of a running site over HTTP, as the browser runtime does. A test can load, create, update and delete form records, upload attachments, save tabular rows and read reports. The package also has helpers for running Umbraco in a test process.

## Install

Add the package to the site's test project. It depends on `AurorisNet.Umbraco.AppBlocks`, so use the version that matches the site's AppBlocks version. It is an alpha and is not on nuget.org yet. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#install) describes how to reference it until it is.

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.Testing --version 0.1.0-alpha.1
```

The package references no test framework. A call that cannot be made, such as a write before the load that supplies its antiforgery token, throws `TestSiteException` with what was observed.

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

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE)
