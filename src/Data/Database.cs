using System.IO;
using Microsoft.Data.Sqlite;

namespace MarketPos.Data;

/// <summary>
/// Owns the single SQLite file and its schema. No server, no config — the database lives
/// in %AppData%\MarketPos so it survives reinstalls and needs no admin rights to write.
/// </summary>
public static class Database
{
    private static string? _path;

    /// <summary>
    /// True on a machine that has no shop database and must never make one.
    ///
    /// <para>
    /// A cashier's till is that machine. Everything it shows comes over the wire from the shop,
    /// so a database file here would be an empty one, created on the first careless call and
    /// then quietly filled by whatever else forgot to ask the shop. Guards at each call site
    /// catch the calls somebody remembered; this catches the ones nobody did.
    /// </para>
    ///
    /// <para>
    /// Set once at start-up, before anything opens anything. From then on every attempt to
    /// reach a database on this machine throws with a message that says which machine the data
    /// actually lives on, rather than silently creating a second shop.
    /// </para>
    /// </summary>
    public static bool NotOnThisMachine { get; set; }

    /// <summary>Where the database is, or would be. Reading this alone creates nothing.</summary>
    public static string Path => _path ??= BuildPath();

    private static void RefuseIfTill()
    {
        if (!NotOnThisMachine) return;

        throw new InvalidOperationException(
            MarketPos.Services.Loc.T("This is a cashier's till: it has no shop database. Whatever asked for one should be asking the shop's server instead."));
    }

