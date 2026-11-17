# AppBlocks Documents

`AurorisNet.Umbraco.AppBlocks.Documents` produces documents from [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) data sources and Razor templates on Umbraco. A document downloads as self-contained HTML, or as PDF through a [Pressroom](https://github.com/auroris/Pressroom) server. Other packages use its output service to send HTML to a PDF or printer destination.

## Install

The package depends on `AurorisNet.Umbraco.AppBlocks`, which NuGet installs with it. It is an alpha and is not on nuget.org yet. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#install) describes how to reference it until it is.

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.Documents --version 0.1.0-alpha.1
```

The package registers itself through an Umbraco composer. When the site restarts, it installs the **Document** (`paperReport`) document type and adds **Settings → AppBlocks → Documents**.

## Use

1. Under **Settings → AppBlocks → Documents**, add a Pressroom configuration for PDF output. Without one, documents download as HTML.
2. Create a **Document** content node, choose a published SQL data source and select a Razor template whose model is `AppBlocks.Documents.PaperReports.PaperReportModel`.
3. Link to the document at `/api/paper-reports/{key}`.

A document always needs a signed-in Member or backoffice User.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `AppBlocks:Documents:Pressroom:Url`, `ApiKey`, `TimeoutSeconds` | none, none, `65` | One PDF configuration, used until configurations are saved in the backoffice |
| `AppBlocks:Documents:AllowAllTemplates` | `true` | Whether the Document type is given every installed template |
| `AppBlocks:Documents:PrintStylesheet` | `/App_Plugins/AppBlocks.Documents/print.css` | The stylesheet placed first in every document. Empty places none |
| `AppBlocks:Documents:AllowedAssetHosts` | none | Hosts on private networks that asset embedding may fetch from |

## Documentation

The [Documents guide](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/documents.md) covers Pressroom configurations, authoring, asset embedding and the `IDocumentOutputService` contract. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#readme) lists the other guides.

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE)
