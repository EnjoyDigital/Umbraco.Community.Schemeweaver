using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Adds the IdOverride column to the SchemaMapping table. This nullable TEXT
/// column stores a user-supplied @id template with tokens ({url}, {type},
/// {key}, {culture}, {siteUrl}) that override the default {url}#{type}
/// convention when emitting JSON-LD.
/// </summary>
public class AddIdOverrideMigration : AsyncMigrationBase
{
    public AddIdOverrideMigration(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        Logger.LogInformation("Running SchemeWeaver AddIdOverrideMigration...");

        if (TableExists(SchemeWeaverConstants.Tables.SchemaMapping)
            && !ColumnExists(SchemeWeaverConstants.Tables.SchemaMapping, "IdOverride"))
        {
            // Raw SQL: Umbraco's fluent Alter.Table() throws NotSupportedException
            // on SQLite. TEXT NULL is cross-database compatible.
            Execute.Sql($"ALTER TABLE {SchemeWeaverConstants.Tables.SchemaMapping} ADD COLUMN IdOverride TEXT NULL").Do();

            Logger.LogInformation("Added IdOverride column to {Table} table",
                SchemeWeaverConstants.Tables.SchemaMapping);
        }

        Logger.LogInformation("SchemeWeaver AddIdOverrideMigration completed successfully");

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
