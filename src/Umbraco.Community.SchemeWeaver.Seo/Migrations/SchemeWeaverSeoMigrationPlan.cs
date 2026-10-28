using Umbraco.Cms.Core.Packaging;

namespace Umbraco.Community.SchemeWeaver.Seo.Migrations;

/// <summary>Migration plan for the SEO satellite's own table. Discovered by type scan like the core plan.</summary>
public class SchemeWeaverSeoMigrationPlan : PackageMigrationPlan
{
    public SchemeWeaverSeoMigrationPlan() : base(SchemeWeaverSeoConstants.PackageName)
    {
    }

    protected override void DefinePlan()
    {
        To<CreatePageOverrideTableMigration>("schemeweaver-seo-page-override-v1");
    }
}
