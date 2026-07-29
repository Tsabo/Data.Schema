using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsabo.Data.Schema;
using Tsabo.Data.Schema.EntityFrameworkCore;
using Tsabo.Data.Schema.SqlServer;

namespace Tsabo.Tests.Unit.Schema.EntityFrameworkCore;

public class ApplicationUser : IdentityUser;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options);

public class IdentityDbContextSchemaReaderTests
{
    // Passkey support (AspNetUserPasskeys) is opt-in via IdentityOptions.Stores.SchemaVersion, which
    // AddIdentityCore(...).AddEntityFrameworkStores<TContext>() feeds into the DbContext's model
    // through the application service provider. Constructing the DbContext directly (`new
    // ApplicationDbContext(options)`) skips that wiring and silently produces a model *without*
    // AspNetUserPasskeys, so this must go through DI exactly as a real host does.
    private static ApplicationDbContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(p => p.UseSqlServer(
            "Server=.;Database=SchemaReaderTests;Trusted_Connection=True;TrustServerCertificate=True;"));

        services.AddIdentityCore<ApplicationUser>(p => p.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();

        return provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    // Under SchemaVersion 3, ASP.NET Core Identity maps the passkey owner (IdentityUserPasskey) and its
    // owned "Data" type (attestation object, public key, etc.) into a single physical table via table
    // splitting / a JSON container column. EFCoreSchemaReader used to walk IModel.GetEntityTypes()
    // directly, which produced two separate TableDefinitions named "AspNetUserPasskeys" - the second
    // CreateTable then failed with "There is already an object named 'AspNetUserPasskeys' in the
    // database." even though the table didn't previously exist.
    [Test]
    public async Task ReadAsync_IdentityDbContext_EmitsPasskeysTableExactlyOnce()
    {
        await using var context = CreateContext();
        var reader = new EFCoreSchemaReader(context);

        var schema = await reader.ReadAsync();

        var passkeyTables = schema.Tables.Where(p => p.Name == "AspNetUserPasskeys").ToList();
        await Assert.That(passkeyTables).Count().IsEqualTo(1);
    }

    // The owned "Data" type isn't split into individual columns - it's stored as a single JSON column
    // named "Data". The real physical table is CredentialId (PK) / Data (json) / UserId (FK).
    [Test]
    public async Task ReadAsync_IdentityDbContext_PasskeysTableHasExpectedColumns()
    {
        await using var context = CreateContext();
        var reader = new EFCoreSchemaReader(context);

        var schema = await reader.ReadAsync();

        var table = schema.Tables.Single(p => p.Name == "AspNetUserPasskeys");
        var columnNames = table.Columns.Select(p => p.Name).OrderBy(p => p).ToList();

        await Assert.That(columnNames).IsEquivalentTo(["CredentialId", "Data", "UserId"]);

        var primaryKeyColumn = table.Columns.Single(p => p.Name == "CredentialId");
        await Assert.That(primaryKeyColumn.IsPrimaryKey).IsTrue();
    }

    // The owned type's link back to its owner (table splitting / JSON container) is an internal
    // modeling detail, not a real relationship between two tables, and must not be emitted as a SQL
    // constraint - only the genuine FK to AspNetUsers should survive.
    [Test]
    public async Task ReadAsync_IdentityDbContext_PasskeysTableHasOnlyTheRealForeignKeyToUsers()
    {
        await using var context = CreateContext();
        var reader = new EFCoreSchemaReader(context);

        var schema = await reader.ReadAsync();

        var table = schema.Tables.Single(p => p.Name == "AspNetUserPasskeys");

        await Assert.That(table.ForeignKeys).Count().IsEqualTo(1);
        await Assert.That(table.ForeignKeys[0].ReferencedTable).IsEqualTo("AspNetUsers");
        await Assert.That(table.ForeignKeys[0].Column).IsEqualTo("UserId");
    }

    // End-to-end regression for the reported bug: migrating a blank database against an
    // IdentityDbContext model must produce exactly one CreateTable for AspNetUserPasskeys, and no
    // invalid self-referencing AddForeignKey operation for it.
    [Test]
    public async Task Compare_EmptyDatabaseAgainstIdentityDbContext_ProducesExactlyOneCreateTableForPasskeys()
    {
        await using var context = CreateContext();
        var reader = new EFCoreSchemaReader(context);
        var target = await reader.ReadAsync();

        var comparer = new SqlServerSchemaComparer();
        var diff = comparer.Compare(new SchemaDefinition(), target);

        var createTableOps = diff.Operations.Where(p => p is { Type: MigrationOperationType.CreateTable, TableName: "AspNetUserPasskeys" }).ToList();
        await Assert.That(createTableOps).Count().IsEqualTo(1);

        var selfReferencingForeignKeyOps = diff.Operations.Where(p =>
                p is { Type: MigrationOperationType.AddForeignKey, TableName: "AspNetUserPasskeys" }
                && p.Sql.Contains("REFERENCES [AspNetUserPasskeys]"))
            .ToList();

        await Assert.That(selfReferencingForeignKeyOps).IsEmpty();
    }
}
