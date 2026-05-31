namespace Tsabo.Data.Schema;

public class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ColumnDefinition> Columns { get; set; } = [];
    public List<IndexDefinition> Indexes { get; set; } = [];
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = [];
}
