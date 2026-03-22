using System.Net;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.TagHelpers;

/// <summary>
/// Tag helper that outputs JSON-LD structured data for published content.
/// Usage: &lt;scheme-weaver content="@Model" /&gt;
/// With CSP nonce: &lt;scheme-weaver content="@Model" nonce="@cspNonce" /&gt;
/// With data-nonce: &lt;scheme-weaver content="@Model" nonce="@cspNonce" nonce-data-attribute /&gt;
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

    /// <summary>
    /// Optional CSP nonce value to add to generated script tags.
    /// </summary>
    [HtmlAttributeName("nonce")]
    public string? Nonce { get; set; }

    /// <summary>
    /// When true, emits the nonce as a data-nonce attribute instead of the standard nonce attribute.
    /// </summary>
    [HtmlAttributeName("nonce-data-attribute")]
    public bool NonceDataAttribute { get; set; }

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

            output.TagName = "script";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute(new TagHelperAttribute("type", new HtmlString("application/ld+json"), HtmlAttributeValueStyle.DoubleQuotes));

            // Apply CSP nonce to the main script tag
            if (!string.IsNullOrEmpty(Nonce))
            {
                var attrName = NonceDataAttribute ? "data-nonce" : "nonce";
                output.Attributes.SetAttribute(attrName, Nonce);
            }

            output.Content.SetHtmlContent(jsonLd);

            // Build nonce attribute string for PostContent scripts
            var nonceAttr = BuildNonceAttribute();

            // Output BreadcrumbList as a separate JSON-LD block
            var breadcrumbJson = _generator.GenerateBreadcrumbJsonLd(Content);
            if (!string.IsNullOrEmpty(breadcrumbJson))
            {
                output.PostContent.AppendHtml($"\n<script type=\"application/ld+json\"{nonceAttr}>{breadcrumbJson}</script>");
            }

            // Output inherited schemas from ancestor nodes
            foreach (var inheritedJsonLd in _generator.GenerateInheritedJsonLdStrings(Content))
            {
                output.PostContent.AppendHtml($"\n<script type=\"application/ld+json\"{nonceAttr}>{inheritedJsonLd}</script>");
            }

            // Output schemas from mapped block elements
            foreach (var blockJsonLd in _generator.GenerateBlockElementJsonLdStrings(Content))
            {
                output.PostContent.AppendHtml($"\n<script type=\"application/ld+json\"{nonceAttr}>{blockJsonLd}</script>");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentId}", Content.Id);
            output.SuppressOutput();
        }
    }

    private string BuildNonceAttribute()
    {
        if (string.IsNullOrEmpty(Nonce))
            return string.Empty;

        var encodedNonce = WebUtility.HtmlEncode(Nonce);
        return NonceDataAttribute
            ? $" data-nonce=\"{encodedNonce}\""
            : $" nonce=\"{encodedNonce}\"";
    }
}
