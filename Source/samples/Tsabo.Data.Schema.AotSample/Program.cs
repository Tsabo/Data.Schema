using Microsoft.Data.Sqlite;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.AotSample;
using Tsabo.Data.Schema.EntityFrameworkCore;
using Tsabo.Data.Schema.Postgres;
using Tsabo.Data.Schema.Serialization;
using Tsabo.Data.Schema.Sqlite;
using Tsabo.Data.Schema.SqlServer;

Console.WriteLine("Tsabo.Data.Schema AOT sample");
Console.WriteLine();

var target = new SchemaDefinition
{
    Tables =
    [
        new TableDefinition
        {
            Name = "Widgets",
            Columns =
            [
                new ColumnDefinition { Name = "Id", Type = "INTEGER", IsPrimaryKey = true, IsAutoIncrement = true },
                new ColumnDefinition { Name = "Name", Type = "TEXT", IsNullable = false },
            ],
        },
    ],
};

var json = SchemaSerializer.ToJson(target);
var roundTripped = SchemaSerializer.FromJson(json) ?? throw new InvalidOperationException("Deserialization returned null.");
Console.WriteLine($"[JSON]      round-trip OK: {roundTripped.Tables.Count} table(s), {roundTripped.Tables[0].Columns.Count} column(s).");

var dbPath = Path.Combine(Path.GetTempPath(), $"tsabo-aot-sample-{Guid.NewGuid():N}.db");
var sqliteConnectionString = $"Data Source={dbPath}";
try
{
    var current = await new SqliteSchemaReader(sqliteConnectionString).ReadAsync();
    var diff = new SqliteSchemaComparer().Compare(current, roundTripped);
    Console.WriteLine($"[SQLite]    diff computed: {diff.Operations.Count} operation(s).");

    var result = await new SqliteSchemaMigrator(sqliteConnectionString).MigrateAsync(diff);
    Console.WriteLine($"[SQLite]    migration succeeded: {result.Success}");

    var verified = await new SqliteSchemaReader(sqliteConnectionString, new SchemaOptions { CacheSchema = false }).ReadAsync();
    var widgets = verified.Tables.SingleOrDefault(t => string.Equals(t.Name, "Widgets", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine(widgets is not null
        ? $"[SQLite]    verified 'Widgets' table exists with {widgets.Columns.Count} column(s)."
        : "[SQLite]    FAILED: 'Widgets' table not found after migration.");
}
finally
{
    SqliteConnection.ClearAllPools();
    File.Delete(dbPath);
}

if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    await using var context = new SampleDbContext();
    var efSchema = await new EFCoreSchemaReader(context).ReadAsync();
    Console.WriteLine($"[EF Core]   model read OK: {efSchema.Tables.Count} table(s).");
}
else
{
    // EF Core cannot build a model at runtime under Native AOT ("Model building is not supported when
    // publishing with NativeAOT. Use a compiled model.") -- this is an EF Core limitation, not something
    // Tsabo.Data.Schema.EntityFrameworkCore controls. A consumer who wants EFCoreSchemaReader to work under
    // AOT must supply a design-time compiled model (dotnet ef dbcontext optimize) and register it via
    // DbContextOptionsBuilder.UseModel(...). See https://aka.ms/efcore-docs-compiled-models.
    Console.WriteLine("[EF Core]   skipped under Native AOT: EF Core requires a precompiled model (see https://aka.ms/efcore-docs-compiled-models).");
}

await TryReadAsync("Postgres", () => new PostgresSchemaReader("Host=localhost;Database=doesnotexist;Username=none;Password=none;Timeout=1").ReadAsync());
await TryReadAsync("SqlServer", () => new SqlServerSchemaReader("Server=localhost;Database=doesnotexist;Connect Timeout=1;TrustServerCertificate=true").ReadAsync());

Console.WriteLine();
Console.WriteLine("Done.");

static async Task TryReadAsync(string label, Func<Task<SchemaDefinition>> read)
{
    try
    {
        await read();
        Console.WriteLine($"[{label}] connected and read schema (unexpected without a live server).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{label}] {ex.GetType().Name}: {ex.Message}");
    }
}
