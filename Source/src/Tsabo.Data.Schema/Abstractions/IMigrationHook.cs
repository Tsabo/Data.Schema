namespace Tsabo.Data.Schema.Abstractions;

public interface IMigrationHook
{
    Task BeforeAsync(MigrationContext context, CancellationToken cancellationToken = default);
    Task AfterAsync(MigrationContext context, CancellationToken cancellationToken = default);
}
