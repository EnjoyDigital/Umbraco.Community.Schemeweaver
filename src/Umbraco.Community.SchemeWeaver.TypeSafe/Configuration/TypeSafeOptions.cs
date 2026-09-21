namespace Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

/// <summary>
/// Configuration for the TypeSafe satellite, bound to the <c>SchemeWeaver:TypeSafe</c> section.
/// </summary>
/// <remarks>
/// The satellite is inert without an <see cref="ApiKey"/>: every judgment falls through to
/// whatever <c>ISchemaAutoMapper</c> was registered before it (the heuristic, or the AI
/// satellite when both are installed) and one Information log line says so at startup.
/// Supply the key through user-secrets or an environment variable, never appsettings.
/// </remarks>
public class TypeSafeOptions
{
    /// <summary>Configuration section this class binds to.</summary>
    public const string SectionName = "SchemeWeaver:TypeSafe";

    /// <summary>
    /// Master switch. <c>false</c> leaves the satellite installed but inert, which is how an
    /// integration test host — or a site that also runs the AI satellite — chooses which
    /// mapper answers auto-map. Default <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// TypeSafe API key (Bearer). No key means <see cref="Enabled"/> is effectively false.
    /// Never serialised with the rest of the options and left out of <see cref="ToString"/>,
    /// so a diagnostics dump of the bound configuration cannot leak it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>
    /// System One endpoint. Only change this for a proxy or a test double. Must be https, or
    /// plain http on the loopback interface (a WireMock or a local dev proxy).
    /// </summary>
    public string Endpoint { get; set; } = "https://api.typesafe.ai/v1/systemone";

    /// <summary>
    /// Model alias sent on every request. <c>jev-latest</c> tracks TypeSafe's stable release;
    /// pin an exact id (e.g. <c>jev-1.13.0</c>) for reproducible behaviour.
    /// </summary>
    public string Model { get; set; } = "jev-latest";

    /// <summary>Per-request HTTP timeout. Jev answers in ~100 ms; this only guards a hung socket.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retries for HTTP 429 (rate limit) and 529 (overloaded), with exponential backoff. Any
    /// other failure is not retried: a 422 means the request shape is wrong, and retrying it
    /// only burns time. Default <c>4</c>.
    /// </summary>
    public int MaxRetries { get; set; } = 4;

    /// <summary>
    /// Questions per request. Questions in one request share the state and evaluate in
    /// parallel, but a request is capped at 64k tokens (32k for state plus the longest
    /// question), so a wide content type against a 130-property schema type is chunked.
    /// Default <c>12</c>, the value the eval harness ran at.
    /// </summary>
    public int MaxQuestionsPerRequest { get; set; } = 12;

    /// <summary>
    /// Beam width for the nested-type descent down the Schema.org tree. Greedy (1) commits to
    /// the best child at every level and cannot recover from an early wrong turn; 3 recovered
    /// both regressions observed in the eval. Default <c>3</c>.
    /// </summary>
    public int BeamWidth { get; set; } = 3;

    /// <summary>Maximum levels the nested-type descent walks below a property's declared range. Default <c>5</c>.</summary>
    public int MaxDescentDepth { get; set; } = 5;

    /// <summary>
    /// Options offered per Choice question. The API allows 255; one slot is reserved for the
    /// "none of these" option every binding question carries. Default <c>254</c>.
    /// </summary>
    public int MaxOptionsPerChoice { get; set; } = 254;

    /// <summary>
    /// Minimum calibrated confidence (0–100) for a property binding to be kept at all. Rows
    /// below it are dropped before the core auto-mapper thresholds
    /// (<c>SchemeWeaver:AutoMapper</c>) decide what is pre-ticked and what is shown. Default
    /// <c>0</c>: keep everything and let the core thresholds gate, as the eval harness did.
    /// </summary>
    public int MinBindingConfidence { get; set; }

    /// <summary>
    /// What happens to the heuristic auto-mapper's own suggestions (the "priors") once TypeSafe
    /// has answered. Default <see cref="TypeSafePriorsMode.None"/>: TypeSafe's rows alone, gated
    /// by the core thresholds, which measured best on the eval harness (strict F1 0.714 against
    /// 0.676 for the override rule this package first shipped with, and rich coverage 8 of 12
    /// against 4 of 12). <see cref="TypeSafePriorsMode.GapFill"/> adds the heuristic's
    /// rule-driven rows (its popular-defaults shapes such as <c>FAQPage.mainEntity</c>, its
    /// cross-piece references, its exact-alias matches) only for schema properties TypeSafe left
    /// unmapped; it recovered one more rich row on the harness at the cost of precision
    /// (strict F1 0.569), so it is opt-in for sites that rely on those shapes.
    /// </summary>
    public TypeSafePriorsMode PriorsMode { get; set; } = TypeSafePriorsMode.None;

