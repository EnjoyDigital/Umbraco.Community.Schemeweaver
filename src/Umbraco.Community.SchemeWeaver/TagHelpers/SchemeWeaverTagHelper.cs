using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
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
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly ILogger<SchemeWeaverTagHelper> _logger;

    public SchemeWeaverTagHelper(
        IJsonLdGenerator generator,
        IVariationContextAccessor variationContextAccessor,
        ILogger<SchemeWeaverTagHelper> logger)
    {
        _generator = generator;
        _variationContextAccessor = variationContextAccessor;
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
            var hasOutput = false;
            var culture = _variationContextAccessor.VariationContext?.Culture;

            // 1. Inherited schemas from ancestor nodes (root-first order)
            foreach (var inheritedJsonLd in _generator.GenerateInheritedJsonLdStrings(Content, culture))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{inheritedJsonLd}</script>");
                hasOutput = true;
            }

            // 2. BreadcrumbList as a separate JSON-LD block
            var breadcrumbJson = _generator.GenerateBreadcrumbJsonLd(Content, culture);
            if (!string.IsNullOrEmpty(breadcrumbJson))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{breadcrumbJson}</script>");
                hasOutput = true;
            }

            // 3. Main page schema
            var jsonLd = _generator.GenerateJsonLdString(Content, culture);
            if (!string.IsNullOrEmpty(jsonLd))
            {
                var prefix = hasOutput ? "\n" : "";
                output.Content.AppendHtml($"{prefix}<script type=\"application/ld+json\">{jsonLd}</script>");
                hasOutput = true;
            }

            // 4. Schemas from mapped block elements
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
