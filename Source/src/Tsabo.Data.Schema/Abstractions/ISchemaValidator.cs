namespace Tsabo.Data.Schema.Abstractions;

public interface ISchemaValidator
{
    ValidationResult Validate(SchemaDefinition schema);
}
