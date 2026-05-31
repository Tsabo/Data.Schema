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

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue;

            var table = new TableDefinition { Name = tableName };

            var storeObjectId = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var ordinal = 0;

            foreach (var item in entityType.GetProperties())
            {
                var column = new ColumnDefinition
                {
                    Name = item.GetColumnName(storeObjectId) ?? item.Name,
                    // GetRelationalTypeMapping().StoreType returns the actual provider-specific SQL type.
                    Type = item.GetRelationalTypeMapping().StoreType,
                    IsNullable = item.IsNullable,
                    IsPrimaryKey = item.IsPrimaryKey(),
                    IsAutoIncrement = item.ValueGenerated is not ValueGenerated.Never
                                      && item.IsPrimaryKey(),
                    DefaultValue = item.GetDefaultValueSql(),
                    OrdinalPosition = ordinal++,
                };

                table.Columns.Add(column);
            }

            foreach (var item in entityType.GetIndexes())
            {
                var storeIndex = item.GetDatabaseName();
                if (storeIndex is null)
                    continue;

                table.Indexes.Add(new IndexDefinition
                {
                    Name = storeIndex,
                    Columns = item.Properties.Select(p => p.GetColumnName(storeObjectId) ?? p.Name).ToList(),
                    IsUnique = item.IsUnique,
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
                    table.ForeignKeys.Add(new ForeignKeyDefinition
                    {
                        Name = fkConstraint,
                        Column = item.Properties[i].GetColumnName(storeObjectId) ?? item.Properties[i].Name,
                        ReferencedTable = principalTable,
                        ReferencedColumn = item.PrincipalKey.Properties[i].GetColumnName(principalStoreObject) ?? item.PrincipalKey.Properties[i].Name,
                    });
                }
            }

            schema.Tables.Add(table);
        }

        return Task.FromResult(schema);
    }
}
