using System.Data.Common;
using System.Globalization;
using System.Text;
using AppBlocks.Databases;

namespace AppBlocks.Testing;

/// <summary>
/// A database as text: each table with its columns, constraints and indexes, the columns of each view, each trigger, and the
/// rows the tables hold. Two databases with equal listings have the same schema, so a review reads the listing and
/// <see cref="SchemaCheck"/> compares it. SQLite and SQL Server are listed.
/// </summary>
/// <remarks>
/// A SQLite table is listed as the definitions SQLite keeps, one column or constraint to a line, with comments removed and
/// spacing made uniform, so two scripts that differ only in layout list alike. A SQL Server table is listed from the catalog
/// views. A view is listed as its columns and a trigger as its name, moment and table: their text is in the scripts, which
/// make them again at every start. Sections and rows are in ordinal order, and lines end with a line feed.
/// </remarks>
public static class SchemaListing
{
    private const string Indent = "    ";

    /// <summary>What a SQL Server constraint is listed as when the server named it itself: such a name differs in every database.</summary>
    public const string Unnamed = "(unnamed)";

    /// <summary>The listing of the database an open connection reaches.</summary>
    /// <param name="connection">An open SQLite or SQL Server connection.</param>
    /// <param name="versionTable">
    /// The table that records the scripts that ran, which is left out: <c>APP_SCHEMA</c>, or the schema's
    /// <see cref="ApplicationSchema.Table"/>.
    /// </param>
    /// <exception cref="NotSupportedException">The connection is to another kind of database.</exception>
    public static string Of(DbConnection connection, string versionTable = "APP_SCHEMA")
    {
        ArgumentNullException.ThrowIfNull(connection);
        // A version table may be named with its schema or owner; the listing leaves out the table of that name.
        var version = versionTable[(versionTable.LastIndexOf('.') + 1)..];
        return ApplicationDatabase.Dialect(connection) switch
        {
            "sqlite" => Sqlite(connection, version),
            "sqlserver" => SqlServer(connection, version),
            var dialect => throw new NotSupportedException($"A schema listing covers SQLite and SQL Server; this connection is {dialect}."),
        };
    }

    // ---- SQLite: the definitions SQLite keeps, with comments removed and spacing made uniform ----

    private static string Sqlite(DbConnection connection, string versionTable)
    {
        var listing = new StringBuilder();
        var tables = Query(connection, "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> @version",
            ("@version", versionTable)).Select(row => (Name: (string)row[0]!, Sql: (string)row[1]!)).OrderBy(table => table.Name, StringComparer.Ordinal).ToArray();
        foreach (var (name, sql) in tables)
        {
            listing.Append("TABLE ").Append(name).Append('\n');
            var (elements, options) = TableElements(sql);
            foreach (var element in elements) listing.Append(Indent).Append(element).Append('\n');
            if (options.Length > 0) listing.Append(Indent).Append(options).Append('\n');
            // Indexes a CREATE INDEX made; those behind PRIMARY KEY and UNIQUE are in the definition above.
            var indexes = Query(connection, "SELECT name, \"unique\", partial FROM pragma_index_list(@table) WHERE origin = 'c'", ("@table", name))
                .Select(row => SqliteIndex(connection, (string)row[0]!, Flag(row[1]), Flag(row[2])));
            foreach (var index in indexes.Order(StringComparer.Ordinal)) listing.Append(Indent).Append(index).Append('\n');
            listing.Append('\n');
        }
        foreach (var view in Query(connection, "SELECT name FROM sqlite_master WHERE type = 'view'").Select(row => (string)row[0]!).Order(StringComparer.Ordinal))
        {
            listing.Append("VIEW ").Append(view).Append('\n');
            foreach (var column in Query(connection, "SELECT name FROM pragma_table_xinfo(@view) ORDER BY cid", ("@view", view)))
                listing.Append(Indent).Append((string)column[0]!).Append('\n');
            listing.Append('\n');
        }
        var triggers = Query(connection, "SELECT sql FROM sqlite_master WHERE type = 'trigger'").Select(row => SqliteTrigger((string)row[0]!)).Order(StringComparer.Ordinal).ToArray();
        foreach (var trigger in triggers) listing.Append("TRIGGER ").Append(trigger).Append('\n');
        if (triggers.Length > 0) listing.Append('\n');
        foreach (var (name, _) in tables) Rows(connection, listing, name, "\"" + name.Replace("\"", "\"\"") + "\"");
        return listing.ToString();
    }

