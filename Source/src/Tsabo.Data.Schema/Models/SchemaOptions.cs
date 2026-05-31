namespace Tsabo.Data.Schema;

public class SchemaOptions
{
    public bool DryRun { get; set; }
    public List<string> IgnoredTables { get; set; } = [];
    public List<string> IgnoredColumns { get; set; } = [];
    public bool CacheSchema { get; set; } = true;
}
