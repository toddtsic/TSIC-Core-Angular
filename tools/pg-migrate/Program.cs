// pg-migrate: copies an in-scope subset of a SQL Server database into a fresh local Postgres database.
//
//   dotnet run -- [--source <sqlserver conn>] [--target <postgres conn>] [--tables <file>|all]
//
// Reads SQL Server only. Drops and recreates the TARGET database every run, and refuses any
// target host other than localhost. Schema comes from the SQL Server catalog, not the EF model.
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

var opts = Options.Parse(args);
var sw = Stopwatch.StartNew();

await using var src = new SqlConnection(opts.Source);
await src.OpenAsync();
var catalog = await Catalog.LoadAsync(src, opts.TablesFile);
Console.WriteLine($"Source: {src.Database} — {catalog.Tables.Count} tables in scope");

var ddl = new DdlBuilder(catalog);
await PgTarget.RecreateDatabaseAsync(opts.Target);
await using var pg = new NpgsqlConnection(opts.Target);
await pg.OpenAsync();

await PgTarget.ExecAsync(pg, ddl.Prologue());
foreach (var t in catalog.Tables) await PgTarget.ExecAsync(pg, ddl.CreateTable(t));
Console.WriteLine($"Created {catalog.Tables.Count} tables ({sw.Elapsed:mm\\:ss})");

var problems = new List<string>();
foreach (var t in catalog.Tables)
{
    var (rows, nulsStripped) = await DataCopier.CopyAsync(src, pg, t, ddl);
    var expected = await DataCopier.CountSourceAsync(src, t);
    var actual = await DataCopier.CountTargetAsync(pg, t);
    var flag = actual == expected ? "" : $"  ** MISMATCH source={expected}";
    if (nulsStripped > 0) problems.Add($"{t.Display}: stripped NUL chars from {nulsStripped} values");
    if (flag != "") problems.Add($"{t.Display}: row count source={expected} target={actual}");
    Console.WriteLine($"  {t.Display,-55} {actual,10:N0}{flag}");
}
Console.WriteLine($"Data copied ({sw.Elapsed:mm\\:ss})");

foreach (var stmt in ddl.PostLoad())
{
    try { await PgTarget.ExecAsync(pg, stmt.Sql); }
    catch (PostgresException ex) { problems.Add($"{stmt.What}: {ex.MessageText}"); }
}
Console.WriteLine($"Indexes, constraints, sequences applied ({sw.Elapsed:mm\\:ss})");

problems.AddRange(ddl.Skipped);
Console.WriteLine(problems.Count == 0 ? "OK — no problems." : $"{problems.Count} problem(s):");
foreach (var p in problems) Console.WriteLine("  - " + p);
return problems.Count == 0 ? 0 : 1;

// ───────────────────────────── options ─────────────────────────────

sealed record Options(string Source, string Target, string? TablesFile)
{
    public static Options Parse(string[] args)
    {
        string source = @"Server=.\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True";
        string target = "Host=localhost;Port=5432;Database=tsicv5;Username=postgres";
        string? tables = Path.Combine(AppContext.BaseDirectory, "tables-tsicv5.txt");
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source": source = args[++i]; break;
                case "--target": target = args[++i]; break;
                case "--tables": tables = args[++i] == "all" ? null : args[i]; break;
                default: throw new ArgumentException($"Unknown argument {args[i]}");
            }
        }
        var host = new NpgsqlConnectionStringBuilder(target).Host;
        if (host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException($"Refusing non-local target host '{host}'. This tool drops the target database.");
        return new Options(source, target, tables);
    }
}

// ───────────────────────────── source catalog ─────────────────────────────

sealed record Column(int Id, string Name, string Type, short MaxLength, byte Precision, byte Scale,
    bool Nullable, bool Identity, string? Default, string? Computed)
{
    public bool IsText => Type is "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" or "sysname";
}

sealed record IndexCol(string Name, bool Descending, bool Included);