    private static string BuildPath()
    {
        // An override exists only so the self-test can run the whole money flow against a
        // scratch file. A shop machine never sets it, and the till has no UI for it — a
        // second database is not something a cashier should be able to end up in.
        var overridePath = Environment.GetEnvironmentVariable("MARKETPOS_DB");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(overridePath)!);
            return overridePath;
        }

        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarketPos");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "marketpos.db");
    }

    private static readonly object Prepared = new();

    public static SqliteConnection Open()
    {
        RefuseIfTill();
        PrepareTheFile();

        // Pooling off: the file is open only while a request is using it. A pooled connection
        // kept marketpos.db open for as long as the server ran - a server with no window - so
        // replacing the file with a clean one was refused by Windows or left the server reading
        // the old one. Now the server always reads whatever marketpos.db is on disk.
        var connection = new SqliteConnection($"Data Source={Path};Pooling=False");
        connection.Open();

        using var pragma = connection.CreateCommand();

        //   foreign_keys  the relationships in the schema are enforced rather than decorative.
        //
        //   journal_mode  DELETE: every committed write is in marketpos.db itself, and the
        //                 short-lived -journal file is gone the moment the write finishes. The
        //                 shop's data is one file and only one file. Write-ahead logging kept
        //                 recent sales in marketpos.db-wal instead, so replacing or copying
        //                 marketpos.db on its own brought back — or lost — whatever that second
        //                 file held.
        //
        //   busy_timeout  five seconds of waiting for a lock instead of failing instantly. With a
        //                 rollback journal a write briefly blocks readers, and two tills paying in
        //                 the same second simply take turns.
        pragma.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = DELETE;
            """;
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// Makes marketpos.db the whole of the database, before every open.
    ///
    /// <para>
    /// A marketpos.db-wal or -shm beside a database that is not in write-ahead mode belongs to
    /// some other, older file: somebody replaced marketpos.db and left its companions behind.
    /// SQLite would replay that log into the new file as if it were unsaved work, and an empty
    /// database would open full of the old shop. Those leftovers are deleted, never read.
    /// </para>
    ///
    /// <para>
    /// A database still in write-ahead mode from an earlier version is the opposite case: its
    /// log is genuinely its own, holding the latest sales. It is folded into marketpos.db and
    /// the file switched to a rollback journal, after which the log is gone for good.
    /// </para>
    /// </summary>
    private static void PrepareTheFile()
    {
        var path = Path;
        var wal = path + "-wal";
        var shm = path + "-shm";

        // Checked on every open, not once per run: the file can be replaced while the server is
        // running, and a leftover beside the new one must not be read. The usual case - no
        // companion files at all - costs two existence checks.
        if (!File.Exists(wal) && !File.Exists(shm)) return;

        lock (Prepared)
        {

            if (!File.Exists(path) || !IsWriteAheadFile(path))
            {
                // Not this file's log. Deleted before SQLite can see it.
                TryDelete(wal);
                TryDelete(shm);
            }
            else
            {
                // This file's own log: written into the file, then switched off.
                using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA busy_timeout = 5000;
                    PRAGMA wal_checkpoint(TRUNCATE);
                    PRAGMA journal_mode = DELETE;
                    """;
                command.ExecuteNonQuery();
                connection.Close();

                TryDelete(wal);
                TryDelete(shm);
            }
        }
    }

    /// <summary>
    /// Whether the file's header says it is in write-ahead mode — bytes 18 and 19 of an SQLite
    /// database are 2 in WAL mode and 1 with a rollback journal.
    /// </summary>
    private static bool IsWriteAheadFile(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[20];
            return file.Read(header, 0, header.Length) == header.Length && header[18] == 2;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes a leftover companion file. One held open by another program is in use by it, not
    /// left over, and is left alone.
    /// </summary>
    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Creates the schema on first run. Safe to call on every startup.</summary>
    public static void Initialize()
    {
        RefuseIfTill();

        using var connection = Open();
        using var command = connection.CreateCommand();

        // Money is stored as TEXT, not REAL. SQLite's REAL is a double, and doubles cannot
        // represent 0.10 exactly — totals would drift by centimes over thousands of sales.
        // Invariant-culture strings round-trip a decimal exactly.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS categories (
                id    INTEGER PRIMARY KEY AUTOINCREMENT,
                name  TEXT    NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS products (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                barcode     TEXT    NOT NULL UNIQUE,
                name        TEXT    NOT NULL,
                category_id INTEGER NOT NULL REFERENCES categories(id),
                price       TEXT    NOT NULL,
                unit        TEXT    NOT NULL,
                tax_rate    TEXT    NOT NULL,
                emoji       TEXT    NOT NULL DEFAULT '',
                image_path  TEXT,
                is_active   INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_products_barcode ON products(barcode);
            CREATE INDEX IF NOT EXISTS ix_products_name    ON products(name);

            CREATE TABLE IF NOT EXISTS sales (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                invoice_number  INTEGER NOT NULL UNIQUE,
                sold_at         TEXT    NOT NULL,
                subtotal        TEXT    NOT NULL,
                tax             TEXT    NOT NULL,
                total           TEXT    NOT NULL,
                payment_method  TEXT    NOT NULL,
                amount_tendered TEXT,
                change_given    TEXT,
                is_voided       INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_sales_sold_at ON sales(sold_at);

            -- Lines keep their own copy of name/price on purpose. A product can be renamed
            -- or repriced later; a receipt reprinted next year must still show what was
            -- actually charged on the day.
            CREATE TABLE IF NOT EXISTS sale_lines (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                sale_id    INTEGER NOT NULL REFERENCES sales(id) ON DELETE CASCADE,
                product_id INTEGER          REFERENCES products(id),
                barcode    TEXT    NOT NULL,
                name       TEXT    NOT NULL,
                unit       TEXT    NOT NULL,
                unit_price TEXT    NOT NULL,
                quantity   TEXT    NOT NULL,
                tax_rate   TEXT    NOT NULL,
                line_total TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sale_lines_sale ON sale_lines(sale_id);
            """;
        command.ExecuteNonQuery();

        AddDiscountColumns(connection);
        Schema.Apply(connection);
    }

    /// <summary>
    /// Adds the remise columns to databases created before the feature existed. SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so existing columns are detected first — this keeps a till
    /// that already has sales history working after an update.
    /// </summary>
    private static void AddDiscountColumns(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(sales);";
            using var reader = columns.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }

        foreach (var (name, ddl) in new[]
                 {
                     ("gross_before_discount", "ALTER TABLE sales ADD COLUMN gross_before_discount TEXT NOT NULL DEFAULT '0';"),
                     ("discount_kind",         "ALTER TABLE sales ADD COLUMN discount_kind TEXT NOT NULL DEFAULT 'None';"),
                     ("discount_value",        "ALTER TABLE sales ADD COLUMN discount_value TEXT NOT NULL DEFAULT '0';"),
                     ("discount_amount",       "ALTER TABLE sales ADD COLUMN discount_amount TEXT NOT NULL DEFAULT '0';"),
                 })
        {
            if (existing.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = ddl;
            alter.ExecuteNonQuery();
        }
    }
}
