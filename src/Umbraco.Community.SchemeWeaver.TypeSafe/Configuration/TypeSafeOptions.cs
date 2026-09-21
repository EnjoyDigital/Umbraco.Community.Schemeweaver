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

    /// <summary>Every option except the key, which is reported only as set or unset.</summary>
    public override string ToString()
        => $"TypeSafeOptions(Enabled={Enabled}, ApiKey={(string.IsNullOrWhiteSpace(ApiKey) ? "unset" : "set")}, "
           + $"Endpoint={Endpoint}, Model={Model}, Timeout={Timeout}, MaxRetries={MaxRetries}, "
           + $"MaxQuestionsPerRequest={MaxQuestionsPerRequest}, BeamWidth={BeamWidth}, MaxDescentDepth={MaxDescentDepth}, "
           + $"MaxOptionsPerChoice={MaxOptionsPerChoice}, MinBindingConfidence={MinBindingConfidence}, PriorsMode={PriorsMode})";
}

/// <summary>How the heuristic auto-mapper's suggestions are combined with TypeSafe's. See <see cref="TypeSafeOptions.PriorsMode"/>.</summary>
public enum TypeSafePriorsMode
{
    /// <summary>TypeSafe's rows only (the default, and the best-measured). The heuristic is still the fallback when TypeSafe is unconfigured or fails.</summary>
    None,

    /// <summary>TypeSafe's rows, plus the heuristic's rule-driven rows for schema properties TypeSafe left unmapped. Never overrides a TypeSafe row.</summary>
    GapFill,
}
