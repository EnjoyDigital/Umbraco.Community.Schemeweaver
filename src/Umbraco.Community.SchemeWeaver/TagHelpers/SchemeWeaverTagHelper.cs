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

            output.TagName = "script";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("type", "application/ld+json");
            output.Content.SetHtmlContent(jsonLd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentId}", Content.Id);
            output.SuppressOutput();
        }
    }
}
