using Microsoft.Data.Sqlite;
using AppBlocks.Databases;

namespace AppBlocks.Tests;

/// <summary>The schema listing and the script checks AppBlocks.Testing gives an application's tests.</summary>
public sealed class SchemaCheckTests
{
    private const string Tables = """
        -- Comments and layout stay out of the listing.
        CREATE TABLE OWNERS (ID INTEGER PRIMARY KEY, NAME TEXT NOT NULL);
        CREATE TABLE ITEMS (
            ID    INTEGER PRIMARY KEY,
            NAME  TEXT    NOT NULL UNIQUE,   -- what the item is called
            KIND  TEXT    NULL CHECK (KIND IN ('A','B')),
            OWNER INTEGER NULL REFERENCES OWNERS (ID)
        );
        CREATE INDEX IX_ITEMS_KIND ON ITEMS (KIND, NAME);
        INSERT INTO OWNERS (ID, NAME) VALUES (2, 'Second'), (1, 'It''s first');
        """;

    private const string Views = """
        DROP VIEW IF EXISTS ITEM_NAMES;
        CREATE VIEW ITEM_NAMES AS SELECT ID, NAME AS ITEM_NAME FROM ITEMS;
        DROP TRIGGER IF EXISTS ITEMS_KEPT;
        CREATE TRIGGER ITEMS_KEPT BEFORE DELETE ON ITEMS BEGIN SELECT RAISE(ABORT, 'Items stay.'); END;
        """;

    private const string Listing = """
        TABLE ITEMS
            ID INTEGER PRIMARY KEY
            NAME TEXT NOT NULL UNIQUE
            KIND TEXT NULL CHECK (KIND IN ('A', 'B'))
            OWNER INTEGER NULL REFERENCES OWNERS(ID)
            INDEX IX_ITEMS_KIND (KIND, NAME)

        TABLE OWNERS
            ID INTEGER PRIMARY KEY
            NAME TEXT NOT NULL

        VIEW ITEM_NAMES
            ID
            ITEM_NAME

        TRIGGER ITEMS_KEPT BEFORE DELETE ON ITEMS

        ROWS OWNERS
            ID=1, NAME='It''s first'
            ID=2, NAME='Second'


        """;

    /// <summary>Tables from a run-once script, then the views and the trigger that read them from a folder a step ensures afterwards.</summary>
    private static ApplicationSchema Schema(string root, string versionTable = "APP_SCHEMA")
    {
        var views = ApplicationSchema.FromDirectory(Path.Combine(root, "Views"), "items-views").RecordIn(versionTable);
        return ApplicationSchema.FromDirectory(root, "items").RecordIn(versionTable).Then((connection, _) => ApplicationDatabase.Ensure(connection, views));
    }

    private static string Scripts(string tables = Tables, string views = Views)
        => Folder(("sqlite/migrations/0001-v1.0.0-base.sql", tables), ("Views/sqlite/010-views.sql", views));

    [Fact]
    public void A_sqlite_listing_holds_tables_indexes_view_columns_triggers_and_rows_however_the_scripts_are_laid_out()
    {
        var root = Scripts();
        Assert.Equal(Listing.ReplaceLineEndings("\n"), ListSqlite(root));

        // The same statements on fewer lines, with other spacing and comments, list alike. The version table is left out under any name.
        var compact = Scripts("""
            CREATE TABLE OWNERS(ID INTEGER PRIMARY KEY,NAME TEXT NOT NULL);
            CREATE TABLE ITEMS(ID INTEGER PRIMARY KEY, NAME TEXT NOT NULL UNIQUE, /* the name */ KIND TEXT NULL CHECK(KIND IN('A', 'B')), OWNER INTEGER NULL REFERENCES OWNERS(ID));
            CREATE INDEX IX_ITEMS_KIND ON ITEMS(KIND,NAME);
            INSERT INTO OWNERS (ID, NAME) VALUES (1, 'It''s first');
            INSERT INTO OWNERS (ID, NAME) VALUES (2, 'Second');
            """);
        var file = Path.Combine(compact, "items.db");
        using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        connection.Open();
        ApplicationDatabase.Ensure(connection, Schema(compact, "ITEMS_SCHEMA"));
        Assert.Equal(Listing.ReplaceLineEndings("\n"), SchemaListing.Of(connection, "ITEMS_SCHEMA"));
    }

