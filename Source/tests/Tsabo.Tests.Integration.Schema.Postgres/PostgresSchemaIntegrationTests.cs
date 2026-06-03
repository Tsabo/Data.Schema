using Npgsql;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Postgres;

namespace Tsabo.Tests.Integration.Schema.Postgres;

[ClassDataSource<PostgresDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class PostgresSchemaIntegrationTests(PostgresDatabaseFixture db)
{
    // Scenario 1: FK ordering
    // Customs (which has a FK to Products) is defined before Products in the schema.
    // Before the fix, AddForeignKey was emitted immediately after CreateTable for each
    // table, so the FK on Customs was applied before Products existed.
    [Test]
    public async Task Migrate_FkDefinedBeforeReferencedTable_AllObjectsCreatedSuccessfully()
    {
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "FkOrder_Customs",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "ProductId", Type = "uuid", IsNullable = false },
                    ],
                    ForeignKeys =
                    [
                        new ForeignKeyDefinition
                        {
                            Name = "FK_FkOrder_Customs_Products_ProductId",
                            Column = "ProductId",
                            ReferencedTable = "FkOrder_Products",
                            ReferencedColumn = "Id",
                        },
                    ],
                },
                new TableDefinition
                {
                    Name = "FkOrder_Products",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "text", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = new PostgresSchemaComparer().Compare(new SchemaDefinition(), target);
        var result = await new PostgresSchemaMigrator(db.ConnectionString).MigrateAsync(diff);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Success).IsTrue();
        await Assert.That(await TableExistsAsync("FkOrder_Products")).IsTrue();
        await Assert.That(await TableExistsAsync("FkOrder_Customs")).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync("FK_FkOrder_Customs_Products_ProductId")).IsTrue();
    }

    // Scenario 2: Guid PK type compatibility
    // Before the fix, IsAutoIncrement=true on a uuid PK caused MapToPostgresType to
    // emit SERIAL (integer) instead of uuid, making the FK type-incompatible.
    [Test]
    public async Task Migrate_GuidPrimaryKeyWithForeignKey_ColumnTypeIsUuidNotSerial()
    {
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Guid_Products",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true, IsAutoIncrement = false },
                        new ColumnDefinition { Name = "Name", Type = "text", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Guid_Customs",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true, IsAutoIncrement = false },
                        new ColumnDefinition { Name = "ProductId", Type = "uuid", IsNullable = false },
                    ],
                    ForeignKeys =
                    [
                        new ForeignKeyDefinition
                        {
                            Name = "FK_Guid_Customs_Products_ProductId",
                            Column = "ProductId",
                            ReferencedTable = "Guid_Products",
                            ReferencedColumn = "Id",
                        },
                    ],
                },
            ],
        };

        var diff = new PostgresSchemaComparer().Compare(new SchemaDefinition(), target);
        var result = await new PostgresSchemaMigrator(db.ConnectionString).MigrateAsync(diff);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Success).IsTrue();
        await Assert.That(await GetColumnTypeAsync("Guid_Products", "Id")).IsEqualTo("uuid");
        await Assert.That(await ForeignKeyExistsAsync("FK_Guid_Customs_Products_ProductId")).IsTrue();
    }

    // Scenario 3: FK added to an existing table on a subsequent migration
    // The comparer's else-branch FK detection was missing before the fix, so new FKs
    // on already-existing tables were silently skipped.
    [Test]
    public async Task Migrate_FkAddedToExistingTable_ForeignKeyCreatedOnSecondRun()
    {
        var initialSchema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Efk_Customers",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "text", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Efk_Orders",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "CustomerId", Type = "uuid", IsNullable = false },
                    ],
                },
            ],
        };

        var migrator = new PostgresSchemaMigrator(db.ConnectionString);
        var firstRun = await migrator.MigrateAsync(new PostgresSchemaComparer().Compare(new SchemaDefinition(), initialSchema));
        await Assert.That(firstRun.Error).IsNull();
        await Assert.That(firstRun.Success).IsTrue();

        var updatedSchema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Efk_Customers",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "text", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Efk_Orders",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uuid", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "CustomerId", Type = "uuid", IsNullable = false },
                    ],
                    ForeignKeys =
                    [
                        new ForeignKeyDefinition
                        {
                            Name = "FK_Efk_Orders_Customers_CustomerId",
                            Column = "CustomerId",
                            ReferencedTable = "Efk_Customers",
                            ReferencedColumn = "Id",
                        },
                    ],
                },
            ],
        };

        var secondRun = await migrator.MigrateAsync(new PostgresSchemaComparer().Compare(initialSchema, updatedSchema));
        await Assert.That(secondRun.Error).IsNull();
        await Assert.That(secondRun.Success).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync("FK_Efk_Orders_Customers_CustomerId")).IsTrue();
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = $1)";
        cmd.Parameters.AddWithValue(tableName);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> ForeignKeyExistsAsync(string constraintName)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = $1 AND constraint_type = 'FOREIGN KEY')";
        cmd.Parameters.AddWithValue(constraintName);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> GetColumnTypeAsync(string tableName, string columnName)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data_type FROM information_schema.columns WHERE table_name = $1 AND column_name = $2";
        cmd.Parameters.AddWithValue(tableName);
        cmd.Parameters.AddWithValue(columnName);
        return (string?)await cmd.ExecuteScalarAsync();
    }
}
