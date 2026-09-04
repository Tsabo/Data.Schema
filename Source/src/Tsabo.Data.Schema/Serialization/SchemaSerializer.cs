using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tsabo.Data.Schema.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SchemaDefinition))]
internal partial class SchemaJsonContext : JsonSerializerContext;

public static class SchemaSerializer
{
    public static string ToJson(SchemaDefinition schema) =>
        JsonSerializer.Serialize(schema, SchemaJsonContext.Default.SchemaDefinition);

    public static SchemaDefinition? FromJson(string json) =>
        JsonSerializer.Deserialize(json, SchemaJsonContext.Default.SchemaDefinition);

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
