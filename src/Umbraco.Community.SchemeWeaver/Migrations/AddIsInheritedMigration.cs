using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Adds the IsInherited column to the SchemaMapping table.
/// When enabled, the schema is also output on all descendant pages.
/// </summary>
public class AddIsInheritedMigration : AsyncMigrationBase
{
    public AddIsInheritedMigration(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        Logger.LogInformation("Running SchemeWeaver AddIsInheritedMigration...");

        if (TableExists(SchemeWeaverConstants.Tables.SchemaMapping)
            && !ColumnExists(SchemeWeaverConstants.Tables.SchemaMapping, "IsInherited"))
        {
            // Use raw SQL for SQLite compatibility (Umbraco's fluent builder throws
            // NotSupportedException on SQLite for ALTER TABLE operations).
            // Use INTEGER NOT NULL DEFAULT 0 for cross-database compatibility (SQLite maps bool to INTEGER).
            Execute.Sql($"ALTER TABLE {SchemeWeaverConstants.Tables.SchemaMapping} ADD COLUMN IsInherited INTEGER NOT NULL DEFAULT 0").Do();

            Logger.LogInformation("Added IsInherited column to {Table} table",
                SchemeWeaverConstants.Tables.SchemaMapping);
        }

        Logger.LogInformation("SchemeWeaver AddIsInheritedMigration completed successfully");

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
