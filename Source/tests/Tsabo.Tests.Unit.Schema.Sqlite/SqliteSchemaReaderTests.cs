using Microsoft.Data.Sqlite;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Sqlite;

namespace Tsabo.Tests.Unit.Schema.Sqlite;

public class SqliteSchemaReaderTests
{
    private static SqliteConnection CreateConnection()
    {
        var name = Guid.NewGuid().ToString("N");
        var connection = new SqliteConnection($"Data Source={name};Mode=Memory;Cache=Shared");
        connection.Open();
        return connection;
    }

    [Test]
    public async Task ReadAsync_EmptyDatabase_ReturnsEmptySchema()
    {
        await using var connection = CreateConnection();
        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });

        var schema = await reader.ReadAsync();

        await Assert.That(schema.Tables).IsEmpty();
    }

    [Test]
    public async Task ReadAsync_SingleTable_ReturnsTableWithColumns()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"Users\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"Name\" TEXT NOT NULL, \"Email\" TEXT);";
        await cmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema = await reader.ReadAsync();

        await Assert.That(schema.Tables).Count().IsEqualTo(1);
        await Assert.That(schema.Tables[0].Name).IsEqualTo("Users");
        await Assert.That(schema.Tables[0].Columns).Count().IsEqualTo(3);

        var idCol = schema.Tables[0].Columns.First(p => p.Name == "Id");
        await Assert.That(idCol.IsPrimaryKey).IsTrue();
        await Assert.That(idCol.IsAutoIncrement).IsTrue();
    }

    [Test]
    public async Task ReadAsync_TableWithIndex_ReturnsIndex()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"Products\" (\"Id\" INTEGER PRIMARY KEY, \"Sku\" TEXT NOT NULL);";
        await cmd.ExecuteNonQueryAsync();

        await using var idxCmd = connection.CreateCommand();
        idxCmd.CommandText = "CREATE UNIQUE INDEX \"IX_Products_Sku\" ON \"Products\" (\"Sku\");";
        await idxCmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema = await reader.ReadAsync();

        var table = schema.Tables.First();
        await Assert.That(table.Indexes).Count().IsEqualTo(1);
        await Assert.That(table.Indexes[0].Name).IsEqualTo("IX_Products_Sku");
        await Assert.That(table.Indexes[0].IsUnique).IsTrue();
    }

    [Test]
    public async Task ReadAsync_TableWithForeignKey_ReturnsForeignKey()
    {
        await using var connection = CreateConnection();
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = "CREATE TABLE \"Orders\" (\"Id\" INTEGER PRIMARY KEY);";
        await cmd1.ExecuteNonQueryAsync();

        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "CREATE TABLE \"OrderLines\" (\"Id\" INTEGER PRIMARY KEY, \"OrderId\" INTEGER NOT NULL, FOREIGN KEY (\"OrderId\") REFERENCES \"Orders\" (\"Id\"));";
        await cmd2.ExecuteNonQueryAsync();

        await using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
        await pragmaCmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema = await reader.ReadAsync();

        var orderLines = schema.Tables.First(t => t.Name == "OrderLines");
        await Assert.That(orderLines.ForeignKeys).Count().IsEqualTo(1);
        await Assert.That(orderLines.ForeignKeys[0].ReferencedTable).IsEqualTo("Orders");
        await Assert.That(orderLines.ForeignKeys[0].Column).IsEqualTo("OrderId");
    }

    [Test]
    public async Task ReadAsync_IgnoredTables_ExcludesTable()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE "Keep" ("Id" INTEGER PRIMARY KEY);
                          CREATE TABLE "Skip" ("Id" INTEGER PRIMARY KEY);
                          """;

        await cmd.ExecuteNonQueryAsync();

        var options = new SchemaOptions { IgnoredTables = ["Skip"], CacheSchema = false };
        var reader = new SqliteSchemaReader(connection.ConnectionString, options);
        var schema = await reader.ReadAsync();

        await Assert.That(schema.Tables).Count().IsEqualTo(1);
        await Assert.That(schema.Tables[0].Name).IsEqualTo("Keep");
    }

    [Test]
    public async Task ReadAsync_IgnoredColumns_ExcludesColumn()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"Items\" (\"Id\" INTEGER PRIMARY KEY, \"Secret\" TEXT, \"Name\" TEXT);";
        await cmd.ExecuteNonQueryAsync();

        var options = new SchemaOptions { IgnoredColumns = ["Secret"], CacheSchema = false };
        var reader = new SqliteSchemaReader(connection.ConnectionString, options);
        var schema = await reader.ReadAsync();

        var table = schema.Tables.First();
        await Assert.That(table.Columns.Any(p => p.Name == "Secret")).IsFalse();
        await Assert.That(table.Columns.Any(p => p.Name == "Name")).IsTrue();
    }

    [Test]
    public async Task ReadAsync_CachingEnabled_ReturnsSameInstance()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"Cached\" (\"Id\" INTEGER PRIMARY KEY);";
        await cmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = true });
        var schema1 = await reader.ReadAsync();
        var schema2 = await reader.ReadAsync();

        await Assert.That(ReferenceEquals(schema1, schema2)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_CachingDisabled_ReturnsNewInstance()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"NoCached\" (\"Id\" INTEGER PRIMARY KEY);";
        await cmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema1 = await reader.ReadAsync();
        var schema2 = await reader.ReadAsync();

        await Assert.That(ReferenceEquals(schema1, schema2)).IsFalse();
    }

    [Test]
    public async Task ReadAsync_CompositePrimaryKey_ReadsBothPkColumns()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"SystemProducts\" (\"SystemId\" TEXT NOT NULL, \"ProductId\" TEXT NOT NULL, PRIMARY KEY (\"SystemId\", \"ProductId\"));";
        await cmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema = await reader.ReadAsync();

        var table = schema.Tables.First();
        var pkColumns = table.Columns.Where(p => p.IsPrimaryKey).ToList();
        await Assert.That(pkColumns).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ReadAsync_ColumnOrder_MatchesOrdinalPosition()
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE \"Ordered\" (\"First\" INTEGER PRIMARY KEY, \"Second\" TEXT, \"Third\" REAL);";
        await cmd.ExecuteNonQueryAsync();

        var reader = new SqliteSchemaReader(connection.ConnectionString, new SchemaOptions { CacheSchema = false });
        var schema = await reader.ReadAsync();

        var cols = schema.Tables.First().Columns;
        await Assert.That(cols[0].Name).IsEqualTo("First");
        await Assert.That(cols[1].Name).IsEqualTo("Second");
        await Assert.That(cols[2].Name).IsEqualTo("Third");
    }
}
