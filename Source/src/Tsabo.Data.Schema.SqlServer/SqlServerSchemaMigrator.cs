using Microsoft.Data.SqlClient;
using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.SqlServer;

public sealed class SqlServerSchemaMigrator : ISchemaMigrator
{
    private readonly string _connectionString;
    private readonly IEnumerable<IMigrationHook> _hooks;

    public SqlServerSchemaMigrator(string connectionString, IEnumerable<IMigrationHook>? hooks = null)
    {
        _connectionString = connectionString;
        _hooks = hooks ?? [];
    }

    public SqlServerSchemaMigrator(SqlConnection connection, IEnumerable<IMigrationHook>? hooks = null)
    {
        _connectionString = connection.ConnectionString;
        _hooks = hooks ?? [];
    }

    public async Task<MigrationResult> MigrateAsync(SchemaDiff diff, SchemaOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SchemaOptions();

        if (!diff.HasChanges)
            return new MigrationResult(true, [], []);

        var context = new MigrationContext { Diff = diff, Options = options };
        var warnings = new List<string>();

        foreach (var item in diff.Operations.Where(p => p.Warning is not null && !p.IsIgnored))
            warnings.Add(item.Warning!);

        if (options.DryRun)
            return new MigrationResult(true, diff.Operations, warnings);

        foreach (var item in _hooks)
            await item.BeforeAsync(context, cancellationToken);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var item in diff.Operations.Where(p => !p.IsIgnored))
            {
                if (string.IsNullOrWhiteSpace(item.Sql))
                    continue;

                await using var cmd = connection.CreateCommand();
                cmd.Transaction = (SqlTransaction)transaction;
                cmd.CommandText = item.Sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                context.CompletedOperations.Add(item);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new MigrationResult(false, context.CompletedOperations, warnings, ex);
        }

        foreach (var item in _hooks)
            await item.AfterAsync(context, cancellationToken);

        return new MigrationResult(true, diff.Operations, warnings);
    }
}
