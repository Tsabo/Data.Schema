namespace Tsabo.Data.Schema.Abstractions;

public interface ISchemaReader
{
    Task<SchemaDefinition> ReadAsync(CancellationToken cancellationToken = default);
}
