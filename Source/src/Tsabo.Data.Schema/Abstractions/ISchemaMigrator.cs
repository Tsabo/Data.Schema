namespace Tsabo.Data.Schema.Abstractions;

public interface ISchemaMigrator
{
    Task<MigrationResult> MigrateAsync(SchemaDiff diff, SchemaOptions? options = null, CancellationToken cancellationToken = default);
}
