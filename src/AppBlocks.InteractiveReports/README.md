# AppBlocks Interactive Reports

`AurorisNet.Umbraco.AppBlocks.InteractiveReports` adds an **Interactive Report** block to [AppBlocks](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks) on Umbraco. An editor picks a table or writes a SELECT, and readers search, filter, sort, chart, save views and download the result.

## Install

The package depends on `AurorisNet.Umbraco.AppBlocks`, which NuGet installs with it. It is an alpha and is not on nuget.org yet. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#install) describes how to reference it until it is.

```powershell
dotnet add package AurorisNet.Umbraco.AppBlocks.InteractiveReports --version 0.1.0-alpha.1
```

The package registers itself through an Umbraco composer. When the site restarts, it installs the report element type and data types beside AppBlocks' own.

## Use

1. Add **Interactive Report** to the allowed blocks of a Block Grid or Block List.
2. Add the block to a page and choose **Table** or **SQL query**. Tables come from the connections under **Settings → AppBlocks → Connections**.
3. Configure the block's starting view.

**A report on a page without Public Access is public, export included.** Protect the page when its rows are not for everyone.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `AppBlocks:ReportAdministratorGroups` | none | Backoffice User Group aliases that grant report administration, beside `admin` and `interactiveReportsAdministrator` |
| `AppBlocks:IncludeBackOfficeReports` | `false` | Turns on the optional backoffice reports |
| `AppBlocks:InteractiveReports:PaperSizes` | `A4`, `Letter`, `Legal` | The paper sizes the Print dialog offers |
| `InteractiveReport:SavedReports:Connection` | `AppBlocksDb` | The connection that stores saved views |

## Documentation

The [Interactive Reports guide](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/docs/interactive-reports.md) covers report access, links to form pages, saved views, downloads, printing and administration. The [repository README](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks#readme) lists the other guides.

## License

[MIT](https://github.com/auroris/AurorisNet.Umbraco.AppBlocks/blob/main/LICENSE)
