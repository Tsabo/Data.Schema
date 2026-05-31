using System.Text.Json;

namespace Tsabo.Data.Schema.Serialization;

public static class SchemaSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(SchemaDefinition schema) =>
        JsonSerializer.Serialize(schema, _options);

    public static SchemaDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize<SchemaDefinition>(json, _options);

    public static async Task ExportToFileAsync(SchemaDefinition schema, string filePath, CancellationToken cancellationToken = default)
    {
        var json = ToJson(schema);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public static async Task<SchemaDefinition?> ImportFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return FromJson(json);
    }
}
