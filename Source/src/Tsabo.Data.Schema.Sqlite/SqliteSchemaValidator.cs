using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.Sqlite;

public sealed class SqliteSchemaValidator : ISchemaValidator
{
    private static readonly HashSet<string> _validTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INTEGER", "INT", "BIGINT", "SMALLINT", "TINYINT",
        "REAL", "FLOAT", "DOUBLE", "NUMERIC", "DECIMAL",
        "TEXT", "CHAR", "VARCHAR", "NVARCHAR", "CLOB",
        "BLOB", "BOOLEAN", "DATE", "DATETIME", "TIMESTAMP",
        "GUID",
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

            if (table.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                result.Errors.Add($"Table name '{table.Name}' uses the reserved 'sqlite_' prefix.");

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
                    result.Errors.Add($"Unsupported SQLite type '{item.Type}' for column '{table.Name}.{item.Name}'.");
            }

            var indexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in table.Indexes)
            {
                if (!indexNames.Add(item.Name))
                    result.Errors.Add($"Duplicate index name '{item.Name}' in table '{table.Name}'.");
            }
        }

        // FK validation (second pass — all tables known)
        var allTableNames = new HashSet<string>(schema.Tables.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var table in schema.Tables)
        {
            foreach (var item in table.ForeignKeys)
            {
                if (item.ReferencedTable.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Table '{table.Name}' has a self-referencing foreign key '{item.Name}'. Self-referencing FKs are not supported.");
                    continue;
                }

                if (!allTableNames.Contains(item.ReferencedTable))
                    result.Errors.Add($"Foreign key '{item.Name}' in table '{table.Name}' references non-existent table '{item.ReferencedTable}'.");
            }
        }

        DetectCircularForeignKeys(schema, result);

        return result;
    }

    private static void DetectCircularForeignKeys(SchemaDefinition schema, ValidationResult result)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in schema.Tables)
        {
            if (!graph.ContainsKey(t.Name))
                graph[t.Name] = t.ForeignKeys.Select(fk => fk.ReferencedTable).ToList();
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in schema.Tables)
        {
            if (!visited.Contains(item.Name))
                DetectCycle(item.Name, graph, visited, stack, result);
        }
    }

    private static void DetectCycle(string table, Dictionary<string, List<string>> graph, HashSet<string> visited, HashSet<string> stack, ValidationResult result)
    {
        visited.Add(table);
        stack.Add(table);

        if (graph.TryGetValue(table, out var neighbors))
        {
            foreach (var item in neighbors)
            {
                if (!visited.Contains(item))
                    DetectCycle(item, graph, visited, stack, result);
                else if (stack.Contains(item))
                    result.Errors.Add($"Circular foreign key detected involving tables '{table}' and '{item}'.");
            }
        }

        stack.Remove(table);
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.All(p => char.IsLetterOrDigit(p) || p == '_');
    }
}
