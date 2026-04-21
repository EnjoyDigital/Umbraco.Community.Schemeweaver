using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// One Google Rich Results rule-set for a Schema.org type (or a family of types).
/// </summary>
public interface ITypeRule
{
    /// <summary>
    /// Return true when this rule applies to a node with the given <c>@type</c>.
    /// Typically matches one or more closely-related types (e.g. ArticleRule
    /// matches Article / BlogPosting / NewsArticle / TechArticle).
    /// </summary>
    bool AppliesTo(string schemaType);

    /// <summary>
    /// Run the rule against one JSON-LD node. <paramref name="path"/> is the
    /// locator of this node within the document (e.g. <c>@graph[3]</c>).
    /// Implementations should yield issues with <c>path + "." + fieldName</c>.
    /// </summary>
    IEnumerable<ValidationIssue> Check(JsonElement node, string path);
}
