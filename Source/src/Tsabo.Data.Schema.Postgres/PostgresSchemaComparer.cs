using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.Postgres;

public sealed class PostgresSchemaComparer : ISchemaComparer
{
    public SchemaDiff Compare(SchemaDefinition current, SchemaDefinition target, SchemaOptions? options = null)
    {
        options ??= new SchemaOptions();
        var diff = new SchemaDiff();

        var currentTables = current.Tables.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var pendingForeignKeys = new List<MigrationOperation>();

        foreach (var table in target.Tables)
        {
            if (!currentTables.TryGetValue(table.Name, out var currentTable))
            {
                var isIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase);

                diff.Operations.Add(new MigrationOperation
                {
                    Type = MigrationOperationType.CreateTable,
                    TableName = table.Name,
                    Sql = GenerateCreateTableSql(table),
                    IsIgnored = isIgnored,
                });

                foreach (var item in table.Indexes)
                {
                    diff.Operations.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.CreateIndex,
                        TableName = table.Name,
                        IndexName = item.Name,
                        Sql = GenerateCreateIndexSql(table.Name, item),
                        IsIgnored = isIgnored,
                    });
                }

                foreach (var item in table.ForeignKeys)
                {
                    pendingForeignKeys.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.AddForeignKey,
                        TableName = table.Name,
                        Sql = GenerateAddForeignKeySql(table.Name, item),
                        IsIgnored = isIgnored,
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
                    else if (!string.Equals(NormalizeType(currentCol.Type), NormalizeType(item.Type), StringComparison.OrdinalIgnoreCase))
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

                var currentForeignKeys = currentTable.ForeignKeys.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in table.ForeignKeys)
                {
                    if (!currentForeignKeys.ContainsKey(item.Name))
                    {
                        pendingForeignKeys.Add(new MigrationOperation
                        {
                            Type = MigrationOperationType.AddForeignKey,
                            TableName = table.Name,
                            Sql = GenerateAddForeignKeySql(table.Name, item),
                            IsIgnored = options.IgnoredTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase),
                        });
                    }
                }
            }
        }

        diff.Operations.AddRange(pendingForeignKeys);

        return diff;
    }

    private static string GenerateCreateTableSql(TableDefinition table)
    {
        var lines = new List<string>();
        var pkCols = table.Columns.Where(p => p.IsPrimaryKey).ToList();

        foreach (var item in table.Columns)
        {
            var pgType = MapToPostgresType(item);
            var colDef = $"    \"{item.Name}\" {pgType}";

            if (item.IsPrimaryKey && pkCols.Count == 1)
                colDef += " PRIMARY KEY";

            if (item is { IsNullable: false, IsPrimaryKey: false })
                colDef += " NOT NULL";

            if (item is { IsAutoIncrement: false, DefaultValue: not null })
                colDef += $" DEFAULT {item.DefaultValue}";

            lines.Add(colDef);
        }

        if (pkCols.Count > 1)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", pkCols.Select(p => $"\"{p.Name}\""))})");

        return $"CREATE TABLE \"{table.Name}\" (\n{string.Join(",\n", lines)}\n);";
    }

    private static string GenerateAddColumnSql(string tableName, ColumnDefinition col)
    {
        var pgType = MapToPostgresType(col);
        var colDef = $"\"{col.Name}\" {pgType}";

        if (!col.IsNullable)
        {
            var defaultVal = col.DefaultValue ?? GetDefaultValueForType(col.Type);
            colDef += $" NOT NULL DEFAULT {defaultVal}";
        }
        else if (col.DefaultValue is not null)
            colDef += $" DEFAULT {col.DefaultValue}";

        return $"ALTER TABLE \"{tableName}\" ADD COLUMN {colDef};";
    }

    private static string GenerateAlterColumnSql(string tableName, ColumnDefinition col)
    {
        var nullability = col.IsNullable
            ? "DROP NOT NULL"
            : "SET NOT NULL";

        return $"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{col.Name}\" TYPE {col.Type}, ALTER COLUMN \"{col.Name}\" {nullability};";
    }

    private static string GenerateCreateIndexSql(string tableName, IndexDefinition index)
    {
        var unique = index.IsUnique
            ? "UNIQUE "
            : string.Empty;

        var cols = string.Join(", ", index.Columns.Select(p => $"\"{p}\""));
        return $"CREATE {unique}INDEX IF NOT EXISTS \"{index.Name}\" ON \"{tableName}\" ({cols});";
    }

    private static string GenerateAddForeignKeySql(string tableName, ForeignKeyDefinition fk) =>
        $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT \"{fk.Name}\" FOREIGN KEY (\"{fk.Column}\") REFERENCES \"{fk.ReferencedTable}\" (\"{fk.ReferencedColumn}\");";

    private static string MapToPostgresType(ColumnDefinition col)
    {
        if (col.IsAutoIncrement)
        {
            return col.Type.ToUpperInvariant() switch
            {
                "INTEGER" or "INT" or "INT4" => "SERIAL",
                "BIGINT" or "INT8" => "BIGSERIAL",
                "SMALLINT" or "INT2" => "SMALLSERIAL",
                var _ => "SERIAL",
            };
        }

        return col.Type;
    }

    private static string GetDefaultValueForType(string type)
    {
        var baseType = type.Split('(')[0].Trim().ToUpperInvariant();
        return baseType switch
        {
            "INTEGER" or "INT" or "INT4" or "BIGINT" or "INT8" or "SMALLINT" or "INT2" => "0",
            "NUMERIC" or "DECIMAL" or "FLOAT" or "FLOAT4" or "FLOAT8" or "REAL" or "DOUBLE PRECISION" => "0",
            "BOOLEAN" or "BOOL" => "false",
            "TIMESTAMP" or "TIMESTAMPTZ" or "DATE" => "NOW()",
            "UUID" => "gen_random_uuid()",
            var _ => "''",
        };
    }

    private static string NormalizeType(string type) =>
        type.ToUpperInvariant().Split('(')[0].Trim();
}
