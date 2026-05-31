using Tsabo.Data.Schema;
using Tsabo.Data.Schema.SqlServer;

namespace Tsabo.Tests.Unit.Schema.SqlServer;

public class SqlServerSchemaComparerTests
{
    [Test]
    public async Task Compare_EmptySchemas_ReturnsNoDiff()
    {
        var comparer = new SqlServerSchemaComparer();
        var diff = comparer.Compare(new SchemaDefinition(), new SchemaDefinition());

        await Assert.That(diff.HasChanges).IsFalse();
    }

    [Test]
    public async Task Compare_NewTableInTarget_GeneratesCreateTable()
    {
        var comparer = new SqlServerSchemaComparer();
        var current = new SchemaDefinition();
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "TestTable",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INT", IsPrimaryKey = true, IsAutoIncrement = true },
                        new ColumnDefinition { Name = "Name", Type = "NVARCHAR(200)", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = comparer.Compare(current, target);

        await Assert.That(diff.HasChanges).IsTrue();
        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.CreateTable);
        await Assert.That(diff.Operations[0].Sql).Contains("IDENTITY");
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
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INT", IsPrimaryKey = true, IsAutoIncrement = true },
                        new ColumnDefinition { Name = "Email", Type = "NVARCHAR(200)", IsNullable = false },
                    ],
                },
            ],
        };

        var validator = new SqlServerSchemaValidator();
        var result = validator.Validate(schema);

        await Assert.That(result.IsValid).IsTrue();
    }
}
