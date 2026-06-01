namespace Tsabo.Data.Schema.Abstractions;

public interface ISchemaComparer
{
    SchemaDiff Compare(SchemaDefinition current, SchemaDefinition target, SchemaOptions? options = null);
}
