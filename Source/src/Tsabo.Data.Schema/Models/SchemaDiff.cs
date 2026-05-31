namespace Tsabo.Data.Schema;

public class SchemaDiff
{
    public List<MigrationOperation> Operations { get; set; } = [];
    public bool HasChanges => Operations.Count > 0;
}
