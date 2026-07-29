using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tsabo.Data.Schema.Abstractions;

namespace Tsabo.Data.Schema.EntityFrameworkCore;

/// <summary>
/// Reads a SchemaDefinition from an EF Core model using provider-agnostic relational metadata.
/// Use this to generate a target schema from your DbContext rather than handcrafting JSON.
/// </summary>
public sealed class EFCoreSchemaReader(DbContext context) : ISchemaReader
{
    public Task<SchemaDefinition> ReadAsync(CancellationToken cancellationToken = default)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var schema = new SchemaDefinition();

        // Owned types using table splitting (e.g. OwnsOne without a distinct ToTable, as used by
        // ASP.NET Core Identity's passkey "Data" owned type) produce a separate IEntityType that
        // maps to the *same* table name as their owner. Group by table so each table is emitted once.
        var tablesByName = model.GetEntityTypes()
            .Select(entityType => (EntityType: entityType, TableName: entityType.GetTableName()))
            .Where(p => p.TableName is not null)
            .GroupBy(p => p.TableName!, StringComparer.OrdinalIgnoreCase);

        foreach (var tableGroup in tablesByName)
        {
            var tableName = tableGroup.Key;
            var table = new TableDefinition { Name = tableName };
            var ordinal = 0;

            foreach (var entityType in tableGroup.Select(p => p.EntityType))
            {
                var storeObjectId = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

                foreach (var item in entityType.GetProperties())
                {
                    var columnName = item.GetColumnName(storeObjectId) ?? item.Name;

                    if (table.Columns.Any(p => p.Name == columnName))
                        continue;

                    var column = new ColumnDefinition
                    {
                        Name = columnName,
                        // GetRelationalTypeMapping().StoreType returns the actual provider-specific SQL type.
                        Type = item.GetRelationalTypeMapping().StoreType,
                        IsNullable = item.IsNullable,
                        IsPrimaryKey = item.IsPrimaryKey(),
                        IsAutoIncrement = item.ValueGenerated is not ValueGenerated.Never
                                          && item.IsPrimaryKey()
                                          && IsIntegerType(item.ClrType),
                        DefaultValue = item.GetDefaultValueSql(),
                        OrdinalPosition = ordinal++
                    };

                    table.Columns.Add(column);
                }

                foreach (var item in entityType.GetIndexes())
                {
                    var storeIndex = item.GetDatabaseName();

                    if (storeIndex is null || table.Indexes.Any(p => p.Name == storeIndex))
                        continue;

                    table.Indexes.Add(new IndexDefinition
                    {
                        Name = storeIndex,
                        Columns = item.Properties.Select(p => p.GetColumnName(storeObjectId) ?? p.Name).ToList(),
                        IsUnique = item.IsUnique
                    });
                }

                foreach (var item in entityType.GetForeignKeys())
                {
                    var fkConstraint = item.GetConstraintName();

                    if (fkConstraint is null)
                        continue;

                    var principalTable = item.PrincipalEntityType.GetTableName();

                    if (principalTable is null)
                        continue;

                    var principalStoreObject = StoreObjectIdentifier.Table(principalTable, item.PrincipalEntityType.GetSchema());

                    for (var i = 0; i < item.Properties.Count; i++)
                    {
                        var columnName = item.Properties[i].GetColumnName(storeObjectId) ?? item.Properties[i].Name;

                        if (table.ForeignKeys.Any(p => p.Name == fkConstraint && p.Column == columnName))
                            continue;

                        table.ForeignKeys.Add(new ForeignKeyDefinition
                        {
                            Name = fkConstraint,
                            Column = columnName,
                            ReferencedTable = principalTable,
                            ReferencedColumn = item.PrincipalKey.Properties[i].GetColumnName(principalStoreObject) ?? item.PrincipalKey.Properties[i].Name
                        });
                    }
                }
            }

            schema.Tables.Add(table);
        }

        return Task.FromResult(schema);
    }

    private static bool IsIntegerType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);
    }
}
