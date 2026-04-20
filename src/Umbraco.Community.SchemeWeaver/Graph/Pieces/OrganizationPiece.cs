using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits the site-wide Organization (or any Organization subtype — LocalBusiness,
/// RealEstateAgent, ProfessionalService, …) from the settings content node's
/// SchemaMapping. The settings node is resolved by <see cref="ISiteSettingsResolver"/>;
/// its mapping is then run through <see cref="IJsonLdGenerator"/> exactly as
/// though it were the main entity of its own page.
///
/// @id convention: <c>{siteUrl}#organization</c>, overridable via the mapping's
/// <c>IdOverride</c> (Phase A) — in which case the override wins and the
/// declared @id matches whatever the Thing ends up with.
///
/// Skipped when no settings node exists, or when it has no active mapping, or
/// when the mapped type isn't an Organization subtype (to avoid surprising
/// consumers — if the user maps the settings node to, say, Thing, the piece
/// bows out and the graph degrades to per-page output).
/// </summary>
public sealed class OrganizationPiece : IGraphPiece
{
    private readonly IJsonLdGenerator _generator;
    private readonly ISchemaTypeRegistry _registry;
    private readonly ILogger<OrganizationPiece> _logger;

    private Thing? _cached;
    private bool _built;

    public OrganizationPiece(
        IJsonLdGenerator generator,
        ISchemaTypeRegistry registry,
        ILogger<OrganizationPiece> logger)
    {
        _generator = generator;
        _registry = registry;
        _logger = logger;
    }

    public string Key => "organization";
    public int Order => 100;

    public string? ResolveId(GraphPieceContext ctx)
    {
        var thing = GetOrBuild(ctx);
        if (thing is null || !IsOrganizationType(thing.GetType()))
            return null;

        // Thing.Id is populated by the generator. If for any reason it's absent,
        // fall back to the conventional {siteUrl}#organization so cross-refs work.
        if (thing.Id is not null)
            return thing.Id.ToString();

        return ctx.SiteUrl is null ? null : $"{ctx.SiteUrl}#organization";
    }

    public Thing? Build(GraphPieceContext ctx) => GetOrBuild(ctx);

    private Thing? GetOrBuild(GraphPieceContext ctx)
    {
        if (_built)
            return _cached;

        _built = true;

        if (ctx.SiteSettings is null)
            return null;

        try
        {
            var thing = _generator.GenerateJsonLd(ctx.SiteSettings, ctx.Culture, ctx);
            if (thing is null)
                return null;

            // Guard-rail: only surface as the Organization piece when the mapped
            // Schema.org type really is an Organization subtype. Prevents silent
            // misconfiguration where the settings node is mapped to a random type.
            if (!IsOrganizationType(thing.GetType()))
            {
                _logger.LogDebug(
                    "Site settings node is mapped to {Type}, not an Organization subtype — OrganizationPiece skipping",
                    thing.GetType().Name);
                return null;
            }

            // If the mapping didn't set @id (e.g. no URL context for the settings
            // node), apply the conventional one so it lines up with what
            // ResolveId returned.
            if (thing.Id is null && ctx.SiteUrl is not null)
                thing.Id = new Uri($"{ctx.SiteUrl}#organization");

            _cached = thing;
            return thing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OrganizationPiece failed to build for site settings node {SiteSettingsId}",
                ctx.SiteSettings.Id);
            return null;
        }
    }

    private static bool IsOrganizationType(Type t) =>
        // Schema.NET models Schema.org's multi-inheritance via interfaces:
        // RealEstateAgent extends LocalBusiness -> Place -> Thing (concrete),
        // implementing IOrganization by interface rather than inheriting the
        // concrete Organization class. Check via IOrganization so every
        // Organization subtype qualifies (LocalBusiness, RealEstateAgent,
        // Corporation, NGO, EducationalOrganization, …).
        typeof(IOrganization).IsAssignableFrom(t)
        || typeof(Organization).IsAssignableFrom(t);
}
