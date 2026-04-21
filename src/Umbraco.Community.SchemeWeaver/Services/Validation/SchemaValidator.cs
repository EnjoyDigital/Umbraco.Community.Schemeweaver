using System.Text.Json;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Walks a JSON-LD document and dispatches each node to every applicable
/// <see cref="ITypeRule"/>. Handles single-Thing documents and graph
/// envelopes transparently.
/// </summary>
public sealed class SchemaValidator : ISchemaValidator
{
    private readonly IReadOnlyList<ITypeRule> _rules;

    public SchemaValidator(IEnumerable<ITypeRule> rules)
    {
        _rules = rules.ToList();
    }

    public ValidationResult Validate(string jsonLd)
    {
        if (string.IsNullOrWhiteSpace(jsonLd))
            return ValidationResult.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonLd);
        }
        catch (JsonException)
        {
            return new ValidationResult(new[]
            {
                new ValidationIssue(ValidationSeverity.Critical, "(unparsable)", "$",
                    "JSON-LD payload failed to parse as JSON."),
            });
        }

        using (doc)
        {
            var issues = new List<ValidationIssue>();
            var root = doc.RootElement;

            // Graph envelope: {"@context":..., "@graph":[...]}
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("@graph", out var graph)
                && graph.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var node in graph.EnumerateArray())
                {
                    ValidateNode(node, $"@graph[{i}]", issues);
                    i++;
                }
            }
            // Bare Thing
            else if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateNode(root, "$", issues);
            }
            // Pre-v1.4 array-of-Things shape
            else if (root.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var node in root.EnumerateArray())
                {
                    ValidateNode(node, $"[{i}]", issues);
                    i++;
                }
            }

            return new ValidationResult(issues);
        }
    }

    private void ValidateNode(JsonElement node, string path, List<ValidationIssue> issues)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        // A bare @id-reference (emitted when a graph piece cross-links to
        // another piece) has no substance of its own and doesn't need validating.
        if (IsBareIdReference(node))
            return;

        var type = node.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? string.Empty
            : string.Empty;

        foreach (var rule in _rules.Where(r => r.AppliesTo(type)))
        {
            foreach (var issue in rule.Check(node, path))
                issues.Add(issue);
        }
    }

    private static bool IsBareIdReference(JsonElement node)
    {
        // {"@id":"..."} or {"@type":"...","@id":"..."} and nothing else
        var count = 0;
        var hasId = false;
        foreach (var prop in node.EnumerateObject())
        {
            count++;
            if (prop.Name == "@id") hasId = true;
            if (count > 2) return false;
        }
        return hasId && count <= 2;
    }
}