sealed record Index(string Name, bool Primary, bool UniqueConstraint, bool Unique, bool Spatial, string? Filter, List<IndexCol> Cols);

sealed record ForeignKey(string Name, int ParentId, int RefId, List<(string Col, string RefCol)> Cols, int OnDelete, int OnUpdate);

sealed record Check(string Name, string Definition);

sealed class Table(int id, string schema, string name)
{
    public int Id { get; } = id;
    public string Schema { get; } = schema;
    public string Name { get; } = name;
    public string Display => $"{Schema}.{Name}";
    public List<Column> Columns { get; } = [];
    public List<Index> Indexes { get; } = [];
    public List<Check> Checks { get; } = [];
    public List<ForeignKey> ForeignKeys { get; } = [];
}

sealed record Sequence(string Schema, string Name, string Type, long Start, long Increment, long Current);

sealed class Catalog
{
    public List<Table> Tables { get; } = [];
    public List<Sequence> Sequences { get; } = [];
    public Dictionary<int, Table> ById { get; } = [];

    public static async Task<Catalog> LoadAsync(SqlConnection cn, string? tablesFile)
    {
        var wanted = tablesFile is null ? null : File.ReadAllLines(tablesFile)
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var c = new Catalog();
        await foreach (var r in Query(cn, """
            SELECT t.object_id, s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 ORDER BY s.name, t.name
            """))
        {
            var t = new Table(r.GetInt32(0), r.GetString(1), r.GetString(2));
            if (wanted is null || wanted.Remove(t.Display)) { c.Tables.Add(t); c.ById[t.Id] = t; }
        }
        if (wanted is { Count: > 0 })
            throw new InvalidOperationException("Tables listed but not found in source: " + string.Join(", ", wanted));

        await foreach (var r in Query(cn, """
            SELECT c.object_id, c.column_id, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable,
                   c.is_identity, d.definition, cc.definition
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            ORDER BY c.object_id, c.column_id
            """))
        {
            if (c.ById.TryGetValue(r.GetInt32(0), out var t))
                t.Columns.Add(new Column(r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt16(4), r.GetByte(5),
                    r.GetByte(6), r.GetBoolean(7), r.GetBoolean(8), r.IsDBNull(9) ? null : r.GetString(9),
                    r.IsDBNull(10) ? null : r.GetString(10)));
        }

        await foreach (var r in Query(cn, """
            SELECT i.object_id, i.index_id, i.name, i.is_primary_key, i.is_unique_constraint, i.is_unique,
                   i.type_desc, i.filter_definition, col.name, ic.is_descending_key, ic.is_included_column
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE i.type > 0 AND i.is_hypothetical = 0
            ORDER BY i.object_id, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """))
        {
            if (!c.ById.TryGetValue(r.GetInt32(0), out var t)) continue;
            var name = r.GetString(2);
            var ix = t.Indexes.FirstOrDefault(x => x.Name == name);
            if (ix is null)
            {
                var type = r.GetString(6);
                if (type is not ("CLUSTERED" or "NONCLUSTERED" or "SPATIAL"))
                    throw new NotSupportedException($"{t.Display}.{name}: index type {type}");
                ix = new Index(name, r.GetBoolean(3), r.GetBoolean(4), r.GetBoolean(5), type == "SPATIAL",
                    r.IsDBNull(7) ? null : r.GetString(7), []);
                t.Indexes.Add(ix);
            }
            ix.Cols.Add(new IndexCol(r.GetString(8), r.GetBoolean(9), r.GetBoolean(10)));
        }

        await foreach (var r in Query(cn, """
            SELECT parent_object_id, name, definition FROM sys.check_constraints WHERE is_disabled = 0
            """))
        {
            if (c.ById.TryGetValue(r.GetInt32(0), out var t)) t.Checks.Add(new Check(r.GetString(1), r.GetString(2)));
        }

        await foreach (var r in Query(cn, """
            SELECT f.object_id, f.name, f.parent_object_id, f.referenced_object_id, f.delete_referential_action,
                   f.update_referential_action, pc.name, rc.name
            FROM sys.foreign_keys f
            JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id
            JOIN sys.columns pc ON pc.object_id = fc.parent_object_id AND pc.column_id = fc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fc.referenced_object_id AND rc.column_id = fc.referenced_column_id
            WHERE f.is_disabled = 0
            ORDER BY f.object_id, fc.constraint_column_id
            """))
        {
            if (!c.ById.TryGetValue(r.GetInt32(2), out var t)) continue;
            var name = r.GetString(1);
            var fk = t.ForeignKeys.FirstOrDefault(x => x.Name == name);
            if (fk is null)
            {
                fk = new ForeignKey(name, t.Id, r.GetInt32(3), [], r.GetByte(4), r.GetByte(5));
                t.ForeignKeys.Add(fk);
            }
            fk.Cols.Add((r.GetString(6), r.GetString(7)));
        }

        var schemas = c.Tables.Select(t => t.Schema).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await foreach (var r in Query(cn, """
            SELECT s.name, q.name, TYPE_NAME(q.user_type_id), CONVERT(bigint, q.start_value),
                   CONVERT(bigint, q.increment), CONVERT(bigint, q.current_value)
            FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id
            """))
        {
            if (schemas.Contains(r.GetString(0)))
                c.Sequences.Add(new Sequence(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5)));
        }
        return c;
    }

    static async IAsyncEnumerable<SqlDataReader> Query(SqlConnection cn, string sql)
    {
        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) yield return r;
    }
}

