using Microsoft.Data.Sqlite;
using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.Sqlite;

public sealed class SqliteSchemaReader : ISchemaReader
{
    private readonly string _connectionString;
    private readonly SchemaOptions _options;
    private SchemaDefinition? _cache;

    public SqliteSchemaReader(string connectionString, SchemaOptions? options = null)
    {
        _connectionString = connectionString;
        _options = options ?? new SchemaOptions();
    }

    public SqliteSchemaReader(SqliteConnection connection, SchemaOptions? options = null)
    {
        _connectionString = connection.ConnectionString;
        _options = options ?? new SchemaOptions();
    }

    public async Task<SchemaDefinition> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_options.CacheSchema && _cache is not null)
            return _cache;

        await using var connection = new SqliteConnection(_connectionString);
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

    private static async Task<List<string>> GetTableNamesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));

        return tables;
    }

    private async Task<List<ColumnDefinition>> GetColumnsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";

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
                IsNullable = reader.GetInt32(3) == 0,
                DefaultValue = reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),
                IsPrimaryKey = reader.GetInt32(5) > 0,
            });
        }

        // Detect AUTOINCREMENT via sqlite_master
        await using var autoCmd = connection.CreateCommand();
        autoCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@table";
        autoCmd.Parameters.AddWithValue("@table", tableName);
        var createSql = (string?)await autoCmd.ExecuteScalarAsync(cancellationToken) ?? string.Empty;

        foreach (var item in columns.Where(p => p.IsPrimaryKey && p.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)))
            item.IsAutoIncrement = createSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase);

        return columns;
    }

    private static async Task<List<IndexDefinition>> GetIndexesAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var indexes = new List<IndexDefinition>();
        await using var listCmd = connection.CreateCommand();
        listCmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";

        var indexNames = new List<(string Name, bool IsUnique)>();
        await using (var reader = await listCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var origin = reader.GetString(3);
                if (origin == "pk")
                    continue;

                indexNames.Add((reader.GetString(1), reader.GetInt32(2) == 1));
            }
        }

        foreach (var (name, isUnique) in indexNames)
        {
            var columns = new List<string>();
            await using var infoCmd = connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info(\"{name}\")";
            await using var infoReader = await infoCmd.ExecuteReaderAsync(cancellationToken);
            while (await infoReader.ReadAsync(cancellationToken))
                columns.Add(infoReader.GetString(2));

            indexes.Add(new IndexDefinition { Name = name, IsUnique = isUnique, Columns = columns });
        }

        return indexes;
    }

    private static async Task<List<ForeignKeyDefinition>> GetForeignKeysAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeyDefinition>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\")";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add(new ForeignKeyDefinition
            {
                Name = $"FK_{tableName}_{reader.GetString(2)}_{reader.GetString(3)}",
                Column = reader.GetString(3),
                ReferencedTable = reader.GetString(2),
                ReferencedColumn = reader.GetString(4),
            });
        }

        return foreignKeys;
    }
}
