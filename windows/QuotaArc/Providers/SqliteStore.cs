using Microsoft.Data.Sqlite;

namespace QuotaArc.Providers;

internal static class SqliteStore
{
    public static SqliteConnection? Open(string path)
    {
        if (!File.Exists(path)) return null;
        foreach (var extra in new[] { "Mode=ReadOnly", "Mode=ReadOnly;Cache=Shared" })
        {
            try
            {
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString();
                var db = new SqliteConnection(cs);
                db.Open();
                return db;
            }
            catch
            {
                /* try next */
            }
        }
        return null;
    }

    public static string? Scalar(SqliteConnection db, string sql, string? bind = null)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        if (bind is not null) cmd.Parameters.AddWithValue("$v", bind);
        var value = cmd.ExecuteScalar();
        return value as string ?? value?.ToString();
    }

    /// The multi-row counterpart to `Scalar`, for a query that returns one
    /// text column per row — `composerHeaders.value`, in practice.
    public static List<string> Rows(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0)) values.Add(reader.GetString(0));
        }
        return values;
    }
}
