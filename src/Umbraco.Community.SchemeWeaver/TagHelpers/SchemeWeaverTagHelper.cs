using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
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
    private readonly ILogger<SchemeWeaverTagHelper> _logger;

    public SchemeWeaverTagHelper(IJsonLdGenerator generator, ILogger<SchemeWeaverTagHelper> logger)
    {
        _generator = generator;
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
            var jsonLd = _generator.GenerateJsonLdString(Content);
            if (string.IsNullOrEmpty(jsonLd))
            {
                output.SuppressOutput();
                return;
            }

            // Output all script blocks as independent elements via Content.
            // Do NOT use output.TagName = "script" — that wraps everything in a single
            // <script>...</script> and PostContent would nest inside it, breaking the HTML.
            output.Content.AppendHtml($"<script type=\"application/ld+json\">{jsonLd}</script>");

            // BreadcrumbList as a separate JSON-LD block
            var breadcrumbJson = _generator.GenerateBreadcrumbJsonLd(Content);
            if (!string.IsNullOrEmpty(breadcrumbJson))
            {
                output.Content.AppendHtml($"\n<script type=\"application/ld+json\">{breadcrumbJson}</script>");
            }

            // Inherited schemas from ancestor nodes
            foreach (var inheritedJsonLd in _generator.GenerateInheritedJsonLdStrings(Content))
            {
                output.Content.AppendHtml($"\n<script type=\"application/ld+json\">{inheritedJsonLd}</script>");
            }

            // Schemas from mapped block elements
            foreach (var blockJsonLd in _generator.GenerateBlockElementJsonLdStrings(Content))
            {
                output.Content.AppendHtml($"\n<script type=\"application/ld+json\">{blockJsonLd}</script>");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentId}", Content.Id);
            output.SuppressOutput();
        }
    }
}
