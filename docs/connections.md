# Connections

The databases AppBlocks reads and writes: the default application database, the upload store, connections added in the backoffice or in code, application databases with schema scripts, and the driver each database type needs.

## The AppBlocks database

**Settings → AppBlocks → Connections** lists **AppBlocks database** (`AppBlocksDb`) as the default connection for forms and reports. On a site whose Umbraco database is a SQLite file, it is the file `AppBlocks.sqlite.db` beside that file. AppBlocks creates it on first use, with foreign keys and write-ahead logging turned on.

Application data, saved report views and uploaded files share this database by default. The site creates its business tables. The saved-view and upload tables are created by their stores, or by hand where the database account may not create tables (see [Database setup](database-setup.md)).

Keep application data in this database. SQLite permits one writer per database, so form submissions and uploads in a file of their own never wait on Umbraco's saves. AppBlocks opens its own connections, separate from Umbraco's scope and transaction. `UmbracoDb` is available for a source that reads Umbraco's own data.

| Situation | Where the default database is |
|---|---|
| Umbraco uses a SQLite file | `AppBlocks.sqlite.db` beside Umbraco's file |
| Umbraco uses another database | `App_Data/AppBlocks.sqlite.db` under `AppBlocks:DataRoot`, or under the content root when that is not set |
| `ConnectionStrings:AppBlocksDb` is set | That connection, with the provider in `ConnectionStrings:AppBlocksDb_ProviderName` |

Back up the application database with Umbraco's database. Use a SQLite-aware backup while the site is running.

## The upload store

Uploaded files are stored in database tables. They go in `AppBlocksDb` unless `Attachments:Connection` names another connection:

```json
{
  "Attachments": {
    "Connection": "AttachmentsDb"
  }
}
```

| `Attachments:Connection` | Upload store |
|---|---|
| Empty, or `AppBlocksDb` | The AppBlocks database |
| `AttachmentsDb` | `Attachments.sqlite.db` beside Umbraco's SQLite file, or the connection in `ConnectionStrings:AttachmentsDb` with its `_ProviderName` |
| Another name | That registered, configured or saved connection |

The upload store runs on SQLite, SQL Server, PostgreSQL and Oracle 12c or later. A connection in **Oracle 11.2** mode cannot hold uploads, so select a separate store for them.

**The PostgreSQL and Oracle upload tables and statements have not been run against a live server.** Test uploads on those databases before relying on them.

## Saved report views

The reports companion stores saved views on `AppBlocksDb`. `InteractiveReport:SavedReports:Connection` or `InteractiveReport:SavedReports:DataSource` names another connection. See the [Interactive Reports guide](interactive-reports.md).

## Adding connections

### In the backoffice

Administrators add, edit and test SQL Server, SQLite, PostgreSQL, **Oracle 11.2** and **Oracle 12+** connections on the Connections screen.

- A connection string entered there is protected with ASP.NET Core Data Protection and stored in `App_Data/AppBlocks/connections.json`. It is kept out of blocks, the listing API and content exports.
- AppBlocks reads the file again whenever its size or modification time changes, so a save on another instance or a restored backup takes effect without a restart.
- Keep that file and the site's Data Protection keys together when backing up or moving an installation.
- A saved connection whose string this installation can no longer decrypt is listed with that problem. This happens after the Data Protection keys change or the file is copied from another server. Everything that opens the connection fails with the same message until the connection is saved again with its connection string.
- Several application instances need a shared, durable store for the file and the keys. Administration from several instances at once is not coordinated.

Changing a connection selects another database. It copies no tables or files.

### In configuration

The screen lists a `ConnectionStrings` entry when AppBlocks can open it: an entry whose `_ProviderName` names a database AppBlocks knows, or an entry with no provider name that reads as a SQL Server connection string. Other entries, such as a job store on MySQL, are left out of the list and the pickers.

Configured connections are read-only on the screen. A connection cannot be saved under a name that `ConnectionStrings` already has, because the configured one would always be opened.

### In code

Register a site's business connections with `AddAppBlocks`:

```csharp
using AppBlocks;

builder.AddAppBlocks(connections =>
    connections.AddConnection("Business", services => new SqlConnection(connectionString)));
```

Registered connections are read-only on the screen, and forms, data sources and the report source picker offer them like any other. The screen shows the database type from the `DatabaseDialect` passed to `AddConnection`, or from the type of the connection the factory creates. A custom connection type with no dialect shows as `unknown`. A saved connection with the same name takes precedence and stays editable.

