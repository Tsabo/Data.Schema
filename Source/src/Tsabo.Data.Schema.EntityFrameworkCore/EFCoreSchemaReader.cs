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

        // The relational model (as opposed to walking IModel.GetEntityTypes() directly) already
        // resolves each physical table exactly once, with its real columns. This is what makes it
        // correct for the cases that break a naive per-entity-type walk: owned types using table
        // splitting (e.g. no distinct ToTable) share their owner's table and would otherwise be read
        // twice; owned types mapped to a single JSON column (e.g. ASP.NET Core Identity's passkey
        // "Data" column under SchemaVersion 3) have properties with no column of their own at all; and
        // the ownership foreign key linking a split/JSON-mapped owned type back to its owner is an
        // internal modeling detail, not a real table-to-table relationship - the relational model
        // doesn't surface it as a ForeignKeyConstraint.
        foreach (var table in model.GetRelationalModel().Tables)
        {
            if (table.IsExcludedFromMigrations)
                continue;

            var tableDefinition = new TableDefinition { Name = table.Name };
            var primaryKeyColumns = table.PrimaryKey?.Columns;
            var ordinal = 0;

            foreach (var column in table.Columns)
            {
                var property = column.PropertyMappings.FirstOrDefault()?.Property;
                var isPrimaryKey = primaryKeyColumns?.Contains(column) ?? false;

                tableDefinition.Columns.Add(new ColumnDefinition
                {
                    Name = column.Name,
                    // StoreType returns the actual provider-specific SQL type.
                    Type = column.StoreType,
                    IsNullable = column.IsNullable,
                    IsPrimaryKey = isPrimaryKey,
                    IsAutoIncrement = isPrimaryKey
                                      && property is not null
                                      && property.ValueGenerated is not ValueGenerated.Never
                                      && IsIntegerType(property.ClrType),
                    DefaultValue = column.DefaultValueSql,
                    OrdinalPosition = ordinal++
                });
            }

            foreach (var index in table.Indexes)
            {
                tableDefinition.Indexes.Add(new IndexDefinition
                {
                    Name = index.Name,
                    Columns = index.Columns.Select(c => c.Name).ToList(),
                    IsUnique = index.IsUnique
                });
            }

            foreach (var fk in table.ForeignKeyConstraints)
            {
                for (var i = 0; i < fk.Columns.Count; i++)
                {
                    tableDefinition.ForeignKeys.Add(new ForeignKeyDefinition
                    {
                        Name = fk.Name,
                        Column = fk.Columns[i].Name,
                        ReferencedTable = fk.PrincipalTable.Name,
                        ReferencedColumn = fk.PrincipalColumns[i].Name
                    });
                }
            }

            schema.Tables.Add(tableDefinition);
        }

        return Task.FromResult(schema);
    }

    private static bool IsIntegerType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);
    }
}
