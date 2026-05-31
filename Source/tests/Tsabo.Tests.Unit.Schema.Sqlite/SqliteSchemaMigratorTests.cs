using Microsoft.Data.Sqlite;
using Moq;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Abstractions;
using Tsabo.Data.Schema.Sqlite;

namespace Tsabo.Tests.Unit.Schema.Sqlite;

public class SqliteSchemaMigratorTests
{
    private static SqliteConnection CreateConnection()
    {
        var name = Guid.NewGuid().ToString("N");
        var connection = new SqliteConnection($"Data Source={name};Mode=Memory;Cache=Shared");
        connection.Open();
        return connection;
    }

    private static string GetConnectionString()
    {
        using var c = CreateConnection();
        return c.ConnectionString;
    }

    [Test]
    public async Task MigrateAsync_EmptyDiff_ReturnsSuccess()
    {
        var migrator = new SqliteSchemaMigrator(GetConnectionString());
        var result = await migrator.MigrateAsync(new SchemaDiff());

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Operations).IsEmpty();
    }

    [Test]
    public async Task MigrateAsync_CreateTable_ExecutesSql()
    {
        await using var connection = CreateConnection();
        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "TestTable",
                    Sql = "CREATE TABLE \"TestTable\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"Name\" TEXT NOT NULL);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(connection.ConnectionString);
        var result = await migrator.MigrateAsync(diff);

        await Assert.That(result.Success).IsTrue();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TestTable'";
        var count = await cmd.ExecuteScalarAsync();
        await Assert.That((long)count!).IsEqualTo(1L);
    }

    [Test]
    public async Task MigrateAsync_MultipleOperations_ExecutesAll()
    {
        await using var connection = CreateConnection();
        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "Orders",
                    Sql = "CREATE TABLE \"Orders\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT);",
                },
                new MigrationOperation
                {
                    Type = MigrationOperationType.AddColumn,
                    TableName = "Orders",
                    ColumnName = "Amount",
                    Sql = "ALTER TABLE \"Orders\" ADD COLUMN \"Amount\" REAL NOT NULL DEFAULT 0.0;",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(connection.ConnectionString);
        var result = await migrator.MigrateAsync(diff);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Operations).Count().IsEqualTo(2);
    }

    [Test]
    public async Task MigrateAsync_DryRun_DoesNotExecuteSql()
    {
        await using var connection = CreateConnection();
        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "DryTable",
                    Sql = "CREATE TABLE \"DryTable\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(connection.ConnectionString);
        var result = await migrator.MigrateAsync(diff, new SchemaOptions { DryRun = true });

        await Assert.That(result.Success).IsTrue();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DryTable'";
        var count = await cmd.ExecuteScalarAsync();
        await Assert.That((long)count!).IsEqualTo(0L);
    }

    [Test]
    public async Task MigrateAsync_InvalidSql_RollsBack()
    {
        await using var connection = CreateConnection();

        await using var setupCmd = connection.CreateCommand();
        setupCmd.CommandText = "CREATE TABLE \"Existing\" (\"Id\" INTEGER PRIMARY KEY);";
        await setupCmd.ExecuteNonQueryAsync();

        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "Existing",
                    Sql = "CREATE TABLE \"Existing\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(connection.ConnectionString);
        var result = await migrator.MigrateAsync(diff);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    public async Task MigrateAsync_BeforeHook_IsCalled()
    {
        var hookMock = new Mock<IMigrationHook>();
        hookMock.Setup(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hookMock.Setup(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "T",
                    Sql = "CREATE TABLE \"T\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(GetConnectionString(), [hookMock.Object]);
        await migrator.MigrateAsync(diff);

        hookMock.Verify(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task MigrateAsync_AfterHook_IsCalled()
    {
        var hookMock = new Mock<IMigrationHook>();
        hookMock.Setup(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hookMock.Setup(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "T2",
                    Sql = "CREATE TABLE \"T2\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(GetConnectionString(), [hookMock.Object]);
        await migrator.MigrateAsync(diff);

        hookMock.Verify(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task MigrateAsync_FailedMigration_DoesNotCallAfterHook()
    {
        var hookMock = new Mock<IMigrationHook>();
        hookMock.Setup(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hookMock.Setup(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        await using var setupCmd = connection.CreateCommand();
        setupCmd.CommandText = "CREATE TABLE \"Clash\" (\"Id\" INTEGER PRIMARY KEY);";
        await setupCmd.ExecuteNonQueryAsync();

        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "Clash",
                    Sql = "CREATE TABLE \"Clash\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(connection.ConnectionString, [hookMock.Object]);
        await migrator.MigrateAsync(diff);

        hookMock.Verify(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MigrateAsync_DryRun_DoesNotCallBeforeOrAfterHooks()
    {
        var hookMock = new Mock<IMigrationHook>();
        hookMock.Setup(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hookMock.Setup(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = "X",
                    Sql = "CREATE TABLE \"X\" (\"Id\" INTEGER PRIMARY KEY);",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(GetConnectionString(), [hookMock.Object]);
        await migrator.MigrateAsync(diff, new SchemaOptions { DryRun = true });

        hookMock.Verify(h => h.BeforeAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()), Times.Never);
        hookMock.Verify(h => h.AfterAsync(It.IsAny<MigrationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MigrateAsync_OperationWithWarningOnly_ReturnsWarning()
    {
        var diff = new SchemaDiff
        {
            Operations =
            [
                new MigrationOperation
                {
                    Type = MigrationOperationType.ModifyColumn,
                    TableName = "T",
                    ColumnName = "Col",
                    Sql = string.Empty,
                    Warning = "SQLite does not support ALTER COLUMN.",
                },
            ],
        };

        var migrator = new SqliteSchemaMigrator(GetConnectionString());
        var result = await migrator.MigrateAsync(diff);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Warnings).Count().IsEqualTo(1);
        await Assert.That(result.Warnings[0]).Contains("SQLite does not support ALTER COLUMN");
    }
}