    [Fact]
    public void The_sqlite_check_writes_the_listing_then_compares_it_and_names_the_lines_a_changed_script_moved()
    {
        var root = Scripts();
        var listing = Path.Combine(root, "current", "sqlite.txt");
        Assert.Contains("is missing", Assert.Throws<SchemaCheckException>(() => SchemaCheck.Sqlite(Schema(root), listing, "Items database", write: false)).Message);

        SchemaCheck.Sqlite(Schema(root), listing, "Items database\nIts tables and views.", write: true);
        var written = File.ReadAllText(listing);
        Assert.StartsWith("Items database\nIts tables and views.\nA new SQLite database after its scripts have run", written);
        Assert.EndsWith("\n\n" + Listing.ReplaceLineEndings("\n"), written);
        Assert.DoesNotContain("\r", written);
        SchemaCheck.Sqlite(Schema(root), listing, "Items database\nIts tables and views.", write: false);
        // A checkout that converts line endings holds the same listing.
        File.WriteAllText(listing, written.Replace("\n", "\r\n"));
        SchemaCheck.Sqlite(Schema(root), listing, "Items database\nIts tables and views.", write: false);

        var changed = Scripts(Tables.Replace("NAME TEXT NOT NULL);", "NAME TEXT NOT NULL, NOTE TEXT NULL);").Replace("OWNER INTEGER NULL", "OWNER INTEGER NOT NULL"));
        var error = Assert.Throws<SchemaCheckException>(() => SchemaCheck.Sqlite(Schema(changed), listing, "Items database\nIts tables and views.", write: false));
        Assert.Contains("no longer make the schema", error.Message);
        Assert.Contains("  TABLE ITEMS\n-     OWNER INTEGER NULL REFERENCES OWNERS(ID)\n+     OWNER INTEGER NOT NULL REFERENCES OWNERS(ID)\n", error.Message);
        Assert.Contains("  TABLE OWNERS\n+     NOTE TEXT NULL\n", error.Message);
        Assert.Contains("ID=1, NAME='It''s first', NOTE=NULL", error.Message);
        Assert.Contains(SchemaCheck.ListingVariable + "=write", error.Message);
    }

    [Fact]
    public void The_sqlite_check_stops_on_a_script_that_changes_the_database_at_every_start()
    {
        var root = Scripts(views: Views + "\nINSERT INTO OWNERS (NAME) VALUES ('Again');");
        var error = Assert.Throws<SchemaCheckException>(() => SchemaCheck.Sqlite(Schema(root), Path.Combine(root, "sqlite.txt"), "Items database", write: true));
        Assert.Contains("A second start changed the schema", error.Message);
        Assert.Contains("+     ID=4, NAME='Again'", error.Message);
        Assert.False(File.Exists(Path.Combine(root, "sqlite.txt")));
    }

