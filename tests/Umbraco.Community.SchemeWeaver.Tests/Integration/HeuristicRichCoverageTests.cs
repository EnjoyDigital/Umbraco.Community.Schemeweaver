using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration;

/// <summary>
/// The in-repo eval gate for Stream A. Boots the TestHost with uSync-seeded content types, runs the
/// REAL heuristic auto-mapper for the eight rich content types, and scores its suggestions against
/// the curated gold mapping <c>.config</c> files — mirroring the STRICT scoring in
/// <c>eval/score.mjs</c> (complexType: nestedType + every gold <c>complexTypeMappings</c> binding;
/// blockContent nested: every gold <c>nestedMappings</c> binding; blockContent stringList:
/// <c>extractAs:"stringList"</c> + inner <c>contentProperty</c>; flat: schemaProp + contentProp +
/// sourceType).
///
/// <para>Asserts the heuristic now reproduces at least <see cref="RichCoverageFloor"/> of the 12
/// self-contained rich gold mappings (the 25%→50%+ lift) AND that it has not regressed the flat
/// mappings it already got right (<see cref="FlatRegressionFloor"/>). Both thresholds are
/// constants so the lead can tighten them as the heuristic improves.</para>
/// </summary>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class HeuristicRichCoverageTests : IClassFixture<HeuristicRichCoverageFactory>
{
    // --- Tunable gate thresholds -------------------------------------------------------------
    // The 12 self-contained rich gold mappings live across these 8 content types. The heuristic
    // baseline before Stream A reproduced 3/12 (25%). The structural enricher lifts this; the gate
    // requires at least half. Tighten as coverage improves.
    private const int RichGoldTotal = 12;
    private const int RichCoverageFloor = 6; // >= 50% of the 12 rich gold mappings

    // Flat (non-rich) gold mappings the heuristic reproduces. The enricher is purely additive over
    // flat rows, so this must never drop. Measured at 30/38 on the seeded TestHost (the 8 unmatched
    // flat golds are cross-node ancestor/parent/sibling mappings the heuristic cannot derive). Raise
    // this if flat coverage improves so a future regression is caught.
    private const int FlatRegressionFloor = 30;

    private static readonly string[] RichContentTypes =
    [
        "recipePage", "howToPage", "blogArticle", "eventPage",
        "productPage", "faqPage", "homePage", "nestedBlocksPage",
    ];

    // Block element types the string-list branch introspects. Waited on alongside the page types so
    // block extraction is deterministic regardless of uSync import ordering (page content types can
    // appear before their block element types).
    private static readonly string[] RequiredBlockElementTypes =
    [
        "recipeIngredient", "howToTool",
    ];

    private readonly HeuristicRichCoverageFactory _factory;
    private readonly ITestOutputHelper _output;

    public HeuristicRichCoverageTests(HeuristicRichCoverageFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task Heuristic_ReproducesRichMappings_WithoutFlatRegression()
    {
        // Force boot (unattended install) before the uSync first-boot import timer fires.
        _factory.CreateClient().Dispose();

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var contentTypeService = sp.GetRequiredService<IContentTypeService>();
        var autoMapper = sp.GetRequiredService<ISchemaAutoMapper>();

        await WaitForContentTypes(
            contentTypeService,
            RichContentTypes.Concat(RequiredBlockElementTypes).ToList(),
            TimeSpan.FromMinutes(3));

        var goldDir = LocateGoldDirectory();

        var totalRichGold = 0;
        var totalRichHit = 0;
        var totalFlatGold = 0;
        var totalFlatHit = 0;

        foreach (var alias in RichContentTypes)
        {
            var gold = ParseGold(Path.Combine(goldDir, GoldFileName(alias)));
            var suggestions = (await autoMapper.SuggestMappingsAsync(alias, gold.SchemaType)).ToList();

            var score = Score(gold, suggestions);
            totalRichGold += score.RichGold;
            totalRichHit += score.RichHit;
            totalFlatGold += score.FlatGold;
            totalFlatHit += score.FlatHit;

            _output.WriteLine(
                $"{alias,-18} {gold.SchemaType,-12} rich {score.RichHit}/{score.RichGold}  flat {score.FlatHit}/{score.FlatGold}" +
                (score.RichMissed.Count > 0 ? $"  missed: {string.Join(", ", score.RichMissed)}" : string.Empty));
        }

        _output.WriteLine($"TOTAL rich {totalRichHit}/{totalRichGold}   flat {totalFlatHit}/{totalFlatGold}");

        totalRichGold.Should().Be(RichGoldTotal, "the eight rich content types contain twelve self-contained rich gold mappings");
        totalRichHit.Should().BeGreaterThanOrEqualTo(RichCoverageFloor,
            $"the heuristic must reproduce at least {RichCoverageFloor}/{RichGoldTotal} rich gold mappings (was 3/12 before Stream A)");
        totalFlatHit.Should().BeGreaterThanOrEqualTo(FlatRegressionFloor,
            "the structural enricher is additive over flat rows — flat coverage must not regress");
    }

    // --- Scoring (mirrors eval/score.mjs strict semantics) -----------------------------------

    private static readonly HashSet<string> RichSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "complexType", "blockContent",
    };

    private static ScoreResult Score(GoldMapping gold, IReadOnlyList<PropertyMappingSuggestion> suggestions)
    {
        var targets = gold.Mappings
            .Where(m => !string.IsNullOrEmpty(m.SchemaProp)
                        && (!string.IsNullOrEmpty(m.ContentProp) || !Eq(m.SourceType, "property")))
            .ToList();

        var cand = suggestions
            .Select(Normalise)
            .Where(c => !string.IsNullOrEmpty(c.SchemaProp)
                        && (!string.IsNullOrEmpty(c.ContentProp) || !Eq(c.SourceType, "property")))
            .ToList();

        // De-dup candidates by schema property (keep first).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candUnique = cand.Where(c => c.SchemaProp is not null && seen.Add(c.SchemaProp)).ToList();

        var richGold = targets.Where(t => RichSourceTypes.Contains(t.SourceType)).ToList();
        var flatGold = targets.Where(t => !RichSourceTypes.Contains(t.SourceType)).ToList();

        var richMissed = new List<string>();
        var richHit = 0;
        foreach (var g in richGold)
        {
            if (candUnique.Any(c => StrictEq(c, g)))
                richHit++;
            else
                richMissed.Add($"{g.SchemaProp}<-{g.SourceType.ToLowerInvariant()}:{g.ContentProp}{(g.NestedType is null ? "" : "/" + g.NestedType)}");
        }

        var flatHit = flatGold.Count(g => candUnique.Any(c => StrictEq(c, g)));

        return new ScoreResult(richGold.Count, richHit, flatGold.Count, flatHit, richMissed);
    }

    private static Candidate Normalise(PropertyMappingSuggestion s) => new(
        Norm(s.SchemaPropertyName),
        Norm(s.SuggestedContentTypePropertyAlias),
        Norm(s.SuggestedSourceType) ?? "property",
        Norm(s.SuggestedNestedSchemaTypeName),
        ParseJson(s.SuggestedResolverConfig));

    private static bool StrictEq(Candidate a, GoldTarget b)
    {
        if (a.SchemaProp != b.SchemaProp) return false;

        if (b.SourceType == "complextype")
        {
            if (a.SourceType != "complextype") return false;
            if (a.NestedType != b.NestedType) return false;
            return CoversBindings(Bindings(a.Resolver, "complexTypeMappings"), Bindings(b.Resolver, "complexTypeMappings"));
        }

        if (b.SourceType == "blockcontent")
        {
            if (a.SourceType != "blockcontent") return false;
            if (a.ContentProp != b.ContentProp) return false;

            var goldStr = StringListProp(b.Resolver);
            if (goldStr is not null)
            {
                if (a.NestedType is not null) return false;
                return StringListProp(a.Resolver) == goldStr;
            }

            if (a.NestedType != b.NestedType) return false;
            return CoversBindings(Bindings(a.Resolver, "nestedMappings"), Bindings(b.Resolver, "nestedMappings"));
        }

        // flat
        if (a.ContentProp != b.ContentProp) return false;
        if (a.SourceType != b.SourceType) return false;
        if (b.NestedType is not null && a.NestedType != b.NestedType) return false;
        return true;
    }

    private static bool CoversBindings(Dictionary<string, string?> candMap, Dictionary<string, string?> goldMap)
        => goldMap.Count == 0 || goldMap.All(kv => candMap.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static Dictionary<string, string?> Bindings(JsonNode? resolver, string key)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (resolver is JsonObject obj && obj.TryGetPropertyValue(key, out var node) && node is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
            {
                var sp = Norm(GetStr(item, "schemaProperty"));
                var cp = Norm(GetStr(item, "contentProperty") ?? GetStr(item, "contentTypePropertyAlias"));
                if (sp is not null)
                    map[sp] = cp;
            }
        }
        return map;
    }

    private static string? StringListProp(JsonNode? resolver)
    {
        if (resolver is JsonObject obj && Eq(GetStr(obj, "extractAs"), "stringList"))
            return Norm(GetStr(obj, "contentProperty"));
        return null;
    }

    // --- Gold parsing -------------------------------------------------------------------------

    private static GoldMapping ParseGold(string path)
    {
        var doc = XDocument.Load(path);
        var info = doc.Root!.Element("Info");
        var schemaType = info!.Element("SchemaTypeName")!.Value.Trim();

        var mappings = doc.Root!
            .Element("PropertyMappings")!
            .Elements("PropertyMapping")
            .Select(pm => new GoldTarget(
                Norm(pm.Element("SchemaPropertyName")?.Value),
                Norm(pm.Element("SourceType")?.Value) ?? "property",
                Norm(pm.Element("ContentTypePropertyAlias")?.Value),
                Norm(pm.Element("NestedSchemaTypeName")?.Value),
                ParseJson(pm.Element("ResolverConfig")?.Value)))
            .ToList();

        return new GoldMapping(schemaType, mappings);
    }

    private static string GoldFileName(string alias) => alias switch
    {
        // The gold files use a couple of lower-cased names that differ from the content type alias.
        "nestedBlocksPage" => "nestedblockspage.config",
        _ => alias + ".config",
    };

    private static string LocateGoldDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "Umbraco.Community.SchemeWeaver.TestHost", "uSync", "v18", "SchemeWeaverMappings");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the gold SchemeWeaverMappings directory by walking up from the test base directory.");
    }

    // --- Helpers ------------------------------------------------------------------------------

    private static async Task WaitForContentTypes(
        IContentTypeService contentTypeService, IReadOnlyList<string> aliases, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (aliases.All(a => contentTypeService.Get(a) is not null))
                return;
            await Task.Delay(500);
        }

        var missing = aliases.Where(a => contentTypeService.Get(a) is null).ToList();
        throw new InvalidOperationException(
            $"uSync first-boot import did not seed the required content types within {timeout}. Missing: {string.Join(", ", missing)}");
    }

    private static JsonNode? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetStr(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var v) && v is JsonValue val && val.TryGetValue<string>(out var s) ? s : null;

    private static string? Norm(string? value) => string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed record GoldMapping(string SchemaType, List<GoldTarget> Mappings);

    private sealed record GoldTarget(string? SchemaProp, string SourceType, string? ContentProp, string? NestedType, JsonNode? Resolver);

    private sealed record Candidate(string? SchemaProp, string? ContentProp, string SourceType, string? NestedType, JsonNode? Resolver);

    private sealed record ScoreResult(int RichGold, int RichHit, int FlatGold, int FlatHit, List<string> RichMissed);
}