// ───────────────────────────── DDL translation ─────────────────────────────

sealed record PostLoadStatement(string What, string Sql);

sealed class DdlBuilder(Catalog catalog)
{
    // Case-insensitive, accent-sensitive: the Postgres equivalent of SQL_Latin1_General_CP1_CI_AS.
    public const string Collation = "ci";
    public List<string> Skipped { get; } = [];
    readonly Dictionary<string, HashSet<string>> _relNames = new(StringComparer.OrdinalIgnoreCase);

    public static string Q(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    public static string QT(Table t) => $"{Q(t.Schema)}.{Q(t.Name)}";

    public string Prologue()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE EXTENSION IF NOT EXISTS postgis;");
        sb.AppendLine($"CREATE COLLATION {Collation} (provider = icu, locale = 'und-u-ks-level2', deterministic = false);");
        foreach (var s in catalog.Tables.Select(t => t.Schema).Distinct(StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Q(s)};");
        return sb.ToString();
    }

    public string CreateTable(Table t)
    {
        Reserve(t.Schema, t.Name);
        var lines = t.Columns.Select(c => "  " + ColumnDef(t, c)).ToList();
        var pk = t.Indexes.FirstOrDefault(i => i.Primary);
        if (pk is not null)
        {
            var pkName = Reserve(t.Schema, pk.Name);
            lines.Add($"  CONSTRAINT {Q(pkName)} PRIMARY KEY ({string.Join(", ", pk.Cols.Select(c => Q(c.Name)))})");
        }
        return $"CREATE TABLE {QT(t)} (\n{string.Join(",\n", lines)}\n);";
    }

    string ColumnDef(Table t, Column c)
    {
        var sb = new StringBuilder($"{Q(c.Name)} {PgType(c)}");
        if (c.IsText) sb.Append($" COLLATE {Collation}");
        if (c.Computed is not null)
        {
            sb.Append($" GENERATED ALWAYS AS ({ComputedExpr(t, c)}) STORED");
            return sb.ToString();
        }
        if (c.Identity) sb.Append(" GENERATED BY DEFAULT AS IDENTITY");
        if (!c.Nullable) sb.Append(" NOT NULL");
        if (c.Default is not null) sb.Append(" DEFAULT " + DefaultExpr(t, c));
        return sb.ToString();
    }

    public static string PgType(Column c) => c.Type switch
    {
        "nvarchar" or "varchar" => c.MaxLength == -1 ? "text" : $"varchar({(c.Type == "nvarchar" ? c.MaxLength / 2 : c.MaxLength)})",
        "nchar" => $"char({c.MaxLength / 2})",
        "char" => $"char({c.MaxLength})",
        "ntext" or "text" => "text",
        "sysname" => "varchar(128)",
        "int" => "integer",
        "bigint" => "bigint",
        "smallint" or "tinyint" => "smallint",
        "bit" => "boolean",
        "decimal" or "numeric" => $"numeric({c.Precision},{c.Scale})",
        "money" => "numeric(19,4)",
        "smallmoney" => "numeric(10,4)",
        "float" => "double precision",
        "real" => "real",
        "datetime" or "smalldatetime" => "timestamp(3)",
        "datetime2" => $"timestamp({Math.Min((int)c.Scale, 6)})",
        "datetimeoffset" => $"timestamptz({Math.Min((int)c.Scale, 6)})",
        "date" => "date",
        "time" => $"time({Math.Min((int)c.Scale, 6)})",
        "uniqueidentifier" => "uuid",
        "varbinary" or "binary" or "image" => "bytea",
        "geography" => "geography",
        "xml" => "xml",
        _ => throw new NotSupportedException($"{c.Name}: type {c.Type}")
    };

    string DefaultExpr(Table t, Column c)
    {
        var d = Unwrap(c.Default!);
        var lower = d.ToLowerInvariant();
        if (lower is "getdate()" or "sysdatetime()") return "LOCALTIMESTAMP";
        if (lower is "getutcdate()" or "sysutcdatetime()") return "(now() AT TIME ZONE 'utc')";
        if (lower is "sysdatetimeoffset()") return "now()";
        if (lower is "newsequentialid()") return "uuidv7()";
        if (lower is "newid()") return "gen_random_uuid()";
        if (Regex.IsMatch(d, @"^-?\d+(\.\d+)?$"))
            return c.Type == "bit" ? (d == "0" ? "false" : "true") : d;
        var str = Regex.Match(d, @"^N?'((?:[^']|'')*)'$");
        if (str.Success) return $"'{str.Groups[1].Value}'";
        throw new NotSupportedException($"{t.Display}.{c.Name}: default {c.Default}");
    }

    // ([Location].[Lat]) / ([Location].[Long]) — the only computed columns in scope.
    static string ComputedExpr(Table t, Column c)
    {
        var m = Regex.Match(c.Computed!, @"^\(\[(\w+)\]\.\[(Lat|Long)\]\)$");
        if (!m.Success) throw new NotSupportedException($"{t.Display}.{c.Name}: computed {c.Computed}");
        return $"{(m.Groups[2].Value == "Lat" ? "ST_Y" : "ST_X")}({Q(ColName(t, m.Groups[1].Value))}::geometry)";
    }

    // T-SQL identifiers are case-insensitive; Postgres quoted ones are not. Resolve to the real column name.
    static string ColName(Table t, string name) =>
        t.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Name ?? name;

    static string Unwrap(string expr)
    {
        var e = expr.Trim();
        while (e.StartsWith('(') && e.EndsWith(')') && Balanced(e[1..^1])) e = e[1..^1].Trim();
        return e;
    }

    static bool Balanced(string s)
    {
        var depth = 0;
        foreach (var ch in s) { if (ch == '(') depth++; else if (ch == ')' && --depth < 0) return false; }
        return depth == 0;
    }

    // T-SQL boolean expression (filters, checks) → Postgres: [x] → "x", N'..' → '..', bit = (0/1) → false/true.
    static string Expr(Table t, string tsql)
    {
        var e = Regex.Replace(tsql, @"\[([^\]]+)\]", m => Q(ColName(t, m.Groups[1].Value)));
        e = Regex.Replace(e, @"\bN'", "'");
        foreach (var bit in t.Columns.Where(c => c.Type == "bit"))
            e = Regex.Replace(e, $@"{Regex.Escape(Q(bit.Name))}\s*=\s*\(?([01])\)?", m => $"{Q(bit.Name)} = {(m.Groups[1].Value == "1" ? "true" : "false")}");
        return e;
    }

    // Postgres index/constraint-backed names share one namespace per schema with tables; max 63 bytes.
    string Reserve(string schema, string name)
    {
        var used = _relNames.TryGetValue(schema, out var set) ? set : _relNames[schema] = new(StringComparer.Ordinal);
        var candidate = Fit(name);
        for (var n = 2; !used.Add(candidate); n++) candidate = Fit($"{name}_{n}");
        return candidate;
    }

    static string Fit(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= 63) return name;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        return name[..54] + "_" + hash;
    }