    [LocalDbFact]
    public void The_sql_server_check_lists_from_the_catalog_and_refuses_a_constraint_without_a_name()
    {
        const string server = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=90;TrustServerCertificate=true";
        const string tables = """
            CREATE TABLE dbo.OWNERS (ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_OWNERS PRIMARY KEY, NAME nvarchar(50) NOT NULL);
            GO
            CREATE TABLE dbo.ITEMS (
                ID       int           NOT NULL CONSTRAINT PK_ITEMS PRIMARY KEY,
                NAME     nvarchar(100) NOT NULL CONSTRAINT UQ_ITEMS_NAME UNIQUE,
                ACTIVE   bit           NOT NULL CONSTRAINT DF_ITEMS_ACTIVE DEFAULT 1,
                OWNER_ID int           NULL CONSTRAINT FK_ITEMS_OWNER REFERENCES dbo.OWNERS (ID),
                AMOUNT   decimal(9, 2) NULL CONSTRAINT CK_ITEMS_AMOUNT CHECK (AMOUNT >= 0),
                NOTES    nvarchar(max) NULL,
                ADDED    datetime2     NOT NULL
            );
            CREATE INDEX IX_ITEMS_OWNER ON dbo.ITEMS (OWNER_ID, NAME DESC) INCLUDE (ACTIVE);
            INSERT INTO dbo.OWNERS (NAME) VALUES (N'First');
            """;
        const string views = """
            CREATE OR ALTER VIEW dbo.ITEM_NAMES AS SELECT ID, NAME AS ITEM_NAME FROM dbo.ITEMS;
            GO
            CREATE OR ALTER TRIGGER dbo.ITEMS_KEPT ON dbo.ITEMS INSTEAD OF DELETE AS BEGIN SET NOCOUNT ON; END;
            """;
        var root = Folder(("sqlserver/migrations/0001-v1.0.0-base.sql", tables), ("Views/sqlserver/010-views.sql", views));
        var listing = Path.Combine(root, "current", "sqlserver.txt");
        SchemaCheck.SqlServer(Schema(root), server, listing, "Items database", write: true);
        var written = File.ReadAllText(listing);
        foreach (var line in new[] {
            "TABLE dbo.ITEMS\n    ID int NOT NULL\n    NAME nvarchar(100) NOT NULL\n    ACTIVE bit NOT NULL DEFAULT DF_ITEMS_ACTIVE ((1))\n    OWNER_ID int NULL\n    AMOUNT decimal(9,2) NULL\n    NOTES nvarchar(max) NULL\n    ADDED datetime2(7) NOT NULL\n",
            "    PRIMARY KEY PK_ITEMS CLUSTERED (ID)\n    UNIQUE UQ_ITEMS_NAME NONCLUSTERED (NAME)\n    FOREIGN KEY FK_ITEMS_OWNER (OWNER_ID) REFERENCES dbo.OWNERS (ID)\n    CHECK CK_ITEMS_AMOUNT ([AMOUNT]>=(0))\n",
            "    INDEX IX_ITEMS_OWNER NONCLUSTERED (OWNER_ID, NAME DESC) INCLUDE (ACTIVE)\n",
            "TABLE dbo.OWNERS\n    ID int IDENTITY(1,1) NOT NULL\n    NAME nvarchar(50) NOT NULL\n    PRIMARY KEY PK_OWNERS CLUSTERED (ID)\n",
            "VIEW dbo.ITEM_NAMES\n    ID int\n    ITEM_NAME nvarchar(100)\n",
            "TRIGGER ITEMS_KEPT INSTEAD OF DELETE ON dbo.ITEMS\n",
            "ROWS dbo.OWNERS\n    ID=1, NAME='First'\n" })
            Assert.Contains(line, written);
        Assert.DoesNotContain("APP_SCHEMA", written);
        SchemaCheck.SqlServer(Schema(root), server, listing, "Items database", write: false);

        // A constraint the server names itself has another name in every database, so no later script could drop it by name.
        var unnamed = Folder(("sqlserver/migrations/0001-v1.0.0-base.sql", tables.Replace("CONSTRAINT DF_ITEMS_ACTIVE ", "")), ("Views/sqlserver/010-views.sql", views));
        var error = Assert.Throws<SchemaCheckException>(() => SchemaCheck.SqlServer(Schema(unnamed), server, listing, "Items database", write: false));
        Assert.Contains("Give every constraint a name", error.Message);
        Assert.Contains("ACTIVE bit NOT NULL DEFAULT (unnamed) ((1))", error.Message);
    }

    [Fact]
    public void Shipped_scripts_keep_their_text_and_name_and_run_before_the_others()
    {
        var root = Folder(("sqlite/migrations/0001-v1.0.0-base.sql", "CREATE TABLE ITEMS (ID INTEGER PRIMARY KEY);"),
            ("sqlserver/migrations/0001-v1.0.0-base.sql", "CREATE TABLE dbo.ITEMS (ID int NOT NULL CONSTRAINT PK_ITEMS PRIMARY KEY);"),
            ("sqlite/010-views.sql", "DROP VIEW IF EXISTS ITEM_IDS; CREATE VIEW ITEM_IDS AS SELECT ID FROM ITEMS;"));
        var list = Path.Combine(root, "shipped.txt");
        var sqlite = Path.Combine(root, "sqlite", "migrations", "0001-v1.0.0-base.sql");
        Assert.Contains("is missing", Assert.Throws<SchemaCheckException>(() => SchemaCheck.Shipped(root, list, write: false)).Message);

        // Before the first release the list holds comments only, and every script may still change.
        File.WriteAllText(list, "# Nothing has shipped.\n");
        SchemaCheck.Shipped(root, list, write: false);
        File.AppendAllText(sqlite, "\n-- still changing");
        SchemaCheck.Shipped(root, list, write: false);

        // A release lists the run-once scripts with the hash AppBlocks records for each; ensure scripts are not listed.
        SchemaCheck.Shipped(root, list, write: true);
        var entries = File.ReadAllLines(list).Where(line => !line.StartsWith('#') && line.Length > 0).ToArray();
        Assert.Equal(["sqlite/migrations/0001-v1.0.0-base.sql " + ApplicationDatabase.MigrationHash(File.ReadAllBytes(sqlite)),
            "sqlserver/migrations/0001-v1.0.0-base.sql " + ApplicationDatabase.MigrationHash(File.ReadAllBytes(Path.Combine(root, "sqlserver", "migrations", "0001-v1.0.0-base.sql")))], entries);
        SchemaCheck.Shipped(root, list, write: false);
        // Line endings a checkout converts leave the script what it was, and a later script may be added and changed freely.
        File.WriteAllText(sqlite, File.ReadAllText(sqlite).Replace("\n", "\r\n"));
        File.WriteAllText(Path.Combine(root, "sqlite", "migrations", "0002-v1.0.1-notes.sql"), "ALTER TABLE ITEMS ADD COLUMN NOTE TEXT NULL;");
        SchemaCheck.Shipped(root, list, write: false);

        var original = File.ReadAllText(sqlite);
        File.AppendAllText(sqlite, "\n-- an edit after shipping");
        Assert.Contains("sqlite/migrations/0001-v1.0.0-base.sql has shipped and its text has changed", Assert.Throws<SchemaCheckException>(() => SchemaCheck.Shipped(root, list, write: false)).Message);
        File.WriteAllText(sqlite, original);

        File.Move(sqlite, Path.Combine(root, "sqlite", "migrations", "0001-v1.0.0-basis.sql"));
        Assert.Contains("has shipped and its file is gone", Assert.Throws<SchemaCheckException>(() => SchemaCheck.Shipped(root, list, write: false)).Message);
        File.Move(Path.Combine(root, "sqlite", "migrations", "0001-v1.0.0-basis.sql"), sqlite);

        File.WriteAllText(Path.Combine(root, "sqlite", "migrations", "0000-v0.9.9-early.sql"), "-- early");
        Assert.Contains("sqlite/migrations/0000-v0.9.9-early.sql sorts before 0001-v1.0.0-base.sql, which has shipped",
            Assert.Throws<SchemaCheckException>(() => SchemaCheck.Shipped(root, list, write: false)).Message);

        File.WriteAllText(list, "sqlite/0001-v1.0.0-base.sql ABC\n");
        Assert.Contains("an entry is <dialect>/migrations/<file> and a hash", Assert.Throws<SchemaCheckException>(() => SchemaCheck.Shipped(root, list, write: false)).Message);
    }

