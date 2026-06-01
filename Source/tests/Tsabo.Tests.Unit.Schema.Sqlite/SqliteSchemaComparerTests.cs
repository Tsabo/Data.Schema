using Tsabo.Data.Schema;
using Tsabo.Data.Schema.Sqlite;

namespace Tsabo.Tests.Unit.Schema.Sqlite;

public class SqliteSchemaComparerTests
{
    private readonly SqliteSchemaComparer _comparer = new();

    [Test]
    public async Task Compare_EmptySchemas_ReturnsNoDiff()
    {
        var diff = _comparer.Compare(new SchemaDefinition(), new SchemaDefinition());

        await Assert.That(diff.HasChanges).IsFalse();
    }

    [Test]
    public async Task Compare_NewTableInTarget_GeneratesCreateTable()
    {
        var current = new SchemaDefinition();
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true, IsAutoIncrement = true },
                        new ColumnDefinition { Name = "Name", Type = "TEXT", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = _comparer.Compare(current, target);

        await Assert.That(diff.HasChanges).IsTrue();
        await Assert.That(diff.Operations).Count().IsEqualTo(1);
        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.CreateTable);
        await Assert.That(diff.Operations[0].Sql).Contains("CREATE TABLE");
        await Assert.That(diff.Operations[0].Sql).Contains("AUTOINCREMENT");
    }

    [Test]
    public async Task Compare_NewColumnInTarget_GeneratesAddColumn()
    {
        var current = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Email", Type = "TEXT", IsNullable = false },
                    ],
                },
            ],
        };

        var diff = _comparer.Compare(current, target);

        await Assert.That(diff.HasChanges).IsTrue();
        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.AddColumn);
        await Assert.That(diff.Operations[0].Sql).Contains("ADD COLUMN");
    }

    [Test]
    public async Task Compare_NewIndexInTarget_GeneratesCreateIndex()
    {
        var column = new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true };
        var current = new SchemaDefinition
        {
            Tables = [new TableDefinition { Name = "Users", Columns = [column] }],
        };

        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [column],
                    Indexes = [new IndexDefinition { Name = "IX_Users_Email", Columns = ["Email"], IsUnique = true }],
                },
            ],
        };

        var diff = _comparer.Compare(current, target);

        await Assert.That(diff.Operations).Count().IsEqualTo(1);
        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.CreateIndex);
        await Assert.That(diff.Operations[0].Sql).Contains("UNIQUE");
    }

    [Test]
    public async Task Compare_RemovedTableInTarget_GeneratesDropTable()
    {
        var current = new SchemaDefinition
        {
            Tables = [new TableDefinition { Name = "OldTable", Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }] }],
        };

        var diff = _comparer.Compare(current, new SchemaDefinition());

        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.DropTable);
    }

    [Test]
    public async Task Compare_TypeChange_GeneratesWarning()
    {
        var current = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Score", Type = "INTEGER" },
                    ],
                },
            ],
        };

        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "Score", Type = "REAL" },
                    ],
                },
            ],
        };

        var diff = _comparer.Compare(current, target);

        await Assert.That(diff.Operations[0].Type).IsEqualTo(MigrationOperationType.ModifyColumn);
        await Assert.That(diff.Operations[0].Warning).IsNotNull();
        await Assert.That(diff.Operations[0].Warning).Contains("SQLite does not support ALTER COLUMN");
    }

    [Test]
    public async Task Compare_MultipleChanges_GeneratesAllOperations()
    {
        var current = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var target = new SchemaDefinition
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
                    Indexes = [new IndexDefinition { Name = "IX_Users_Name", Columns = ["Name"] }],
                },
                new TableDefinition
                {
                    Name = "NewTable",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var diff = _comparer.Compare(current, target);

        await Assert.That(diff.Operations.Count).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task Compare_IgnoredTable_AllOperationsMarkedIgnored()
    {
        var current = new SchemaDefinition();
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Logs",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                    Indexes = [new IndexDefinition { Name = "IX_Logs_Id", Columns = ["Id"] }],
                },
            ],
        };

        var options = new SchemaOptions { IgnoredTables = ["Logs"] };
        var diff = _comparer.Compare(current, target, options);

        await Assert.That(diff.Operations).Count().IsGreaterThan(0);
        await Assert.That(diff.Operations.All(p => p.IsIgnored)).IsTrue();
        await Assert.That(diff.HasChanges).IsFalse();
    }

    [Test]
    public async Task Compare_IgnoredColumn_OperationMarkedIgnored()
    {
        var current = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Users",
                    Columns =
                    [
                        new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true },
                        new ColumnDefinition { Name = "CreatedAt", Type = "TEXT" },
                    ],
                },
            ],
        };

        var options = new SchemaOptions { IgnoredColumns = ["CreatedAt"] };
        var diff = _comparer.Compare(current, target, options);

        await Assert.That(diff.Operations).Count().IsEqualTo(1);
        await Assert.That(diff.Operations[0].ColumnName).IsEqualTo("CreatedAt");
        await Assert.That(diff.Operations[0].IsIgnored).IsTrue();
        await Assert.That(diff.HasChanges).IsFalse();
    }

    [Test]
    public async Task Compare_MixedIgnoredAndNonIgnored_HasChangesIsTrue()
    {
        var current = new SchemaDefinition();
        var target = new SchemaDefinition
        {
            Tables =
            [
                new TableDefinition
                {
                    Name = "Logs",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
                new TableDefinition
                {
                    Name = "Users",
                    Columns = [new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true }],
                },
            ],
        };

        var options = new SchemaOptions { IgnoredTables = ["Logs"] };
        var diff = _comparer.Compare(current, target, options);

        await Assert.That(diff.Operations.Any(p => p.IsIgnored)).IsTrue();
        await Assert.That(diff.Operations.Any(p => !p.IsIgnored)).IsTrue();
        await Assert.That(diff.HasChanges).IsTrue();
    }
}