    // ---- v2: multiple targets per content property ----

    /// <summary>
    /// A content property's bind answer carries a probability per schema property. A runner-up
    /// at or above this probability whose schema property nobody else claimed becomes a second
    /// row (the way a hand-written mapping sends <c>title</c> to both <c>headline</c> and
    /// <c>name</c>). Such a row is shown at or above this probability regardless of the core's
    /// show threshold (a runner-up rarely holds more than half of the distribution), is never
    /// pre-ticked, and is never created for a Block List property. Costs no extra questions.
    /// Default <c>0.30</c>.
    /// </summary>
    public double SecondaryBindingMinProbability { get; set; } = 0.30;

    // ---- v2: what the model sees ----

    /// <summary>
    /// Opt-in: describe each scalar property with a short, HTML-stripped sample value taken
    /// from one published node of the type, so the model can tell a "code" textbox holding
    /// an ISBN from one holding a coupon. Default <c>false</c>: sample values are customer
    /// content and leave the site in the request. The value schema (the shape of the stored
    /// value) is always included and carries no content.
    /// </summary>
    public bool IncludeSampleValues { get; set; }

    /// <summary>
    /// Budget, in characters of JSON, for the shared request state: the content type's
    /// properties with their value schemas, sample values and nested block structure (the
    /// neighbourhood attached to the cross-node round is not counted). A request is capped at
    /// 64k tokens, 32k of them for the state, and one over the cap is refused with a 4xx that
    /// is never retried, after which the whole mapping falls back to the prior mapper. So while
    /// the state is over budget detail is dropped in a fixed order until it fits: block-field
    /// value schemas, then nested block levels beyond the first, then the properties' own value
    /// schemas, then sample values, each step logged at Debug with sizes only. Default
    /// <c>100000</c>, roughly 25k tokens at four characters a token, which leaves the questions
    /// their share of the request.
    /// </summary>
    public int MaxStateCharacters { get; set; } = 100_000;

    // ---- v2: cross-node sources (parent / ancestor / sibling) ----

    /// <summary>Master switch for the cross-node round. Default <c>true</c>.</summary>
    public bool EnableCrossNodeSources { get; set; } = true;

    /// <summary>
    /// How the content type's neighbourhood (which types sit above and beside it) is found.
    /// <see cref="TypeSafeNeighbourhoodDiscovery.Structure"/> reads the document types' allowed
    /// children; <see cref="TypeSafeNeighbourhoodDiscovery.Observed"/> samples real nodes of the
    /// type and reads their actual parents, ancestors and siblings, which is what works on
    /// sites whose structures are loose (most of them). Default <see cref="TypeSafeNeighbourhoodDiscovery.Both"/>.
    /// </summary>
    public TypeSafeNeighbourhoodDiscovery NeighbourhoodDiscovery { get; set; } = TypeSafeNeighbourhoodDiscovery.Both;

    /// <summary>How far above the type ancestors are collected (1 = parents only). Default <c>3</c>.</summary>
    public int MaxAncestorDepth { get; set; } = 3;

    /// <summary>Neighbour types offered, parents first, then ancestors nearest-first, then siblings. Default <c>12</c>.</summary>
    public int MaxNeighbourTypes { get; set; } = 12;

    /// <summary>Properties listed per neighbour type. Default <c>30</c>.</summary>
    public int MaxPropertiesPerNeighbour { get; set; } = 30;

    /// <summary>Neighbour properties offered in one cross-node Choice in total (the API allows 255 options). Default <c>120</c>.</summary>
    public int MaxNeighbourProperties { get; set; } = 120;

    /// <summary>Nodes of the type sampled for observed-tree discovery. Default <c>25</c>.</summary>
    public int MaxSampledNodes { get; set; } = 25;

    /// <summary>Share of sampled nodes a parent, ancestor or sibling type must appear in to count; an observed sibling type is also dropped when any sampled parent holds more than one page of it. Default <c>0.5</c>.</summary>
    public double MinObservedShare { get; set; } = 0.5;

    /// <summary>Unbound schema properties asked about in the cross-node round, most cross-node-prone first. Default <c>12</c>.</summary>
    public int MaxCrossNodeQuestions { get; set; } = 12;

