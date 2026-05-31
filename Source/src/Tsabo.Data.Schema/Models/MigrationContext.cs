namespace Tsabo.Data.Schema;

public class MigrationContext
{
    public SchemaDiff Diff { get; init; } = new();
    public SchemaOptions Options { get; init; } = new();
    public List<MigrationOperation> CompletedOperations { get; } = [];
}
