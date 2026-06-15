using Umbraco.AI.Core.Tools.Scopes;

namespace Umbraco.Community.SchemeWeaver.AI.Tools;

/// <summary>
/// Tool scope for SchemeWeaver schema mapping operations.
/// </summary>
[AIToolScope("schemeweaver-mapping", Icon = "icon-brackets", Domain = "SchemeWeaver")]
public sealed class SchemeWeaverMappingScope : AIToolScopeBase
{
    public const string ScopeId = "schemeweaver-mapping";
}
