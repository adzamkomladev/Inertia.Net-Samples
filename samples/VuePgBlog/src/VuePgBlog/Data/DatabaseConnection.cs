using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace VuePgBlog.Data;

public static class DatabaseConnection
{
    public static string Parse(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)) return value;

        var uri = new Uri(value);
        if (string.IsNullOrEmpty(uri.Host) || uri.AbsolutePath.Length < 2)
            throw new ArgumentException("DATABASE_URL must include a host and database name.");

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = uri.IdnHost,
            Port = uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
        };

        if (uri.UserInfo.Length > 0)
        {
            var credentials = uri.UserInfo.Split(':', 2);
            connection.Username = Uri.UnescapeDataString(credentials[0]);
            if (credentials.Length == 2) connection.Password = Uri.UnescapeDataString(credentials[1]);
        }

        foreach (var (key, option) in QueryHelpers.ParseQuery(uri.Query))
        {
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                connection.SslMode = Enum.Parse<SslMode>(option.ToString().Replace("-", ""), ignoreCase: true);
            else connection[key] = option.ToString();
        }

        return connection.ConnectionString;
    }
}
