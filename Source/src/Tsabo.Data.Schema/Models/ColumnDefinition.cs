namespace Tsabo.Data.Schema;

public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsAutoIncrement { get; set; }
    public string? DefaultValue { get; set; }
    public int OrdinalPosition { get; set; }
}
