using Microsoft.Data.SqlClient;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.SqlServer;

namespace Tsabo.Tests.Integration.Schema.SqlServer;

[ClassDataSource<SqlServerDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SqlServerSchemaIntegrationTests(SqlServerDatabaseFixture db)
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
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "ProductId", Type = "uniqueidentifier", IsNullable = false },
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
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "nvarchar(200)", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = new SqlServerSchemaComparer().Compare(new SchemaDefinition(), target);
        var result = await new SqlServerSchemaMigrator(db.ConnectionString).MigrateAsync(diff);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Success).IsTrue();
        await Assert.That(await TableExistsAsync("FkOrder_Products")).IsTrue();
        await Assert.That(await TableExistsAsync("FkOrder_Customs")).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync("FK_FkOrder_Customs_Products_ProductId")).IsTrue();
    }

    // Scenario 2: Guid PK type compatibility
    // Before the fix, IsAutoIncrement=true on a uniqueidentifier PK caused the comparer
    // to emit IDENTITY(1,1), which SQL Server rejects on non-integer columns.
    [Test]
    public async Task Migrate_GuidPrimaryKeyWithForeignKey_ColumnTypeIsUniqueidentifierNotIdentity()
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
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true, IsAutoIncrement = false },
                        new ColumnDefinition { Name = "Name", Type = "nvarchar(200)", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Guid_Customs",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true, IsAutoIncrement = false },
                        new ColumnDefinition { Name = "ProductId", Type = "uniqueidentifier", IsNullable = false },
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

        var diff = new SqlServerSchemaComparer().Compare(new SchemaDefinition(), target);
        var result = await new SqlServerSchemaMigrator(db.ConnectionString).MigrateAsync(diff);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Success).IsTrue();
        await Assert.That(await GetColumnTypeAsync("Guid_Products", "Id")).IsEqualTo("uniqueidentifier");
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
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "nvarchar(200)", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Efk_Orders",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "CustomerId", Type = "uniqueidentifier", IsNullable = false },
                    ],
                },
            ],
        };

        var migrator = new SqlServerSchemaMigrator(db.ConnectionString);
        var firstRun = await migrator.MigrateAsync(new SqlServerSchemaComparer().Compare(new SchemaDefinition(), initialSchema));
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
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "nvarchar(200)", IsNullable = false },
                    ],
                },
                new TableDefinition
                {
                    Name = "Efk_Orders",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "uniqueidentifier", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "CustomerId", Type = "uniqueidentifier", IsNullable = false },
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

        var secondRun = await migrator.MigrateAsync(new SqlServerSchemaComparer().Compare(initialSchema, updatedSchema));
        await Assert.That(secondRun.Error).IsNull();
        await Assert.That(secondRun.Success).IsTrue();
        await Assert.That(await ForeignKeyExistsAsync("FK_Efk_Orders_Customers_CustomerId")).IsTrue();
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@table) IS NOT NULL THEN 1 ELSE 0 END";
        cmd.Parameters.AddWithValue("@table", tableName);
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private async Task<bool> ForeignKeyExistsAsync(string constraintName)
    {
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@fk, 'F') IS NOT NULL THEN 1 ELSE 0 END";
        cmd.Parameters.AddWithValue("@fk", constraintName);
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private async Task<string?> GetColumnTypeAsync(string tableName, string columnName)
    {
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TYPE_NAME(system_type_id) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = @col";
        cmd.Parameters.AddWithValue("@table", tableName);
        cmd.Parameters.AddWithValue("@col", columnName);
        return (string?)await cmd.ExecuteScalarAsync();
    }
}
