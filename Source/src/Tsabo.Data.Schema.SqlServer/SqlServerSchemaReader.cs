using Microsoft.Data.SqlClient;
using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.SqlServer;

public sealed class SqlServerSchemaReader : ISchemaReader
{
    private readonly string _connectionString;
    private readonly SchemaOptions _options;
    private SchemaDefinition? _cache;

    public SqlServerSchemaReader(string connectionString, SchemaOptions? options = null)
    {
        _connectionString = connectionString;
        _options = options ?? new SchemaOptions();
    }

    public SqlServerSchemaReader(SqlConnection connection, SchemaOptions? options = null)
    {
        _connectionString = connection.ConnectionString;
        _options = options ?? new SchemaOptions();
    }

    public async Task<SchemaDefinition> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_options.CacheSchema && _cache is not null)
            return _cache;

        await using var connection = new SqlConnection(_connectionString);
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

    private static async Task<List<string>> GetTableNamesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA = 'dbo'
            ORDER BY TABLE_NAME
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));

        return tables;
    }

    private async Task<List<ColumnDefinition>> GetColumnsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                c.ORDINAL_POSITION - 1,
                c.COLUMN_NAME,
                CASE
                    WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL
                         AND c.CHARACTER_MAXIMUM_LENGTH != -1
                    THEN c.DATA_TYPE + '(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) + ')'
                    WHEN c.CHARACTER_MAXIMUM_LENGTH = -1
                    THEN c.DATA_TYPE + '(MAX)'
                    ELSE c.DATA_TYPE
                END AS DATA_TYPE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                COLUMNPROPERTY(OBJECT_ID(@table), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND tc.TABLE_NAME = @table
            ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.TABLE_NAME = @table
              AND c.TABLE_SCHEMA = 'dbo'
            ORDER BY c.ORDINAL_POSITION
            """;

        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            if (_options.IgnoredColumns.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            columns.Add(new ColumnDefinition
            {
                OrdinalPosition = reader.GetInt32(0),
                Name = name,
                Type = reader.GetString(2),
                IsNullable = reader.GetString(3) == "YES",
                DefaultValue = reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),
                IsAutoIncrement = reader.GetInt32(5) == 1,
                IsPrimaryKey = reader.GetInt32(6) == 1,
            });
        }

        return columns;
    }

    private static async Task<List<IndexDefinition>> GetIndexesAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                i.name AS INDEX_NAME,
                i.is_unique AS IS_UNIQUE,
                c.name AS COLUMN_NAME
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@table)
              AND i.is_primary_key = 0
              AND i.type > 0
            ORDER BY i.name, ic.key_ordinal
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

    private static async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeyDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                fk.name AS FK_NAME,
                c.name AS COLUMN_NAME,
                pt.name AS REFERENCED_TABLE,
                pc.name AS REFERENCED_COLUMN
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            JOIN sys.tables pt ON pt.object_id = fkc.referenced_object_id
            JOIN sys.columns pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(@table)
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
