using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.Sqlite;

public sealed class SqliteSchemaComparer : ISchemaComparer
{
    public SchemaDiff Compare(SchemaDefinition current, SchemaDefinition target, SchemaOptions? options = null)
    {
        options ??= new SchemaOptions();
        var diff = new SchemaDiff();

        var currentTables = current.Tables.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var targetTables = target.Tables.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var table in target.Tables)
        {
            if (!currentTables.TryGetValue(table.Name, out var currentTable))
            {
                diff.Operations.Add(new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = table.Name,
                    Sql = GenerateCreateTableSql(table),
                    IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase),
                });

                foreach (var item in table.Indexes)
                {
                    diff.Operations.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.CreateIndex,
                        TableName = table.Name,
                        IndexName = item.Name,
                        Sql = GenerateCreateIndexSql(table.Name, item),
                        IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase),
                    });
                }
            }
            else
            {
                var currentColumns = currentTable.Columns.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in table.Columns)
                {
                    if (!currentColumns.TryGetValue(item.Name, out var currentCol))
                    {
                        diff.Operations.Add(new MigrationOperation
                        {
                            Type = MigrationOperationType.AddColumn,
                            TableName = table.Name,
                            ColumnName = item.Name,
                            Sql = GenerateAddColumnSql(table.Name, item),
                            IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase)
                                        || options.IgnoredColumns.Contains(item.Name, StringComparer.OrdinalIgnoreCase),
                        });
                    }
                    else if (!string.Equals(currentCol.Type, item.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        diff.Operations.Add(new MigrationOperation
                        {
                            Type = MigrationOperationType.ModifyColumn,
                            TableName = table.Name,
                            ColumnName = item.Name,
                            Sql = string.Empty,
                            Warning = $"SQLite does not support ALTER COLUMN. Column '{table.Name}.{item.Name}' type change from '{currentCol.Type}' to '{item.Type}' requires a table rebuild.",
                            IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase)
                                        || options.IgnoredColumns.Contains(item.Name, StringComparer.OrdinalIgnoreCase),
                        });
                    }
                }

                var currentIndexes = currentTable.Indexes.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in table.Indexes)
                {
                    if (!currentIndexes.ContainsKey(item.Name))
                    {
                        diff.Operations.Add(new MigrationOperation
                        {
                            Type = MigrationOperationType.CreateIndex,
                            TableName = table.Name,
                            IndexName = item.Name,
                            Sql = GenerateCreateIndexSql(table.Name, item),
                            IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase),
                        });
                    }
                }
            }
        }

        foreach (var item in current.Tables)
        {
            if (!targetTables.ContainsKey(item.Name))
            {
                diff.Operations.Add(new MigrationOperation
                {
                    Type = MigrationOperationType.DropTable,
                    TableName = item.Name,
                    Sql = $"DROP TABLE IF EXISTS \"{item.Name}\";",
                    IsIgnored = options.IgnoredTables.Contains(item.Name, StringComparer.OrdinalIgnoreCase),
                });
            }
        }

        return diff;
    }

    private static string GenerateCreateTableSql(TableDefinition table)
    {
        var lines = new List<string>();
        var pkCols = table.Columns.Where(p => p.IsPrimaryKey).ToList();

        foreach (var item in table.Columns)
        {
            var colDef = $"    \"{item.Name}\" {item.Type}";

            if (item.IsPrimaryKey && pkCols.Count == 1)
            {
                colDef += " PRIMARY KEY";
                if (item.IsAutoIncrement && item.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                    colDef += " AUTOINCREMENT";
            }

            if (item is { IsNullable: false, IsPrimaryKey: false })
                colDef += " NOT NULL";

            if (item.DefaultValue is not null)
                colDef += $" DEFAULT {item.DefaultValue}";

            lines.Add(colDef);
        }

        if (pkCols.Count > 1)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", pkCols.Select(p => $"\"{p.Name}\""))})");

        foreach (var item in table.ForeignKeys)
            lines.Add($"    FOREIGN KEY (\"{item.Column}\") REFERENCES \"{item.ReferencedTable}\" (\"{item.ReferencedColumn}\")");

        return $"CREATE TABLE \"{table.Name}\" (\n{string.Join(",\n", lines)}\n);";
    }

    private static string GenerateAddColumnSql(string tableName, ColumnDefinition col)
    {
        var colDef = $"\"{col.Name}\" {col.Type}";

        if (!col.IsNullable)
        {
            var defaultVal = col.DefaultValue ?? GetDefaultValueForType(col.Type);
            colDef += $" NOT NULL DEFAULT {defaultVal}";
        }
        else if (col.DefaultValue is not null)
            colDef += $" DEFAULT {col.DefaultValue}";

        return $"ALTER TABLE \"{tableName}\" ADD COLUMN {colDef};";
    }

    private static string GenerateCreateIndexSql(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique
            ? "UNIQUE "
            : string.Empty;

        var cols = string.Join(", ", index.Columns.Select(p => $"\"{p}\""));
        return $"CREATE {unique}INDEX IF NOT EXISTS \"{index.Name}\" ON \"{tableName}\" ({cols});";
    }

    private static string GetDefaultValueForType(string type) =>
        type.ToUpperInvariant() switch
        {
            "INTEGER" or "INT" or "BIGINT" or "SMALLINT" => "0",
            "REAL" or "FLOAT" or "DOUBLE" or "NUMERIC" => "0.0",
            "BOOLEAN" => "0",
            var _ => "''",
        };
}
