namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Response payload for <c>GET /umbraco/delivery/api/v2/schemeweaver/json-ld</c> and
/// <c>GET /umbraco/delivery/api/v2/schemeweaver/json-ld/by-route</c>. Each string is a
/// standalone JSON-LD block ready to be injected into a
/// <c>&lt;script type="application/ld+json"&gt;</c> tag. Order:
/// inherited ancestor schemas (root-first) → <c>BreadcrumbList</c> → main page schema →
/// block element schemas.
/// </summary>
public sealed record SchemeWeaverJsonLdResponse(string[] SchemaOrg);
