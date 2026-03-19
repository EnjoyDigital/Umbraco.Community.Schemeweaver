using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Creates the SchemeWeaver database tables.
/// </summary>
public class CreateTablesMigration : MigrationBase
{
    public CreateTablesMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Logger.LogInformation("Running SchemeWeaver CreateTablesMigration...");

        if (!TableExists(SchemeWeaverConstants.Tables.SchemaMapping))
        {
            Create.Table<SchemaMapping>().Do();
            Logger.LogInformation("Created {Table} table", SchemeWeaverConstants.Tables.SchemaMapping);
        }

        if (!TableExists(SchemeWeaverConstants.Tables.PropertyMapping))
        {
            Create.Table<PropertyMapping>().Do();
            Logger.LogInformation("Created {Table} table", SchemeWeaverConstants.Tables.PropertyMapping);
        }

        Logger.LogInformation("SchemeWeaver CreateTablesMigration completed successfully");
    }
}
