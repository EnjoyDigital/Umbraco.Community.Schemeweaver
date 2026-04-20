using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.TagHelpers;

/// <summary>
/// Tag helper that outputs JSON-LD structured data for published content.
/// Usage: &lt;scheme-weaver content="@Model" /&gt;
/// </summary>
[HtmlTargetElement("scheme-weaver")]
public class SchemeWeaverTagHelper : TagHelper
{
    private readonly IJsonLdGenerator _generator;
    private readonly IGraphGenerator _graphGenerator;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<SchemeWeaverTagHelper> _logger;

    public SchemeWeaverTagHelper(
        IJsonLdGenerator generator,
        IGraphGenerator graphGenerator,
        IVariationContextAccessor variationContextAccessor,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SchemeWeaverTagHelper> logger)
    {
        _generator = generator;
        _graphGenerator = graphGenerator;
        _variationContextAccessor = variationContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    [HtmlAttributeName("content")]
    public IPublishedContent? Content { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;

        if (Content is null)
        {
            output.SuppressOutput();
            return;
        }

        try
        {
            var culture = _variationContextAccessor.VariationContext?.Culture;

            if (_options.UseGraphModel)
            {
                // v1.4+: a single Yoast-style @graph script carries every piece
                // (Organization, WebSite, WebPage, Breadcrumb, main entity, …).
                var graphJson = _graphGenerator.GenerateGraphJson(Content, culture);
                if (!string.IsNullOrEmpty(graphJson))
                {
                    output.Content.AppendHtml($"<script type=\"application/ld+json\">{graphJson}</script>");
                    return;
                }
                output.SuppressOutput();
                return;
            }

            // Legacy path — one script tag per piece of source data.
            var hasOutput = false;

            foreach (var inheritedJsonLd in _generator.GenerateInheritedJsonLdStrings(Content, culture))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{inheritedJsonLd}</script>");
                hasOutput = true;
            }

            var breadcrumbJson = _generator.GenerateBreadcrumbJsonLd(Content, culture);
            if (!string.IsNullOrEmpty(breadcrumbJson))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{breadcrumbJson}</script>");
                hasOutput = true;
            }

            var jsonLd = _generator.GenerateJsonLdString(Content, culture);
            if (!string.IsNullOrEmpty(jsonLd))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{jsonLd}</script>");
                hasOutput = true;
            }

            foreach (var blockJsonLd in _generator.GenerateBlockElementJsonLdStrings(Content, culture))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{blockJsonLd}</script>");
                hasOutput = true;
            }

            if (!hasOutput)
            {
                output.SuppressOutput();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentId}", Content.Id);
            output.SuppressOutput();
        }
    }
}