A connection added directly to Interactive Reports' builder, through `AddAppBlocksInteractiveReports`, stays off this list. Configured reports, report definition nodes and forms can open it by name, and a report block refuses it.

## Application databases

An application with tables of its own registers its database and its schema together. AppBlocks brings the schema up to date the first time the process opens the connection, before the caller's first statement:

```csharp
builder.AddAppBlocks(connections => connections.AddApplicationDatabase("NoticesDb",
    ApplicationSchema.FromEmbedded(typeof(Notices).Assembly, "Schema")
        .Once("seed-categories", (connection, transaction, dialect) => SeedCategories(connection, transaction))));
```

The connection string is a connection saved under that name, or `ConnectionStrings:<name>` with its `_ProviderName`. Without either, the database is a SQLite file beside Umbraco's SQLite file, named `<name>.sqlite.db` or the registration's `defaultFile`. When Umbraco has no SQLite file, the file goes under `App_Data`. A file that already exists under `App_Data` stays in use when no file exists beside Umbraco's.

### Schema scripts

The schema is one folder per dialect: `sqlite`, `sqlserver`, `postgresql` and `oracle`. Read it from disk with `ApplicationSchema.FromDirectory`, or from embedded resources with `FromEmbedded`. The first open in a process works through it in this order:

1. **The version table.** `APP_SCHEMA`, with the columns `SCRIPT`, `HASH` and `APPLIED_AT`, is created when it is missing. `RecordIn("ORDERS_SCHEMA")` names another table.
2. **Ensure scripts.** The `*.sql` files of the dialect folder run in file-name order at every process start, each in its own transaction. Write them to be safe to repeat: `CREATE TABLE IF NOT EXISTS`, or a check before each change. On SQLite, an `ALTER TABLE ... ADD` whose column already exists counts as applied.
3. **Run-once scripts.** The `*.sql` files of the dialect folder's `migrations` folder run in file-name order, each once per database. Each runs in one transaction with its row, recorded as `<schema>/migrations/<file>` with the script's SHA-256 hash. Use them for changes that cannot be repeated, such as moving data or renaming a column.
4. **C# steps.** They run in the order they were added. `Then(step)` runs at every start. `Once(name, step)` runs once per database, recorded as `<schema>/once/<name>`, inside the transaction that records it. Its commands set `command.Transaction` to the transaction the step receives.

### Schema names

Every row starts with the schema's `Name`, so two application databases whose connections reach one physical database keep separate rows in one version table. `FromDirectory` names a schema after its folder, such as `Schema`. `FromEmbedded` names it after the assembly and the folder, such as `Notices:Schema`. Pass `name` to either to choose the name, and keep it once a database has rows under it.

### Changed and failed scripts

**A run-once script that has run must stay as it ran.** When its text no longer matches its recorded hash, AppBlocks throws an `ApplicationSchemaException` that names the script, and every open of the connection fails until someone resolves it:

- Restore the script and put the change in a new file, or
- check the database by hand and delete the script's row in the version table, so the script runs again.

The hash ignores a byte order mark and reads CRLF line endings as LF, so a checkout that converts line endings produces the same hash.

**A database that another build has taken further stays as it is.** Run-once scripts are numbered in the order they are written. When the database has run a script that the build does not have, and that script sorts after the build's last one, another build of the application changed the database, usually a newer one installed before a rollback. AppBlocks runs nothing against it and throws an `ApplicationSchemaException` whose `DatabaseFromAnotherBuild` is true, naming the script. Install the build the database belongs to, or restore the database backup taken before that build was installed.

A recorded script that sorts before the build's last one is a script the application has retired, and is passed over. So is every recorded script when the schema has no run-once scripts to compare with.

A script or step that fails throws an `ApplicationSchemaException` that names it. It leaves no row and runs again on the next open.

Each run-once script records its row before its statements run. A second process that starts at the same moment waits for the first and then skips the script.

**Oracle commits around DDL, so on Oracle the row is written after the statements.** A run-once script that fails partway may leave part of its work behind, and two processes that start together against an empty Oracle schema may both try it. After deploying a run-once script to Oracle, start one process first.

### A schema that changes between releases

Run-once scripts carry a schema from one release to the next. A new database and an updated one run the same scripts in the same order, so both reach the same tables:

| Script | Holds |
|---|---|
| `migrations/0001-v1.0.0-base.sql` | The tables, indexes and starting rows of the first release |
| `migrations/0002-v1.0.1-due-date.sql` | One later change. Name each script with the next number, the release that ships it and the change |
| Ensure scripts | Views and triggers, written to replace what is there: `CREATE OR ALTER` on SQL Server, `DROP ... IF EXISTS` and `CREATE` on SQLite |

