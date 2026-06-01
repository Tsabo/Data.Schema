namespace Tsabo.Data.Schema;

public class MigrationOperation
{
    public MigrationOperationType Type { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string? IndexName { get; set; }
    public string Sql { get; set; } = string.Empty;
    public string? Warning { get; set; }
    public bool IsIgnored { get; set; }
}
