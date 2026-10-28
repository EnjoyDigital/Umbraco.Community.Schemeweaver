using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Core.Security.Authorization;

namespace Umbraco.Community.SchemeWeaver.Seo.Authorization;

public static class SeoAuthorizationPolicies
{
    /// <summary>Requires the calling backoffice user (or API user) to have access to the SEO Schema section.</summary>
    public const string SectionAccess = "SchemeWeaverSeoSectionAccess";
}

/// <summary>
/// Umbraco's own allowed-application requirement is internal to the management API assembly,
/// so the package ships its own. The handler reads <c>IUser.AllowedSections</c> from the user
/// (database-backed) rather than a login claim, so a freshly granted section is visible on the
/// next request without re-authenticating.
/// </summary>
public sealed class SeoSectionRequirement : IAuthorizationRequirement
{
    public SeoSectionRequirement(string sectionAlias)
    {
        SectionAlias = sectionAlias;
    }

    public string SectionAlias { get; }
}

public sealed class SeoSectionHandler : AuthorizationHandler<SeoSectionRequirement>
{
    private readonly IAuthorizationHelper _authorizationHelper;

    public SeoSectionHandler(IAuthorizationHelper authorizationHelper)
    {
        _authorizationHelper = authorizationHelper;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SeoSectionRequirement requirement)
    {
        if (_authorizationHelper.TryGetUmbracoUser(context.User, out var user)
            && user.AllowedSections.Contains(requirement.SectionAlias, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
