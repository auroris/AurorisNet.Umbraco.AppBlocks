# AppBlocks Documents

Generates self-contained HTML or PDF documents from [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) SQL data sources and Razor templates on Umbraco 18. PDF output and printing use a [Pressroom](https://github.com/auroris/AurorisNet.Pressroom) server.

**Companion package:** Automatically pulls in `AurorisNet.Umbraco.AppBlocks`.

## Install

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.Documents --version 0.1.0-alpha.1
```

Auto-registers via a composer, installs the **Document** (`paperReport`) content type, and adds **Settings → AppBlocks → Documents**.

1. Add a Pressroom configuration under **Settings → AppBlocks → Documents** for PDF output (without one, documents download as HTML).
2. Create a **Document** node, pick a SQL data source and a Razor template with model `AppBlocks.Documents.PaperReports.PaperReportModel`.
3. Serve at `/api/paper-reports/{key}` (requires a signed-in Member or backoffice User).

## Settings

| Setting | Default | Effect |
|---|---|---|
| `AppBlocks:Documents:Pressroom:Url`, `ApiKey`, `TimeoutSeconds` | none, none, `65` | Fallback Pressroom settings until configured in the backoffice. |
| `AppBlocks:Documents:AllowAllTemplates` | `true` | Allows every installed template on the Document type. |
| `AppBlocks:Documents:PrintStylesheet` | `/App_Plugins/AppBlocks.Documents/print.css` | Stylesheet prepended to every document (empty disables). |
| `AppBlocks:Documents:AllowedAssetHosts` | none | Private-network hosts permitted during asset embedding. |

See the [Documents guide](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/documents.md) and [MIT License](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE).
