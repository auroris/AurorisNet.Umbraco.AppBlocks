# AppBlocks Interactive Reports

Adds an **Interactive Report** block to [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) on Umbraco 18 so editors can pick a table or `SELECT` query and users can search, filter, sort, chart, save views, and export results.

**Companion package:** Automatically pulls in `AurorisNet.Umbraco.AppBlocks`.

## Install

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.InteractiveReports --version 0.1.0-alpha.1
```

Auto-registers via a composer and installs the **Interactive Report** element type. Allow it on your Block Grid or Block List, add it to a page, and choose a table or SQL query.

> **Security:** A report on a page without Public Access is publicly readable and exportable. Protect the page if its rows are not public.

## Settings

| Setting | Default | Effect |
|---|---|---|
| `AppBlocks:ReportAdministratorGroups` | none | Extra backoffice user group aliases granted report admin rights (besides `admin` and `interactiveReportsAdministrator`). |
| `AppBlocks:IncludeBackOfficeReports` | `false` | Enables optional built-in backoffice reports. |
| `AppBlocks:InteractiveReports:PaperSizes` | `A4`, `Letter`, `Legal` | Paper sizes offered in the Print dialog. |
| `InteractiveReport:SavedReports:Connection` | `AppBlocksDb` | Database connection storing saved views. |

See the [Interactive Reports guide](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/interactive-reports.md) and [MIT License](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE).
