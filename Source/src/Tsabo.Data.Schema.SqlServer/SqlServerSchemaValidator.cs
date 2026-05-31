using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.SqlServer;

public sealed class SqlServerSchemaValidator : ISchemaValidator
{
    private static readonly HashSet<string> _validTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIGINT", "BINARY", "BIT", "CHAR", "DATE", "DATETIME", "DATETIME2",
        "DATETIMEOFFSET", "DECIMAL", "FLOAT", "IMAGE", "INT", "INTEGER",
        "MONEY", "NCHAR", "NTEXT", "NUMERIC", "NVARCHAR", "REAL",
        "ROWVERSION", "SMALLDATETIME", "SMALLINT", "SMALLMONEY", "SQL_VARIANT",
        "TEXT", "TIME", "TIMESTAMP", "TINYINT", "UNIQUEIDENTIFIER",
        "VARBINARY", "VARCHAR", "XML",
    };

    public ValidationResult Validate(SchemaDefinition schema)
    {
        var result = new ValidationResult();

        if (schema.Tables.Count == 0)
            return result;

        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            if (!tableNames.Add(table.Name))
                result.Errors.Add($"Duplicate table name: '{table.Name}'.");

            if (!IsValidIdentifier(table.Name))
                result.Errors.Add($"Invalid table name: '{table.Name}'.");

            var hasPrimaryKey = table.Columns.Any(p => p.IsPrimaryKey);
            if (!hasPrimaryKey)
                result.Errors.Add($"Table '{table.Name}' has no primary key.");

            var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in table.Columns)
            {
                if (!colNames.Add(item.Name))
                    result.Errors.Add($"Duplicate column name '{item.Name}' in table '{table.Name}'.");

                if (!IsValidIdentifier(item.Name))
                    result.Errors.Add($"Invalid column name '{item.Name}' in table '{table.Name}'.");

                var baseType = item.Type.Split('(')[0].Trim();
                if (!_validTypes.Contains(baseType))
                    result.Errors.Add($"Unsupported SQL Server type '{item.Type}' for column '{table.Name}.{item.Name}'.");
            }

            var indexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in table.Indexes)
            {
                if (!indexNames.Add(item.Name))
                    result.Errors.Add($"Duplicate index name '{item.Name}' in table '{table.Name}'.");
            }
        }

        var allTableNames = new HashSet<string>(schema.Tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            foreach (var item in table.ForeignKeys)
            {
                if (item.ReferencedTable.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Table '{table.Name}' has a self-referencing foreign key '{item.Name}'.");
                    continue;
                }

                if (!allTableNames.Contains(item.ReferencedTable))
                    result.Errors.Add($"Foreign key '{item.Name}' in table '{table.Name}' references non-existent table '{item.ReferencedTable}'.");
            }
        }

        return result;
    }

    private static bool IsValidIdentifier(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(p => char.IsLetterOrDigit(p) || p == '_');
}
