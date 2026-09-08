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
}
