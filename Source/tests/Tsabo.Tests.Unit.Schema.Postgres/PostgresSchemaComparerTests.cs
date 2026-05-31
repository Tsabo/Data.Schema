using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Postgres;

namespace Tsabo.Tests.Unit.Schema.Postgres;

public class PostgresSchemaComparerTests
{
    [Test]
    public async Task Compare_EmptySchemas_ReturnsNoDiff()
    {
        var comparer = new PostgresSchemaComparer();
        var diff = comparer.Compare(new SchemaDefinition(), new SchemaDefinition());

        await Assert.That(diff.HasChanges).IsFalse();
    }

    [Test]
    public async Task Compare_NewTableInTarget_GeneratesCreateTable()
    {
        var comparer = new PostgresSchemaComparer();
        var current = new SchemaDefinition();
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "test_table",
                    Columns =
                    [
                        new ColumnDefinition { Name = "id", Type = "integer", IsPrimaryKey = true, IsAutoIncrement = true },
                        new ColumnDefinition { Name = "name", Type = "varchar(200)", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = comparer.Compare(current, target);

        await Assert.That(diff.HasChanges).IsTrue();
        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.CreateTable);
        await Assert.That(diff.Operations[0].Sql).Contains("SERIAL");
    }

    [Test]
    public async Task SchemaValidator_ValidSchema_IsValid()
    {
        var schema = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "id", Type = "INTEGER", IsPrimaryKey = true, IsAutoIncrement = true },
                        new ColumnDefinition { Name = "email", Type = "TEXT", IsNullable = false },
                    ],
                },
            ],
        };

        var validator = new PostgresSchemaValidator();
        var result = validator.Validate(schema);

        await Assert.That(result.IsValid).IsTrue();
    }
}