Scripts run in ordinal file-name order, which the leading number fixes. A release number alone would sort `1.0.10` before `1.0.9`.

Keep tables and their rows in run-once scripts once a schema will change. An ensure script that creates a table runs at every start against each later shape of that table, while a new database still starts from the shape it creates, so every later script has to work from both. A row an ensure script inserts when missing also comes back after a run-once script deletes it.

A schema's ensure scripts run before its run-once scripts. Views and triggers that read tables from run-once scripts therefore go in a schema folder of their own, which a step ensures afterwards:

```csharp
var root = Path.Combine(builder.Environment.ContentRootPath, "Schema");
var views = ApplicationSchema.FromDirectory(Path.Combine(root, "Views"));
builder.AddAppBlocks(connections => connections.AddApplicationDatabase("NoticesDb",
    ApplicationSchema.FromDirectory(root).Then((connection, _) => ApplicationDatabase.Ensure(connection, views))));
```

Three rules keep the scripts dependable after a release:

- **A shipped script keeps its text and its file name.** A changed script stops the ensure, as [Changed and failed scripts](#changed-and-failed-scripts) describes. A renamed script has no row, so it runs a second time.
- **A script assumes the schema that the script before it left.** Leave `IF NOT EXISTS` checks out of run-once scripts. A database in another state then fails the script. On SQLite, SQL Server and PostgreSQL the script's transaction rolls back and the data stays as it was.
- **Every SQL Server constraint has a name.** A later script drops or changes a constraint by its name, and a name SQL Server makes up differs in every database.

Scripts go forward only. To return to an earlier release, restore the database backup taken before the update: the earlier build stops when it meets a database that a later one changed.

`AppBlocks.Testing` checks these rules in the application's tests and keeps a listing of the resulting schema under source control. See [Schema checks](../src/AppBlocks.Testing/README.md#schema-checks).

## Database drivers

AppBlocks ships no database driver. Umbraco brings `Microsoft.Data.Sqlite` and `Microsoft.Data.SqlClient` into every site. For the other databases, the site installs the driver package and AppBlocks finds it by name at run time.

| Database | Package the site installs |
| --- | --- |
| SQLite | None. It comes with Umbraco |
| SQL Server | None. It comes with Umbraco |
| PostgreSQL | `Npgsql` |
| Oracle 11.2 and Oracle 12+ | `Oracle.ManagedDataAccess.Core` |

Without the package, saving or testing a PostgreSQL or Oracle connection fails with a message that names it. A configured or registered connection of that type fails the same way when it is first opened. Install the package and restart the site.

**The Oracle driver needs one more setting.** Umbraco's startup scan loads every assembly beside the site and the assemblies those reference. `Oracle.ManagedDataAccess` references optional cloud assemblies that its package leaves out, so the site cannot start until the scan skips the driver:

```json
{
  "Umbraco": {
    "CMS": {
      "TypeFinder": {
        "AdditionalAssemblyExclusionEntries": ["Oracle.ManagedDataAccess"]
      }
    }
  }
}
```

AppBlocks still finds the driver by name when a connection opens. `Npgsql` needs no such setting.

## Oracle modes

**Oracle 11.2** and **Oracle 12+** are SQL compatibility modes shared with Interactive Reports. The mode controls the SQL that AppBlocks generates for the connection. Both use the site's `Oracle.ManagedDataAccess.Core` driver.

| Mode | `_ProviderName` for a configured connection | Dialect for `AddConnection` |
|---|---|---|
| Oracle 11.2 | `oracle11g` | `DatabaseDialect.Oracle11g` |
| Oracle 12+ | `oracle`, `Oracle.ManagedDataAccess.Client` or `Oracle.ManagedDataAccess.Core` | `DatabaseDialect.Oracle` |

- Oracle 11.2 mode uses 11.2-compatible metadata and row limits. Generated keys need an explicit sequence in the data source's **Key sequences**, such as `{"ID":"ORDERS_SEQ"}`.
- Oracle 12+ mode also discovers identity columns. On first use it detects a server older than 12 and switches to Oracle 11.2 mode.
- The upload store needs Oracle 12+ mode, or a separate store.

AppBlocks' tests use the `Oracle.ManagedDataAccess.Core` version that `tests/AppBlocks.Tests/AppBlocks.Tests.csproj` references. Oracle's [driver certification](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/InstallSystemRequirements.html) lists Database 19c and later, so verify connectivity to an older server yourself, whichever mode is selected.
