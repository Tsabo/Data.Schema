using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Tsabo.Data.Schema.AotSample;

// EF Core's DbContext base constructor is unconditionally annotated RequiresDynamicCode/RequiresUnreferencedCode
// because EF Core's own AOT/trim support is partial (see https://aka.ms/efcore-docs-trimming) — this is an EF Core
// limitation, not something Tsabo.Data.Schema.EntityFrameworkCore controls (it never constructs a DbContext itself).
// This sample uses a minimal model with no navigations/conventions-heavy features, which does work correctly
// under Native AOT despite the defensive warning — verified below by actually publishing and running natively.
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "EF Core's own AOT support is partial; this sample's minimal model works at runtime.")]
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EF Core's own trim support is partial; this sample's minimal model works at runtime.")]
internal sealed class SampleDbContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite("Data Source=:memory:");
}

internal sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