    public IEnumerable<PostLoadStatement> PostLoad()
    {
        foreach (var t in catalog.Tables)
        {
            foreach (var ix in t.Indexes.Where(i => !i.Primary))
            {
                var name = Reserve(t.Schema, ix.Name);
                var keys = ix.Cols.Where(c => !c.Included).Select(c => Q(c.Name) + (c.Descending ? " DESC" : ""));
                var incl = ix.Cols.Where(c => c.Included).Select(c => Q(c.Name)).ToList();
                if (ix.UniqueConstraint)
                {
                    yield return new($"{t.Display} unique {ix.Name}",
                        $"ALTER TABLE {QT(t)} ADD CONSTRAINT {Q(name)} UNIQUE ({string.Join(", ", keys)});");
                    continue;
                }
                var sql = new StringBuilder($"CREATE {(ix.Unique ? "UNIQUE " : "")}INDEX {Q(name)} ON {QT(t)}");
                sql.Append(ix.Spatial ? $" USING gist ({string.Join(", ", keys)})" : $" ({string.Join(", ", keys)})");
                if (incl.Count > 0) sql.Append($" INCLUDE ({string.Join(", ", incl)})");
                if (ix.Filter is not null) sql.Append($" WHERE {Expr(t, ix.Filter)}");
                yield return new($"{t.Display} index {ix.Name}", sql + ";");
            }
            foreach (var ck in t.Checks)
                yield return new($"{t.Display} check {ck.Name}",
                    $"ALTER TABLE {QT(t)} ADD CONSTRAINT {Q(Fit(ck.Name))} CHECK {Expr(t, ck.Definition)};");
        }

        foreach (var t in catalog.Tables)
        {
            foreach (var fk in t.ForeignKeys)
            {
                if (!catalog.ById.TryGetValue(fk.RefId, out var rt))
                {
                    Skipped.Add($"{t.Display} FK {fk.Name}: references a table outside scope — not created");
                    continue;
                }
                yield return new($"{t.Display} FK {fk.Name}",
                    $"ALTER TABLE {QT(t)} ADD CONSTRAINT {Q(Fit(fk.Name))} FOREIGN KEY ({string.Join(", ", fk.Cols.Select(c => Q(c.Col)))}) " +
                    $"REFERENCES {QT(rt)} ({string.Join(", ", fk.Cols.Select(c => Q(c.RefCol)))})" +
                    $" ON DELETE {Action(fk.OnDelete)} ON UPDATE {Action(fk.OnUpdate)};");
            }
            foreach (var c in t.Columns.Where(c => c.Identity))
                yield return new($"{t.Display} identity {c.Name}",
                    $"SELECT setval(pg_get_serial_sequence('{QT(t).Replace("'", "''")}', '{c.Name.Replace("'", "''")}'), " +
                    $"COALESCE((SELECT MAX({Q(c.Name)}) FROM {QT(t)}), 1), (SELECT MAX({Q(c.Name)}) IS NOT NULL FROM {QT(t)}));");
        }

        foreach (var s in catalog.Sequences)
        {
            var type = s.Type is "tinyint" or "smallint" ? "smallint" : s.Type == "int" ? "integer" : "bigint";
            yield return new($"sequence {s.Schema}.{s.Name}",
                $"CREATE SEQUENCE {Q(s.Schema)}.{Q(s.Name)} AS {type} START WITH {s.Start} INCREMENT BY {s.Increment}; " +
                $"SELECT setval('{Q(s.Schema)}.{Q(s.Name)}', {s.Current});");
        }
    }

