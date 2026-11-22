using MySqlConnector;

namespace Server.Infrastructure;

public interface IDbConnectionFactory { MySqlConnection Create(); }

public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Default");
    }

    public MySqlConnection Create() => new(_connectionString);
}
