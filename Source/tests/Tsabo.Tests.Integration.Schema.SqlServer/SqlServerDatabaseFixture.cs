using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

namespace Tsabo.Tests.Integration.Schema.SqlServer;

public sealed class SqlServerDatabaseFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
