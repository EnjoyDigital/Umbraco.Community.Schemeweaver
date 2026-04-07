using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Adds the DynamicRootConfig column to the PropertyMapping table.
/// This nullable TEXT column stores JSON configuration for Umbraco dynamic root
/// settings (origin, query steps) used by parent/ancestor/sibling source types.
/// </summary>
public class AddDynamicRootConfigMigration : AsyncMigrationBase
{
    public AddDynamicRootConfigMigration(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        Logger.LogInformation("Running SchemeWeaver AddDynamicRootConfigMigration...");

        if (TableExists(SchemeWeaverConstants.Tables.PropertyMapping)
            && !ColumnExists(SchemeWeaverConstants.Tables.PropertyMapping, "DynamicRootConfig"))
        {
            // Use raw SQL instead of fluent Alter.Table() because Umbraco's fluent migration
            // builder throws NotSupportedException on SQLite for ALTER TABLE operations.
            // Use TEXT rather than nvarchar(max) for cross-database compatibility.
            Execute.Sql($"ALTER TABLE {SchemeWeaverConstants.Tables.PropertyMapping} ADD COLUMN DynamicRootConfig TEXT NULL").Do();

            Logger.LogInformation("Added DynamicRootConfig column to {Table} table",
                SchemeWeaverConstants.Tables.PropertyMapping);
        }

        Logger.LogInformation("SchemeWeaver AddDynamicRootConfigMigration completed successfully");

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