    /// <summary>
    /// Minimum calibrated confidence (0 to 100) for a cross-node row. The default is the core's
    /// show bar, so a cross-node row is offered exactly like any other suggestion and auto-applied
    /// only at the core's auto-apply bar. The first v2 cut used 80 here, and the live measurement
    /// showed the rows an editor would want to see (a department's location from a sibling contact
    /// page, a shop's parent organisation) sitting at 60 to 79 and never reaching the screen.
    /// Default <c>60</c>.
    /// </summary>
    public int MinCrossNodeConfidence { get; set; } = 60;

    // ---- v2: nested-block routes ----

    /// <summary>
    /// When a Block List row is emitted as per-block-type <c>routes</c> rather than one nested
    /// type for the whole list. <see cref="TypeSafeRoutesMode.Auto"/> (default) uses routes only
    /// when the block types land on different Schema.org types, when a block contains a nested
    /// Block List, or when one block type was judged not to belong; otherwise the v1 shapes are
    /// emitted unchanged.
    /// </summary>
    public TypeSafeRoutesMode RoutesMode { get; set; } = TypeSafeRoutesMode.Auto;

    /// <summary>How many levels of blocks-inside-blocks are planned (the core discovers three). Default <c>3</c>.</summary>
    public int MaxBlockRouteDepth { get; set; } = 3;

    /// <summary>Every option except the key, which is reported only as set or unset.</summary>
    public override string ToString()
        => $"TypeSafeOptions(Enabled={Enabled}, ApiKey={(string.IsNullOrWhiteSpace(ApiKey) ? "unset" : "set")}, "
           + $"Endpoint={Endpoint}, Model={Model}, Timeout={Timeout}, MaxRetries={MaxRetries}, "
           + $"MaxQuestionsPerRequest={MaxQuestionsPerRequest}, BeamWidth={BeamWidth}, MaxDescentDepth={MaxDescentDepth}, "
           + $"MaxOptionsPerChoice={MaxOptionsPerChoice}, MinBindingConfidence={MinBindingConfidence}, PriorsMode={PriorsMode}, "
           + $"SecondaryBindingMinProbability={SecondaryBindingMinProbability}, IncludeSampleValues={IncludeSampleValues}, MaxStateCharacters={MaxStateCharacters}, "
           + $"EnableCrossNodeSources={EnableCrossNodeSources}, NeighbourhoodDiscovery={NeighbourhoodDiscovery}, "
           + $"MaxAncestorDepth={MaxAncestorDepth}, MaxNeighbourTypes={MaxNeighbourTypes}, MaxPropertiesPerNeighbour={MaxPropertiesPerNeighbour}, "
           + $"MaxNeighbourProperties={MaxNeighbourProperties}, MaxSampledNodes={MaxSampledNodes}, MinObservedShare={MinObservedShare}, "
           + $"MaxCrossNodeQuestions={MaxCrossNodeQuestions}, MinCrossNodeConfidence={MinCrossNodeConfidence}, "
           + $"RoutesMode={RoutesMode}, MaxBlockRouteDepth={MaxBlockRouteDepth})";
}

/// <summary>How a content type's neighbourhood is discovered for cross-node sources. See <see cref="TypeSafeOptions.NeighbourhoodDiscovery"/>.</summary>
public enum TypeSafeNeighbourhoodDiscovery
{
    /// <summary>From the document types' allowed-children declarations only.</summary>
    Structure,

    /// <summary>From the actual parents, ancestors and siblings of sampled nodes only.</summary>
    Observed,

    /// <summary>Both, merged (structure first). The default.</summary>
    Both,
}

/// <summary>When Block List rows use per-block-type routes. See <see cref="TypeSafeOptions.RoutesMode"/>.</summary>
public enum TypeSafeRoutesMode
{
    /// <summary>Never: the v1 shapes (a string list, or one nested type for the whole list) only.</summary>
    Off,

    /// <summary>Only when the list needs them (different types per block, nested blocks, a skipped block type). The default.</summary>
    Auto,

    /// <summary>Whenever a list is emitted as nested objects.</summary>
    Always,
}

/// <summary>How the heuristic auto-mapper's suggestions are combined with TypeSafe's. See <see cref="TypeSafeOptions.PriorsMode"/>.</summary>
public enum TypeSafePriorsMode
{
    /// <summary>TypeSafe's rows only (the default, and the best-measured). The heuristic is still the fallback when TypeSafe is unconfigured or fails.</summary>
    None,

    /// <summary>TypeSafe's rows, plus the heuristic's rule-driven rows for schema properties TypeSafe left unmapped. Never overrides a TypeSafe row.</summary>
    GapFill,
}
