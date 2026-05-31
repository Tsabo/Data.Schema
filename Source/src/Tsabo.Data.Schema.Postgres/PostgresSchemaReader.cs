using Npgsql;
using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.Postgres;

public sealed class PostgresSchemaReader : ISchemaReader
{
    private readonly string _connectionString;
    private readonly SchemaOptions _options;
    private SchemaDefinition? _cache;

    public PostgresSchemaReader(string connectionString, SchemaOptions? options = null)
    {
        _connectionString = connectionString;
        _options = options ?? new SchemaOptions();
    }

    public PostgresSchemaReader(NpgsqlConnection connection, SchemaOptions? options = null)
    {
        _connectionString = connection.ConnectionString;
        _options = options ?? new SchemaOptions();
    }

    public async Task<SchemaDefinition> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_options.CacheSchema && _cache is not null)
            return _cache;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var schema = new SchemaDefinition();
        var tableNames = await GetTableNamesAsync(connection, cancellationToken);

        foreach (var item in tableNames)
        {
            if (_options.IgnoredTables.Contains(item, StringComparer.OrdinalIgnoreCase))
                continue;

            var table = new TableDefinition { Name = item };
            table.Columns.AddRange(await GetColumnsAsync(connection, item, cancellationToken));
            table.Indexes.AddRange(await GetIndexesAsync(connection, item, cancellationToken));
            table.ForeignKeys.AddRange(await GetForeignKeysAsync(connection, item, cancellationToken));
            schema.Tables.Add(table);
        }

        if (_options.CacheSchema)
            _cache = schema;

        return schema;
    }

    private static async Task<List<string>> GetTableNamesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));

        return tables;
    }

    private async Task<List<ColumnDefinition>> GetColumnsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                c.ordinal_position - 1,
                c.column_name,
                CASE
                    WHEN c.character_maximum_length IS NOT NULL
                    THEN c.udt_name || '(' || c.character_maximum_length || ')'
                    WHEN c.numeric_precision IS NOT NULL AND c.numeric_scale IS NOT NULL
                         AND c.data_type NOT IN ('integer','bigint','smallint')
                    THEN c.udt_name || '(' || c.numeric_precision || ',' || c.numeric_scale || ')'
                    ELSE c.udt_name
                END AS data_type,
                c.is_nullable,
                c.column_default,
                EXISTS (
                    SELECT 1 FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                      AND tc.table_name = c.table_name
                      AND kcu.column_name = c.column_name
                ) AS is_pk
            FROM information_schema.columns c
            WHERE c.table_name = @table
              AND c.table_schema = 'public'
            ORDER BY c.ordinal_position
            """;

        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            if (_options.IgnoredColumns.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var defaultValue = reader.IsDBNull(4)
                ? null
                : reader.GetString(4);

            var isAutoIncrement = defaultValue is not null &&
                                  (defaultValue.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase) ||
                                   defaultValue.StartsWith("gen_random_uuid(", StringComparison.OrdinalIgnoreCase));

            columns.Add(new ColumnDefinition
            {
                OrdinalPosition = reader.GetInt32(0),
                Name = name,
                Type = reader.GetString(2),
                IsNullable = reader.GetString(3) == "YES",
                DefaultValue = defaultValue,
                IsAutoIncrement = isAutoIncrement,
                IsPrimaryKey = reader.GetBoolean(5),
            });
        }

        return columns;
    }

    private static async Task<List<IndexDefinition>> GetIndexesAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                i.relname AS index_name,
                ix.indisunique AS is_unique,
                a.attname AS column_name
            FROM pg_class t
            JOIN pg_index ix ON ix.indrelid = t.oid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE t.relname = @table
              AND NOT ix.indisprimary
            ORDER BY i.relname, a.attnum
            """;

        cmd.Parameters.AddWithValue("@table", tableName);

        var indexMap = new Dictionary<string, IndexDefinition>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!indexMap.TryGetValue(name, out var idx))
            {
                idx = new IndexDefinition { Name = name, IsUnique = reader.GetBoolean(1) };
                indexMap[name] = idx;
                indexes.Add(idx);
            }

            idx.Columns.Add(reader.GetString(2));
        }

        return indexes;
    }

    private static async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeyDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                tc.constraint_name,
                kcu.column_name,
                ccu.table_name AS foreign_table_name,
                ccu.column_name AS foreign_column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_name = @table
              AND tc.table_schema = 'public'
            """;

        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add(new ForeignKeyDefinition
            {
                Name = reader.GetString(0),
                Column = reader.GetString(1),
                ReferencedTable = reader.GetString(2),
                ReferencedColumn = reader.GetString(3),
            });
        }

        return foreignKeys;
    }
}
