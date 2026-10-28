using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;

namespace Umbraco.Community.SchemeWeaver.Seo.Migrations;

/// <summary>Creates the per-page override table. Later columns must use raw ALTER TABLE … TEXT NULL (SQLite).</summary>
public class CreatePageOverrideTableMigration : AsyncMigrationBase
{
    public CreatePageOverrideTableMigration(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (!TableExists(SchemeWeaverSeoConstants.Tables.PageOverride))
        {
            Create.Table<SeoPageOverride>().Do();
            Logger.LogInformation("Created {Table} table", SchemeWeaverSeoConstants.Tables.PageOverride);
        }

        return Task.CompletedTask;
    }
}
