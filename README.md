# Tsabo.Data.Schema

Schema-first database management for .NET — define what your schema should look like, apply the diff.

## The Problem

While building [ClipMate](https://github.com/Tsabo/ClipMate), a SQLite-backed desktop application, I kept running into the same friction: Entity Framework Migrations are a great fit for server-side apps with a DBA and a deployment pipeline, but they're the wrong tool when your end user *is* the database.

EF migrations require you to track every incremental change in version-stamped files, ship those files with your app, and trust that the migration history table on the user's machine lines up with yours. When it doesn't, you're debugging `table "X" already exists` in a stranger's `AppData` folder.

What I actually wanted was simpler: **describe the schema you need, compare it to what's there, apply only the difference**. No migration history. No version stamps. Just a diff and a set of SQL statements.

## Why Not EF Migrations?

Traditional migration systems are stateful.

They require:

- Tracking migration history
- Shipping migration files
- Preserving migration ordering
- Ensuring migration state matches deployment state

Tsabo.Data.Schema is schema-first instead.

You define the desired schema, compare it against the database, and apply the difference.

No migration history table.
No version numbers.
No dependency on prior migration scripts.

## How It Works

1. **Define** a `SchemaDefinition` — either by hand, from JSON, or by reading it from an EF Core `DbContext` model.
2. **Read** the live schema from the database.
3. **Compare** to produce a `SchemaDiff` (a list of `MigrationOperation`s).
4. **Migrate** — execute the diff inside a transaction, with optional dry-run and before/after hooks.

```csharp
// 1. Define your target schema
var target = new SchemaDefinition
{
    Tables =
    [
        new TableDefinition
        {
            Name = "Notes",
            Columns =
            [
                new ColumnDefinition { Name = "Id",      Type = "INTEGER", IsPrimaryKey = true, IsAutoIncrement = true },
                new ColumnDefinition { Name = "Content",  Type = "TEXT",    IsNullable = false },
                new ColumnDefinition { Name = "CreatedAt", Type = "DATETIME", IsNullable = false },
            ],
        },
    ],
};

var connectionString = "Data Source=myapp.db";

// 2. Read the live schema
var reader   = new SqliteSchemaReader(connectionString);
var current  = await reader.ReadAsync();

// 3. Diff
var comparer = new SqliteSchemaComparer();
var diff     = comparer.Compare(current, target);

// 4. Apply
if (diff.HasChanges)
{
    var migrator = new SqliteSchemaMigrator(connectionString);
    var result   = await migrator.MigrateAsync(diff);

    if (!result.Success)
        throw result.Error!;

    foreach (var warning in result.Warnings)
        Console.WriteLine($"[warn] {warning}");
}
```

## Packages

| Package | Description |
|---|---|
| `Tsabo.Data.Schema` | Core abstractions and models — no external dependencies |
| `Tsabo.Data.Schema.Sqlite` | SQLite provider (reader, comparer, migrator, validator) |
| `Tsabo.Data.Schema.SqlServer` | SQL Server provider |
| `Tsabo.Data.Schema.Postgres` | PostgreSQL provider |
| `Tsabo.Data.Schema.EntityFrameworkCore` | Read a `SchemaDefinition` from any EF Core `DbContext` |

Install only what you need — each provider package pulls in only its own driver.

## Options

```csharp
var options = new SchemaOptions
{
    DryRun         = true,               // preview SQL without executing
    IgnoredTables  = ["__EFMigrationsHistory"],
    IgnoredColumns = ["RowVersion"],
    CacheSchema    = true,               // cache the live read (default: true)
};

var result = await migrator.MigrateAsync(diff, options);
foreach (var op in result.Operations)
    Console.WriteLine(op.Sql);
```

## Hooks

```csharp
public class BackupHook : IMigrationHook
{
    public async Task BeforeAsync(MigrationContext ctx, CancellationToken ct)
        => await CreateBackupAsync(ct);

    public Task AfterAsync(MigrationContext ctx, CancellationToken ct)
        => Task.CompletedTask;
}

var migrator = new SqliteSchemaMigrator(connectionString, [new BackupHook()]);
```

## Validation

```csharp
var validator = new SqliteSchemaValidator();
var result    = validator.Validate(target);

if (!result.IsValid)
    foreach (var error in result.Errors)
        Console.WriteLine(error);
```

## Reading from EF Core

If you already have a `DbContext`, you can derive the target schema from it instead of defining it by hand:

```csharp
var reader = new EFCoreSchemaReader(dbContext);
var target = await reader.ReadAsync();
```

This uses `GetRelationalTypeMapping().StoreType` under the hood, so the column types it returns are whatever the configured provider actually uses — no manual type mapping required.

## Limitations

- **SQLite does not support `ALTER COLUMN`** — type changes are flagged as warnings and skipped. A table rebuild is required; this library does not perform it automatically.
- Schema reading is scoped to the `dbo` schema (SQL Server) and `public` schema (PostgreSQL). Multi-schema support is not currently planned.
- Destructive operations (`DROP TABLE`, `DROP COLUMN`) are generated when the target schema omits a table or column. Review the diff before applying in production.

## License

MIT — see [LICENSE](LICENSE).
