namespace Tsabo.Data.Schema;

public record MigrationResult(
    bool Success,
    IReadOnlyList<MigrationOperation> Operations,
    IReadOnlyList<string> Warnings,
    Exception? Error = null);
