using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Adds the TargetPieceKey column to the PropertyMapping table. Populated by
/// the v1.4 <c>reference</c> source type — holds the key of the graph piece
/// (e.g. <c>"organization"</c>) whose @id this property should resolve to, so
/// the generator emits <c>{"@id": "https://example.com/#organization"}</c>
/// instead of an inline object.
/// </summary>
public class AddTargetPieceKeyMigration : AsyncMigrationBase
{
    public AddTargetPieceKeyMigration(IMigrationContext context) : base(context)
    {
    }

    protected override async Task MigrateAsync()
    {
        Logger.LogInformation("Running SchemeWeaver AddTargetPieceKeyMigration...");

        if (TableExists(SchemeWeaverConstants.Tables.PropertyMapping)
            && !ColumnExists(SchemeWeaverConstants.Tables.PropertyMapping, "TargetPieceKey"))
        {
            // Raw SQL — fluent Alter.Table() trips NotSupportedException on SQLite.
            // TEXT NULL is cross-database compatible (NPoco maps string? to nvarchar on SQL Server).
            Execute.Sql($"ALTER TABLE {SchemeWeaverConstants.Tables.PropertyMapping} ADD COLUMN TargetPieceKey TEXT NULL").Do();

            Logger.LogInformation("Added TargetPieceKey column to {Table} table",
                SchemeWeaverConstants.Tables.PropertyMapping);
        }

        Logger.LogInformation("SchemeWeaver AddTargetPieceKeyMigration completed successfully");

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
