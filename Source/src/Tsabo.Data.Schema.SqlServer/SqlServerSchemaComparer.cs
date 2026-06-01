using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.SqlServer;

public sealed class SqlServerSchemaComparer : ISchemaComparer
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

                foreach (var item in table.ForeignKeys)
                {
                    diff.Operations.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.AddForeignKey,
                        TableName = table.Name,
                        Sql = GenerateAddForeignKeySql(table.Name, item),
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
                            Sql = GenerateAlterColumnSql(table.Name, item),
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
                    Sql = $"DROP TABLE IF EXISTS [{item.Name}];",
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
            var colDef = $"    [{item.Name}] {item.Type}";

            if (item.IsAutoIncrement)
                colDef += " IDENTITY(1,1)";

            if (item.IsPrimaryKey && pkCols.Count == 1)
                colDef += " PRIMARY KEY";

            if (item is { IsNullable: false, IsPrimaryKey: false })
                colDef += " NOT NULL";
            else if (item.IsNullable)
                colDef += " NULL";

            if (item.DefaultValue is not null)
                colDef += $" DEFAULT {item.DefaultValue}";

            lines.Add(colDef);
        }

        if (pkCols.Count > 1)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", pkCols.Select(p => $"[{p.Name}]"))})");

        return $"CREATE TABLE [{table.Name}] (\n{string.Join(",\n", lines)}\n);";
    }

    private static string GenerateAddColumnSql(string tableName, ColumnDefinition col)
    {
        var colDef = $"[{col.Name}] {col.Type}";

        if (!col.IsNullable)
        {
            var defaultVal = col.DefaultValue ?? GetDefaultValueForType(col.Type);
            colDef += $" NOT NULL DEFAULT {defaultVal}";
        }
        else
        {
            colDef += " NULL";
            if (col.DefaultValue is not null)
                colDef += $" DEFAULT {col.DefaultValue}";
        }

        return $"ALTER TABLE [{tableName}] ADD {colDef};";
    }

    private static string GenerateAlterColumnSql(string tableName, ColumnDefinition col)
    {
        var nullability = col.IsNullable
            ? "NULL"
            : "NOT NULL";

        return $"ALTER TABLE [{tableName}] ALTER COLUMN [{col.Name}] {col.Type} {nullability};";
    }

    private static string GenerateCreateIndexSql(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique
            ? "UNIQUE "
            : string.Empty;

        var cols = string.Join(", ", index.Columns.Select(p => $"[{p}]"));
        return $"CREATE {unique}INDEX [{index.Name}] ON [{tableName}] ({cols});";
    }

    private static string GenerateAddForeignKeySql(string tableName, ForeignKeyDefinition fk) =>
        $"ALTER TABLE [{tableName}] ADD CONSTRAINT [{fk.Name}] FOREIGN KEY ([{fk.Column}]) REFERENCES [{fk.ReferencedTable}] ([{fk.ReferencedColumn}]);";

    private static string GetDefaultValueForType(string type)
    {
        var baseType = type.Split('(')[0].Trim().ToUpperInvariant();
        return baseType switch
        {
            "INT" or "BIGINT" or "SMALLINT" or "TINYINT" or "INTEGER" => "0",
            "DECIMAL" or "NUMERIC" or "FLOAT" or "REAL" or "MONEY" or "SMALLMONEY" => "0",
            "BIT" => "0",
            "DATETIME" or "DATETIME2" or "DATE" or "TIME" or "DATETIMEOFFSET" => "GETUTCDATE()",
            "UNIQUEIDENTIFIER" => "NEWID()",
            var _ => "''",
        };
    }
}
