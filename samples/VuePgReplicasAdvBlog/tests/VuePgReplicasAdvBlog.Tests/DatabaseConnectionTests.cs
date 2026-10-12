using Npgsql;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class DatabaseConnectionTests
{
    [Fact]
    public void Database_url_decodes_credentials_and_ssl_options()
    {
        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(
            "postgresql://user:p%40ss%3Aword@localhost/sample%20db?sslmode=verify-full"));
        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("user", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal("sample db", parsed.Database);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);
        Assert.Equal(55432, new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse("postgres://localhost:55432/sample")).Port);
        Assert.Equal("Host=localhost;Database=sample", DatabaseConnection.Parse("Host=localhost;Database=sample"));
        Assert.Throws<ArgumentException>(() => DatabaseConnection.Parse("postgres://localhost/"));
    }
}
