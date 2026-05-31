using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Sqlite;

namespace Tsabo.Tests.Unit.Schema.Sqlite;

public class SqliteSchemaValidatorTests
{
    private readonly SqliteSchemaValidator _validator = new();

    [Test]
    public async Task Validate_EmptySchema_IsValid()
    {
        var result = _validator.Validate(new SchemaDefinition());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_ValidSchema_IsValid()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "TEXT" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_InvalidTableName_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "My Table",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Invalid table name"))).IsTrue();
    }

    [Test]
    public async Task Validate_InvalidColumnName_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Items",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "my-column", Type = "TEXT" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Invalid column name"))).IsTrue();
    }

    [Test]
    public async Task Validate_ReservedTableName_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "sqlite_sequence",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("sqlite_"))).IsTrue();
    }

    [Test]
    public async Task Validate_InvalidColumnType_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Things",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Data", Type = "JSONB" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Unsupported SQLite type"))).IsTrue();
    }

    [Test]
    public async Task Validate_DuplicateColumns_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Items",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Name", Type = "TEXT" },
                        new ColumnDefinition { Name = "Name", Type = "TEXT" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Duplicate column name"))).IsTrue();
    }

    [Test]
    public async Task Validate_DuplicateIndexes_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Items",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    Indexes =
                    [
                        new IndexDefinition { Name = "IX_Items_Id", Columns = ["Id"] },
                        new IndexDefinition { Name = "IX_Items_Id", Columns = ["Id"] },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Duplicate index name"))).IsTrue();
    }

    [Test]
    public async Task Validate_SelfReferencingForeignKey_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Nodes",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    ForeignKeys =
                    [
                        new ForeignKeyDefinition { Name = "FK_Nodes_Self", Column = "Id", ReferencedTable = "Nodes", ReferencedColumn = "Id" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("self-referencing"))).IsTrue();
    }

    [Test]
    public async Task Validate_CircularForeignKeys_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "A",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    ForeignKeys = [new ForeignKeyDefinition { Name = "FK_A_B", Column = "Id", ReferencedTable = "B", ReferencedColumn = "Id" }],
                },
                new TableDefinition
                {
                    Name = "B",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    ForeignKeys = [new ForeignKeyDefinition { Name = "FK_B_A", Column = "Id", ReferencedTable = "A", ReferencedColumn = "Id" }],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Circular foreign key"))).IsTrue();
    }

    [Test]
    public async Task Validate_ForeignKeyToNonExistentTable_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Orders",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    ForeignKeys =
                    [
                        new ForeignKeyDefinition { Name = "FK_Orders_Ghost", Column = "CustomerId", ReferencedTable = "Customers", ReferencedColumn = "Id" },
                    ],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("non-existent table"))).IsTrue();
    }

    [Test]
    public async Task Validate_TableWithNoPrimaryKey_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "NoPk",
                    Columns = [new ColumnDefinition { Name = "Name", Type = "TEXT" }],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("no primary key"))).IsTrue();
    }

    [Test]
    public async Task Validate_DuplicateTableNames_HasError()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var result = _validator.Validate(schema);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Duplicate table name"))).IsTrue();
    }
}
