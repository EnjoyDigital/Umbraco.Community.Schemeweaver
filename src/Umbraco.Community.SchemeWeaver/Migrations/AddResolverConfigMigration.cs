using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Adds the ResolverConfig column to the PropertyMapping table.
/// This nullable TEXT column stores JSON configuration for property value resolvers,
/// such as nested property mappings for block content.
/// </summary>
public class AddResolverConfigMigration : AsyncMigrationBase
{
    public AddResolverConfigMigration(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        Logger.LogInformation("Running SchemeWeaver AddResolverConfigMigration...");

        if (TableExists(SchemeWeaverConstants.Tables.PropertyMapping)
            && !ColumnExists(SchemeWeaverConstants.Tables.PropertyMapping, "ResolverConfig"))
        {
            // Use raw SQL instead of fluent Alter.Table() because Umbraco's fluent migration
            // builder throws NotSupportedException on SQLite for ALTER TABLE operations.
            // SQLite natively supports ALTER TABLE ... ADD COLUMN at the raw SQL level.
            // Use TEXT rather than nvarchar(max) for cross-database compatibility (SQLite uses TEXT for all strings).
            Execute.Sql($"ALTER TABLE {SchemeWeaverConstants.Tables.PropertyMapping} ADD COLUMN ResolverConfig TEXT NULL").Do();

            Logger.LogInformation("Added ResolverConfig column to {Table} table",
                SchemeWeaverConstants.Tables.PropertyMapping);
        }

        Logger.LogInformation("SchemeWeaver AddResolverConfigMigration completed successfully");

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
