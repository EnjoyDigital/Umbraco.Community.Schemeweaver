using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

/// <summary>
/// The catalogue's <c>required</c> slots must be exactly the fields the core's Google
/// rich-result rules flag as Critical on an empty node, so the wizard never calls a field
/// "required" the validator does not, and never lets a page through the validator will fail.
/// </summary>
public class SeoCatalogueRuleParityTests
{
    /// <summary>Kinds with no core rule yet; add a rule and remove the kind from here.</summary>
    private static readonly HashSet<string> KindsWithoutRule = ["software-app"];

    private static IReadOnlyList<ITypeRule> Rules { get; } = typeof(ITypeRule).Assembly
        .GetTypes()
        .Where(t => typeof(ITypeRule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .Select(t => (ITypeRule)Activator.CreateInstance(t)!)
        .ToList();

    public static IEnumerable<object[]> Kinds() =>
        SeoTestSupport.Catalogue.All.Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RequiredSlots_MatchTheRulesCriticalChecks(string kindId)
    {
        var template = SeoTestSupport.Catalogue.Get(kindId)!;
        var rules = Rules.Where(r => r is not GenericEligibleRule && r.AppliesTo(template.SchemaType)).ToList();

        if (KindsWithoutRule.Contains(kindId))
        {
            rules.Should().BeEmpty($"'{kindId}' is listed as having no rule; remove it from the allow-list now that one exists");
            return;
        }

        rules.Should().NotBeEmpty($"kind '{kindId}' ({template.SchemaType}) needs a core ITypeRule");

        using var doc = JsonDocument.Parse($$"""{"@type":"{{template.SchemaType}}"}""");
        var critical = rules
            .SelectMany(r => r.Check(doc.RootElement, "$"))
            .Where(i => i.Severity == ValidationSeverity.Critical)
            .ToList();

        var criticalFields = critical
            .Select(i => i.Path)
            .Where(p => p.StartsWith("$.", StringComparison.Ordinal))
            .Select(p => p["$.".Length..])
            .Where(f => !f.Contains('.') && !f.Contains('['))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiredSlots = template.Slots
            .Where(s => s.IsRequired && s.AnyOfGroup is null)
            .Select(s => s.SchemaProperty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        requiredSlots.Should().BeEquivalentTo(criticalFields,
            $"the '{kindId}' kind's required slots must equal the Critical top-level checks of {string.Join(", ", rules.Select(r => r.GetType().Name))}");

        // A bare "$" Critical means "one of several properties": the kind must model it as an any-of group.
        var hasAnyOfCritical = critical.Any(i => i.Path == "$");
        var hasAnyOfGroup = template.Slots.Any(s => s.AnyOfGroup is not null);
        hasAnyOfGroup.Should().Be(hasAnyOfCritical,
            $"'{kindId}' must declare an anyOfGroup exactly when its rule has a whole-node Critical check");
    }

    [Fact]
    public void EveryVariant_HasTheSameCriticalChecksAsItsKind()
    {
        foreach (var template in SeoTestSupport.Catalogue.All.Where(t => !KindsWithoutRule.Contains(t.Id)))
        {
            var baseFields = CriticalFields(template.SchemaType);
            foreach (var variant in template.Variants.Where(v => !string.Equals(v.SchemaType, template.SchemaType, StringComparison.Ordinal)))
            {
                CriticalFields(variant.SchemaType).Should().BeEquivalentTo(baseFields,
                    $"variant {variant.SchemaType} of '{template.Id}' must require the same fields as {template.SchemaType}, or the per-page retype would change what is required");
            }
        }
    }

    private static HashSet<string> CriticalFields(string schemaType)
    {
        using var doc = JsonDocument.Parse($$"""{"@type":"{{schemaType}}"}""");
        return Rules
            .Where(r => r is not GenericEligibleRule && r.AppliesTo(schemaType))
            .SelectMany(r => r.Check(doc.RootElement, "$"))
            .Where(i => i.Severity == ValidationSeverity.Critical)
            .Select(i => i.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
