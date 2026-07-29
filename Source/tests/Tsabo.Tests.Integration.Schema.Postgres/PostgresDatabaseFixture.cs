using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Tsabo.Tests.Integration.Schema.Postgres;

public sealed class PostgresDatabaseFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