    [Fact]
    public void Base_and_current_match_for_the_first_release()
    {
        ReleaseCheck.CheckBaseline(MigrationRoot());
        ReleaseCheck.CheckSqlDialects(MigrationRoot(), "AppBlocks/Attachments/Schema", "sqlite", "sqlserver", "oracle", "postgresql");
    }

    private static string MigrationRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../Migrations"));

    private const string AttachmentTables = "AppBlocks attachment tables\nThe scripts in the selected installation snapshot under AppBlocks/Attachments/Schema, which the upload store ensures on its connection.";

    private static string AttachmentListing(string dialect)
        => Path.Combine(ReleaseCheck.Snapshot(MigrationRoot(), typeof(AppBlocks.Attachments.AttachmentStore).Assembly), "AppBlocks", "Attachments", "Schema", "current", dialect + ".txt");

    [Fact]
    public void The_attachment_scripts_make_the_listed_tables_on_sqlite()
        => SchemaCheck.Sqlite(AppBlocks.Attachments.AttachmentStore.Schema, AttachmentListing("sqlite"), AttachmentTables);

    [LocalDbFact]
    public void The_attachment_scripts_make_the_listed_tables_on_sql_server()
        => SchemaCheck.SqlServer(AppBlocks.Attachments.AttachmentStore.Schema, @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=90;TrustServerCertificate=true",
            AttachmentListing("sqlserver"), AttachmentTables);

    [Fact]
    public void Every_dialect_folder_holds_the_same_run_once_scripts()
    {
        var root = Folder(("sqlite/migrations/0001-base.sql", "-- a"), ("sqlserver/migrations/0001-base.sql", "-- a"), ("sqlite/010-views.sql", "-- views differ freely"),
            ("Views/sqlite/010-views.sql", "-- another schema's folder is not a dialect"));
        SchemaCheck.SameScriptsInEveryDialect(root);

        File.WriteAllText(Path.Combine(root, "sqlite", "migrations", "0002-notes.sql"), "-- b");
        var error = Assert.Throws<SchemaCheckException>(() => SchemaCheck.SameScriptsInEveryDialect(root));
        Assert.EndsWith("Missing: sqlserver/migrations/0002-notes.sql", error.Message);
    }

    private static string ListSqlite(string root)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "items.db")};Pooling=False");
        connection.Open();
        ApplicationDatabase.Ensure(connection, Schema(root));
        return SchemaListing.Of(connection);
    }

    /// <summary>A new temporary folder holding the given files, by path relative to it.</summary>
    private static string Folder(params (string Path, string Content)[] files)
    {
        var root = Directory.CreateTempSubdirectory("schema-check-test-").FullName;
        foreach (var (path, content) in files)
        {
            var target = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
        return root;
    }
}
