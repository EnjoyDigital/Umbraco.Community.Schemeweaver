using Umbraco.Cms.Core.Packaging;

namespace Umbraco.Community.SchemeWeaver.Migrations;

/// <summary>
/// Migration plan for SchemeWeaver database tables.
/// </summary>
public class SchemeWeaverMigrationPlan : PackageMigrationPlan
{
    public SchemeWeaverMigrationPlan() : base(SchemeWeaverConstants.PackageName)
    {
    }

    protected override void DefinePlan()
    {
        To<CreateTablesMigration>("schemeweaver-tables-v1");
        To<AddResolverConfigMigration>("schemeweaver-add-resolver-config-v2");
        To<AddIsInheritedMigration>("schemeweaver-add-is-inherited-v3");
    }
}