    private static string SqliteIndex(DbConnection connection, string name, bool unique, bool partial)
    {
        var columns = Query(connection, "SELECT name, \"desc\" FROM pragma_index_xinfo(@index) WHERE \"key\" = 1 ORDER BY seqno", ("@index", name))
            .Select(row => (row[0] as string ?? "(expression)") + (Flag(row[1]) ? " DESC" : ""));
        var text = (unique ? "UNIQUE INDEX " : "INDEX ") + name + " (" + string.Join(", ", columns) + ")";
        if (!partial) return text;
        var sql = Tokens((string)Query(connection, "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @index", ("@index", name))[0][0]!);
        var where = sql.FindIndex(token => token.Equals("WHERE", StringComparison.OrdinalIgnoreCase));
        return where < 0 ? text : text + " " + Render(sql.Skip(where).ToList());
    }

    /// <summary>A trigger as its name, its moment and its table: the words between TRIGGER and the table's name.</summary>
    private static string SqliteTrigger(string sql)
    {
        var tokens = Tokens(sql);
        var start = tokens.FindIndex(token => token.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase)) + 1;
        if (tokens.Count > start + 2 && tokens[start].Equals("IF", StringComparison.OrdinalIgnoreCase)) start += 3;
        var on = tokens.FindIndex(start, token => token.Equals("ON", StringComparison.OrdinalIgnoreCase));
        return Render(tokens.GetRange(start, on < 0 ? tokens.Count - start : Math.Min(on + 2, tokens.Count) - start));
    }

    /// <summary>The columns and constraints of a CREATE TABLE statement, one each, and whatever follows the closing parenthesis.</summary>
    private static (List<string> Elements, string Options) TableElements(string sql)
    {
        var tokens = Tokens(sql);
        var open = tokens.IndexOf("(");
        // A table made from a query has no column list; its statement is listed whole.
        if (open < 0) return ([Render(tokens)], "");
        var elements = new List<string>();
        var current = new List<string>();
        var depth = 0;
        var index = open;
        for (; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token == "(" && depth++ == 0) continue;
            if (token == ")" && --depth == 0) break;
            if (token == "," && depth == 1) { elements.Add(Render(current)); current = []; continue; }
            current.Add(token);
        }
        if (current.Count > 0) elements.Add(Render(current));
        return (elements, Render(tokens.Skip(index + 1).ToList()));
    }

    /// <summary>A statement's words, literals and punctuation, without its comments and white space.</summary>
    private static List<string> Tokens(string sql)
    {
        var tokens = new List<string>();
        for (var i = 0; i < sql.Length;)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                continue;
            }
            if (c is '\'' or '"' or '`' or '[')
            {
                // A literal or a quoted name, kept as written; a doubled quote inside one is part of it.
                var close = c == '[' ? ']' : c;
                var j = i + 1;
                for (; j < sql.Length; j++)
                {
                    if (sql[j] != close) continue;
                    if (close != ']' && j + 1 < sql.Length && sql[j + 1] == close) { j++; continue; }
                    break;
                }
                tokens.Add(sql[i..Math.Min(j + 1, sql.Length)]);
                i = j + 1;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                var j = i;
                while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] is '_' or '$' or '.')) j++;
                tokens.Add(sql[i..j]);
                i = j;
                continue;
            }
            if (i + 1 < sql.Length && sql.Substring(i, 2) is "<=" or ">=" or "<>" or "!=" or "==" or "||")
            {
                tokens.Add(sql.Substring(i, 2));
                i += 2;
                continue;
            }
            tokens.Add(c.ToString());
            i++;
        }
        return tokens;
    }

    /// <summary>Words that a parenthesis follows after a space; after any other word it follows directly, as in a function call.</summary>
    private static readonly HashSet<string> BeforeParenthesis = new(StringComparer.OrdinalIgnoreCase)
        { "CHECK", "IN", "KEY", "UNIQUE", "ON", "AND", "OR", "NOT", "AS", "DEFAULT", "VALUES", "EXISTS", "WHEN", "THEN", "ELSE", "WHERE", "SELECT", "FROM" };

    /// <summary>Tokens as one line with the same spacing however the statement was written.</summary>
    private static string Render(IReadOnlyList<string> tokens)
    {
        var text = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (i > 0)
            {
                var previous = tokens[i - 1];
                var word = char.IsLetterOrDigit(previous[0]) || previous[0] is '_' or '"' or '`' or '[' or '\'';
                var space = token switch
                {
                    ")" or "," => false,
                    "(" => previous != "(" && (!word || BeforeParenthesis.Contains(previous)),
                    _ => previous != "(",
                };
                if (space) text.Append(' ');
            }
            text.Append(token);
        }
        return text.ToString();
    }

    // ---- SQL Server: the catalog views ----

    private static string SqlServer(DbConnection connection, string versionTable)
    {
        // Each table's lines: rank 0 holds its columns in their order, the later ranks its constraints and indexes, sorted by text.
        var tables = new SortedDictionary<string, List<(int Rank, int Order, string Text)>>(StringComparer.Ordinal);
        List<(int Rank, int Order, string Text)> Lines(object? schema, object? table)
            => tables.TryGetValue(schema + "." + table, out var lines) ? lines : tables[schema + "." + table] = [];
        static string Named(object? name, object? systemNamed) => Flag(systemNamed) ? Unnamed : (string)name!;
        static int Number(object? value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

        var columnOrder = 0;
        foreach (var row in Query(connection, """
            SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
                   CONVERT(bigint, ic.seed_value), CONVERT(bigint, ic.increment_value), cc.definition, dc.name, dc.definition, dc.is_system_named
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE t.is_ms_shipped = 0 AND t.name <> @version
            ORDER BY s.name, t.name, c.column_id
            """, ("@version", versionTable)))
        {
            var text = new StringBuilder((string)row[2]!);
            if (row[11] is string computed) text.Append(" AS ").Append(computed);
            else
            {
                text.Append(' ').Append(SqlServerType((string)row[3]!, Number(row[4]), Number(row[5]), Number(row[6])));
                if (Flag(row[8])) text.Append($" IDENTITY({row[9]},{row[10]})");
                text.Append(Flag(row[7]) ? " NULL" : " NOT NULL");
                if (row[13] is string value) text.Append(" DEFAULT ").Append(Named(row[12], row[14])).Append(' ').Append(value);
            }
            Lines(row[0], row[1]).Add((0, columnOrder++, text.ToString()));
        }

        foreach (var index in Query(connection, """
            SELECT s.name, t.name, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition,
                   ISNULL(kc.is_system_named, 0), ic.is_included_column, ic.is_descending_key, c.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.indexes i ON i.object_id = t.object_id AND i.type > 0
            LEFT JOIN sys.key_constraints kc ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE t.is_ms_shipped = 0 AND t.name <> @version
            ORDER BY s.name, t.name, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """, ("@version", versionTable)).GroupBy(row => (Schema: row[0], Table: row[1], Id: row[2])))
        {
            var first = index.First();
            var keys = string.Join(", ", index.Where(row => !Flag(row[10])).Select(row => row[12] + (Flag(row[11]) ? " DESC" : "")));
            var included = string.Join(", ", index.Where(row => Flag(row[10])).Select(row => (string)row[12]!));
            var (rank, start) = Flag(first[6]) ? (1, "PRIMARY KEY " + Named(first[3], first[9]))
                : Flag(first[7]) ? (2, "UNIQUE " + Named(first[3], first[9]))
                : (5, (Flag(first[5]) ? "UNIQUE INDEX " : "INDEX ") + first[3]);
            var text = $"{start} {first[4]} ({keys})" + (included.Length > 0 ? $" INCLUDE ({included})" : "") + (first[8] is string filter ? " WHERE " + filter : "");
            Lines(first[0], first[1]).Add((rank, 0, text));
        }

        foreach (var key in Query(connection, """
            SELECT s.name, t.name, fk.object_id, fk.name, fk.is_system_named, fk.delete_referential_action_desc, fk.update_referential_action_desc,
                   pc.name, rs.name, rt.name, rc.name
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON t.object_id = fk.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE t.is_ms_shipped = 0 AND t.name <> @version
            ORDER BY s.name, t.name, fk.object_id, fkc.constraint_column_id
            """, ("@version", versionTable)).GroupBy(row => (Schema: row[0], Table: row[1], Id: row[2])))
        {
            var first = key.First();
            var text = $"FOREIGN KEY {Named(first[3], first[4])} ({string.Join(", ", key.Select(row => row[7]))}) REFERENCES {first[8]}.{first[9]} ({string.Join(", ", key.Select(row => row[10]))})";
            if ((string)first[5]! != "NO_ACTION") text += " ON DELETE " + ((string)first[5]!).Replace('_', ' ');
            if ((string)first[6]! != "NO_ACTION") text += " ON UPDATE " + ((string)first[6]!).Replace('_', ' ');
            Lines(first[0], first[1]).Add((3, 0, text));
        }

        foreach (var check in Query(connection, """
            SELECT s.name, t.name, ck.name, ck.is_system_named, ck.definition
            FROM sys.check_constraints ck
            JOIN sys.tables t ON t.object_id = ck.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND t.name <> @version
            """, ("@version", versionTable)))
            Lines(check[0], check[1]).Add((4, 0, $"CHECK {Named(check[2], check[3])} {check[4]}"));

        var listing = new StringBuilder();
        foreach (var (table, lines) in tables)
        {
            listing.Append("TABLE ").Append(table).Append('\n');
            foreach (var line in lines.OrderBy(line => line.Rank).ThenBy(line => line.Order).ThenBy(line => line.Text, StringComparer.Ordinal))
                listing.Append(Indent).Append(line.Text).Append('\n');
            listing.Append('\n');
        }

        foreach (var view in Query(connection, """
            SELECT s.name, v.name, c.name, ty.name, c.max_length, c.precision, c.scale
            FROM sys.views v
            JOIN sys.schemas s ON s.schema_id = v.schema_id
            JOIN sys.columns c ON c.object_id = v.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE v.is_ms_shipped = 0
            ORDER BY s.name, v.name, c.column_id
            """).GroupBy(row => row[0] + "." + row[1]).OrderBy(columns => columns.Key, StringComparer.Ordinal))
        {
            listing.Append("VIEW ").Append(view.Key).Append('\n');
            foreach (var row in view)
                listing.Append(Indent).Append(row[2]).Append(' ').Append(SqlServerType((string)row[3]!, Number(row[4]), Number(row[5]), Number(row[6]))).Append('\n');
            listing.Append('\n');
        }

        var triggers = Query(connection, """
            SELECT tr.name, tr.is_instead_of_trigger, te.type_desc, s.name, o.name
            FROM sys.triggers tr
            JOIN sys.objects o ON o.object_id = tr.parent_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.trigger_events te ON te.object_id = tr.object_id
            WHERE tr.is_ms_shipped = 0 AND tr.parent_class = 1
            """).Select(row => $"{row[0]} {(Flag(row[1]) ? "INSTEAD OF" : "AFTER")} {row[2]} ON {row[3]}.{row[4]}").Order(StringComparer.Ordinal).ToArray();
        foreach (var trigger in triggers) listing.Append("TRIGGER ").Append(trigger).Append('\n');
        if (triggers.Length > 0) listing.Append('\n');

        foreach (var table in tables.Keys)
        {
            var parts = table.Split('.', 2);
            Rows(connection, listing, table, $"[{parts[0].Replace("]", "]]")}].[{parts[1].Replace("]", "]]")}]");
        }
        return listing.ToString();
    }

    private static string SqlServerType(string name, int maxLength, int precision, int scale) => name switch
    {
        "nvarchar" or "nchar" => $"{name}({(maxLength < 0 ? "max" : (maxLength / 2).ToString(CultureInfo.InvariantCulture))})",
        "varchar" or "char" or "varbinary" or "binary" => $"{name}({(maxLength < 0 ? "max" : maxLength.ToString(CultureInfo.InvariantCulture))})",
        "decimal" or "numeric" => $"{name}({precision},{scale})",
        "datetime2" or "datetimeoffset" or "time" => $"{name}({scale})",
        _ => name,
    };

    // ---- Both ----

    /// <summary>A table's rows, one line each in ordinal order, when it has any: what the scripts put in a new database.</summary>
    private static void Rows(DbConnection connection, StringBuilder listing, string table, string quoted)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM " + quoted;
        var rows = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                rows.Add(string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i) + "=" + Value(reader.GetValue(i)))));
        if (rows.Count == 0) return;
        rows.Sort(StringComparer.Ordinal);
        listing.Append("ROWS ").Append(table).Append('\n');
        foreach (var row in rows) listing.Append(Indent).Append(row).Append('\n');
        listing.Append('\n');
    }

    private static string Value(object? value) => value switch
    {
        null or DBNull => "NULL",
        string text => "'" + text.Replace("'", "''") + "'",
        bool flag => flag ? "1" : "0",
        DateTime time => "'" + time.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

    private static bool Flag(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;

    private static List<object?[]> Query(DbConnection connection, string sql, params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        var rows = new List<object?[]>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
