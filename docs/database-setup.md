# Database setup

Forms and reports read the site's own tables and need no tables from AppBlocks. Two features keep tables of their own and create them on first use when the database account may. This page lists those tables and the two ways to set them up when the account may not create them.

**Settings → AppBlocks → Database setup** shows administrators each feature's connection, whether its tables exist, and both ways to set the feature up for that database type. Opening the screen creates nothing, and the screen shows no connection string.

## The tables

| Feature | Tables | Connection | Without the tables |
| --- | --- | --- | --- |
| File uploads | `APP_ATTACHMENT`, `APP_ATTACHMENT_LINK` | `Attachments:Connection`, by default `AppBlocksDb` | Attachments blocks show a notice in place of the files, and nothing can be uploaded. The form around them keeps working. |
| Saved report views, in the Interactive Reports package | `IR_SAVED_REPORTS`, and `IR_ADMINISTRATORS` for the saved report administration page | `InteractiveReport:SavedReports`, by default `AppBlocksDb` | Saving views is hidden and each report opens on its starting view. Reports still load, query and export. |

While a feature's tables are missing:

- Everything else keeps working, and visitors see no error.
- An administrator who is signed in to the backoffice gets a link to the setup screen on the block.
- The reason is logged once, as a warning that says what to do.

A companion that writes its own link to the screen uses `DatabaseSetupGuide.ScreenPath`, under the site's path base, or `DatabaseSetupGuide.ScreenPathUnder(backOfficePath)` for another backoffice path.

Each feature needs the driver for its database first. PostgreSQL needs the `Npgsql` package and Oracle needs `Oracle.ManagedDataAccess.Core`, as [Connections](connections.md#database-drivers) describes.

## Setting a feature up

Either way takes effect on the next request, with no restart.

### Grant the permission

With the permission, AppBlocks creates the tables itself. The screen shows the statements with the account's name filled in.

| Database | What the account needs |
|---|---|
| SQLite | SQLite has no accounts. The account the site runs under needs write access to the database file and its folder. |
| SQL Server | `CREATE TABLE`, and `ALTER`, `SELECT`, `INSERT`, `UPDATE`, `DELETE` and `REFERENCES` on the account's default schema, where its tables go. That schema is `dbo` unless the database user has another, and the screen names it. |
| PostgreSQL | `USAGE` and `CREATE` on the account's current schema, which the screen names. The account owns the tables it creates. |
| Oracle | `CREATE TABLE` and `CREATE SEQUENCE`, `CREATE TRIGGER` as well in Oracle 11.2 mode, and a quota on the account's tablespace. The account owns the tables it creates. |

On SQL Server the grant covers every table in that schema, because tables in a schema belong to the schema's owner. To limit the account to these tables, run the script.

### Run the script

The screen shows the script and offers it as a download. Run it as an account that may create tables. The account the site signs in as then needs no right to create anything. The script is built from the statements AppBlocks itself runs, and it is safe to run more than once.

| Database | How to run it |
|---|---|
| SQL Server | Run it as the database's owner. The tables go in `dbo`, where SQL Server looks after an account's default schema, so an account with a schema of its own still finds them. The script ends by granting the site's account read and write on the tables. |
| PostgreSQL | An owner first sets `search_path` to the account's schema, as the script's header says. The script ends by granting the site's account read and write on the tables. |
| Oracle | Connect as the site's account, or as a DBA after `ALTER SESSION SET CURRENT_SCHEMA = <account>`, so that the tables are in the account's schema. |

## Limits

File uploads run on SQLite, SQL Server, PostgreSQL and Oracle 12c or later. On a connection in Oracle 11.2 mode, the Attachments block shows the same notice and nothing is created: select a separate store with `Attachments:Connection`.

**The PostgreSQL and Oracle scripts have not been run against a live server**, as [Connections](connections.md#the-upload-store) notes.

## The APP_SCHEMA table

`APP_SCHEMA` is a bookkeeping table that AppBlocks' schema code writes when it creates tables. The features on this page work without it, and the script leaves it out. An account that may create tables gets one. An [application database](connections.md#application-databases) of your own reads it to know which run-once scripts have run.