    static string Action(int a) => a switch { 1 => "CASCADE", 2 => "SET NULL", 3 => "SET DEFAULT", _ => "NO ACTION" };
}

// ───────────────────────────── data copy ─────────────────────────────

static class DataCopier
{
    public static async Task<(long Rows, long NulsStripped)> CopyAsync(SqlConnection src, NpgsqlConnection pg, Table t, DdlBuilder ddl)
    {
        var cols = t.Columns.Where(c => c.Computed is null).ToList();
        var select = string.Join(", ", cols.Select(c => c.Type == "geography"
            ? $"[{c.Name}].STAsText() AS [{c.Name}], [{c.Name}].STSrid AS [{c.Name}__srid]"
            : $"[{c.Name}]"));
        var hasGeo = cols.Any(c => c.Type == "geography");

        // Geography goes through a text staging column; binary COPY can't produce PostGIS values from WKT.
        var copyTarget = DdlBuilder.QT(t);
        if (hasGeo)
        {
            copyTarget = "pg_temp." + DdlBuilder.Q("stg_" + t.Name);
            var defs = cols.SelectMany(c => c.Type == "geography"
                ? new[] { $"{DdlBuilder.Q(c.Name)} text", $"{DdlBuilder.Q(c.Name + "__srid")} integer" }
                : new[] { $"{DdlBuilder.Q(c.Name)} {DdlBuilder.PgType(c)}" });
            await PgTarget.ExecAsync(pg, $"CREATE TEMP TABLE {copyTarget} ({string.Join(", ", defs)});");
        }

        var copyCols = cols.SelectMany(c => c.Type == "geography" ? new[] { c.Name, c.Name + "__srid" } : new[] { c.Name }).ToList();
        long rows = 0, nuls = 0;
        await using (var cmd = new SqlCommand($"SELECT {select} FROM [{t.Schema}].[{t.Name}]", src) { CommandTimeout = 0 })
        await using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
        await using (var importer = await pg.BeginBinaryImportAsync(
            $"COPY {copyTarget} ({string.Join(", ", copyCols.Select(DdlBuilder.Q))}) FROM STDIN (FORMAT BINARY)"))
        {
            while (await reader.ReadAsync())
            {
                await importer.StartRowAsync();
                var ordinal = 0;
                foreach (var c in cols)
                {
                    if (c.Type == "geography")
                    {
                        await WriteAsync(importer, reader, ordinal++, NpgsqlDbType.Text, _ => { });
                        await WriteAsync(importer, reader, ordinal++, NpgsqlDbType.Integer, _ => { });
                        continue;
                    }
                    await WriteAsync(importer, reader, ordinal++, c, () => nuls++);
                }
                rows++;
            }
            await importer.CompleteAsync();
        }

        if (hasGeo)
        {
            var targetCols = string.Join(", ", cols.Select(c => DdlBuilder.Q(c.Name)));
            var exprs = string.Join(", ", cols.Select(c => c.Type == "geography"
                ? $"CASE WHEN {DdlBuilder.Q(c.Name)} IS NULL THEN NULL ELSE ST_GeogFromText('SRID=' || {DdlBuilder.Q(c.Name + "__srid")} || ';' || {DdlBuilder.Q(c.Name)}) END"
                : DdlBuilder.Q(c.Name)));
            await PgTarget.ExecAsync(pg, $"INSERT INTO {DdlBuilder.QT(t)} ({targetCols}) OVERRIDING SYSTEM VALUE SELECT {exprs} FROM {copyTarget}; DROP TABLE {copyTarget};");
        }
        return (rows, nuls);
    }

