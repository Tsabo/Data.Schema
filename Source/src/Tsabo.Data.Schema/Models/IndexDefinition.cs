namespace Tsabo.Data.Schema;

public class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public bool IsUnique { get; set; }
}
