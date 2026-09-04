# Tsabo.Data.Schema.AotSample

Proves that consumers of the Tsabo.Data.Schema packages can `dotnet publish` with Native AOT
without trim/AOT warnings or runtime reflection failures. `PublishAot` is already set in the
project file, so publishing just needs a target runtime:

```
dotnet publish Source/samples/Tsabo.Data.Schema.AotSample -c Release -r linux-x64
```

CI publishes and runs this natively on every push/PR (`.github/workflows/ci.yml`, `linux-x64`,
which needs no extra setup on `ubuntu-latest`). Publishing natively on Windows additionally
requires the "Desktop development with C++" workload (Native AOT needs a platform linker) — see
https://aka.ms/nativeaot-prerequisites; without it, `dotnet publish` fails at the final link step
even though the AOT/IL compilation itself succeeds.

What it exercises:
- JSON round-trip through `SchemaSerializer`, backed by the source-generated `SchemaJsonContext`.
- A full read -> compare -> migrate -> re-read cycle against a real temp-file SQLite database
  (the one provider that needs no external server, so it's fully runnable here).
- Constructing the Postgres and SQL Server readers and calling `ReadAsync` against them. No live
  Postgres/SQL Server instance is available in CI or in local dev here, so these are expected to
  fail with an ordinary connection exception (e.g. a socket/timeout exception) — the point is only
  to confirm the failure happens there, and not earlier with a reflection/trim-related exception
  (`MissingMethodException`, `TypeLoadException`, etc.), which would indicate the trimmer removed
  something those providers need. If you have a real Postgres/SQL Server instance handy, update the
  connection strings in `Program.cs` to point at it for a full end-to-end check of those providers too.

**EF Core is skipped under Native AOT, by design.** EF Core cannot build a model at runtime once
`RuntimeFeature.IsDynamicCodeSupported` is `false` (which `PublishAot=true` forces even for a plain
`dotnet run`, not only an actual native publish) — it throws `InvalidOperationException: Model
building is not supported when publishing with NativeAOT. Use a compiled model.` This is an EF Core
limitation, not something `Tsabo.Data.Schema.EntityFrameworkCore` can fix: that library never
constructs a `DbContext` itself (`EFCoreSchemaReader` only reads the model of a context the caller
already built), so it carries no AOT/trim warnings of its own — but a consumer's own AOT app will
hit this EF Core requirement as soon as it constructs a `DbContext`. To use `EFCoreSchemaReader`
under Native AOT, the consumer must supply a design-time compiled model (`dotnet ef dbcontext
optimize`) and register it via `DbContextOptionsBuilder.UseModel(...)` — see
https://aka.ms/efcore-docs-compiled-models. Wiring that up is out of scope for this sample since
it's specific to each consumer's own DbContext and entity model, not to Tsabo.Data.Schema.