    static async Task WriteAsync(NpgsqlBinaryImporter imp, SqlDataReader r, int i, NpgsqlDbType type, Action<object> _)
    {
        if (await r.IsDBNullAsync(i)) await imp.WriteNullAsync();
        else await imp.WriteAsync(r.GetValue(i), type);
    }

    static async Task WriteAsync(NpgsqlBinaryImporter imp, SqlDataReader r, int i, Column c, Action onNulStripped)
    {
        if (await r.IsDBNullAsync(i)) { await imp.WriteNullAsync(); return; }
        switch (c.Type)
        {
            case "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" or "sysname":
                var s = r.GetString(i);
                if (s.Contains('\0')) { s = s.Replace("\0", ""); onNulStripped(); }
                await imp.WriteAsync(s, NpgsqlDbType.Text); break;
            case "int": await imp.WriteAsync(r.GetInt32(i), NpgsqlDbType.Integer); break;
            case "bigint": await imp.WriteAsync(r.GetInt64(i), NpgsqlDbType.Bigint); break;
            case "smallint": await imp.WriteAsync(r.GetInt16(i), NpgsqlDbType.Smallint); break;
            case "tinyint": await imp.WriteAsync((short)r.GetByte(i), NpgsqlDbType.Smallint); break;
            case "bit": await imp.WriteAsync(r.GetBoolean(i), NpgsqlDbType.Boolean); break;
            case "decimal" or "numeric" or "money" or "smallmoney": await imp.WriteAsync(r.GetDecimal(i), NpgsqlDbType.Numeric); break;
            case "float": await imp.WriteAsync(r.GetDouble(i), NpgsqlDbType.Double); break;
            case "real": await imp.WriteAsync(r.GetFloat(i), NpgsqlDbType.Real); break;
            case "datetime" or "smalldatetime" or "datetime2":
                await imp.WriteAsync(DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Unspecified), NpgsqlDbType.Timestamp); break;
            case "datetimeoffset": await imp.WriteAsync(r.GetDateTimeOffset(i).ToUniversalTime(), NpgsqlDbType.TimestampTz); break;
            case "date": await imp.WriteAsync(DateOnly.FromDateTime(r.GetDateTime(i)), NpgsqlDbType.Date); break;
            case "time": await imp.WriteAsync(r.GetTimeSpan(i), NpgsqlDbType.Time); break;
            case "uniqueidentifier": await imp.WriteAsync(r.GetGuid(i), NpgsqlDbType.Uuid); break;
            case "varbinary" or "binary" or "image": await imp.WriteAsync((byte[])r.GetValue(i), NpgsqlDbType.Bytea); break;
            case "xml": await imp.WriteAsync(r.GetString(i), NpgsqlDbType.Xml); break;
            default: throw new NotSupportedException($"{c.Name}: type {c.Type}");
        }
    }

    public static async Task<long> CountSourceAsync(SqlConnection src, Table t)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM [{t.Schema}].[{t.Name}]", src) { CommandTimeout = 0 };
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<long> CountTargetAsync(NpgsqlConnection pg, Table t)
    {
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {DdlBuilder.QT(t)}", pg);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}

// ───────────────────────────── target ─────────────────────────────

static class PgTarget
{
    public static async Task RecreateDatabaseAsync(string target)
    {
        var b = new NpgsqlConnectionStringBuilder(target);
        var db = b.Database ?? throw new ArgumentException("Target connection string needs a Database");
        b.Database = "postgres";
        await using var cn = new NpgsqlConnection(b.ConnectionString);
        await cn.OpenAsync();
        await ExecAsync(cn, $"DROP DATABASE IF EXISTS {DdlBuilder.Q(db)} WITH (FORCE);");
        await ExecAsync(cn, $"CREATE DATABASE {DdlBuilder.Q(db)};");
    }

    public static async Task ExecAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync();
    }
}
