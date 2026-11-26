# AppBlocks Testing

HTTP test clients, Umbraco in-process test helpers, and database schema/content snapshot checks for [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) sites. Framework-agnostic (throws `TestSiteException`, `SchemaCheckException`, or `ContentCheckException`).

**Companion package:** Depends on `AurorisNet.Umbraco.AppBlocks`; match its version in your test project.

## Install

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.Testing --version 0.1.0-alpha.1
```

## What's included

- **Endpoint clients** (pass an `HttpClient` with cookies and optional `pathBase`; call load/list before writes to acquire antiforgery token + definition version):
  - `FormsClient` (`/api/forms/{page}/{form}`) — `LoadAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`
  - `AttachmentsClient` (`/api/attachments/{page}/{block}`) — `ListAsync`, `UploadAsync`, `RenameAsync`, `DownloadAsync`, `RemoveAsync`
  - `TabularClient` (`/api/tabular/{page}/{block}`) — `ListAsync`, `SaveAsync`
  - `ReportsClient` (`/api/reports`, `/api/download`) — `SchemaAsync`, `RowsAsync`, `DownloadAsync`
  - `PaperReportsClient` (`/api/paper-reports/{node}`) — `DownloadAsync`
- **`UmbracoTestSupport`**: `UseSingleServer(services)`, `WaitForInstallAsync(services)`, `ResetBackOfficePath()`, and `ResetPublishedContentServices()` for running Umbraco inside a test process. Exclude your test runner assemblies via `Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries` (e.g. `xunit.`).

### Schema checks

`SchemaCheck` (`Sqlite`, `SqlServer`, `Shipped`, `SameScriptsInEveryDialect`) validates application database scripts against committed snapshot/hash files (`APPBLOCKS_SCHEMA_LISTING=write`, `APPBLOCKS_SCHEMA_SHIPPED=write`).

### Content checks

`ContentListing` and `ContentCheck` compare rendered Umbraco document/block trees against a committed text file (`APPBLOCKS_CONTENT_LISTING=write`).

```csharp
using AppBlocks.Testing;

var forms = new FormsClient(client, pathBase: "", pageKey, formKey);
await forms.LoadAsync();
var created = await forms.CreateAsync(new { customer = "Ada", quantity = 2 });
var updated = await forms.UpdateAsync(created.RecordKey!, created.RecordChecksum, new { quantity = 3 });
```

See [Connections (application databases)](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/connections.md#application-databases), [Site integration](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/site-integration.md), and [MIT License](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE).
