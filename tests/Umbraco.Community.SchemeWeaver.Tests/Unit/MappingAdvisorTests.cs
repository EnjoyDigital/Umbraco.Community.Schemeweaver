using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Advisory;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Drives <see cref="MappingAdvisor"/> against the REAL <see cref="SchemaTypeRegistry"/> so the
/// accepted-type ranges (plain-text vs structured vs ItemList) are exercised faithfully — exactly
/// like <c>SchemaRangeValidatorTests</c>.
/// </summary>
public class MappingAdvisorTests
{
    private readonly MappingAdvisor _sut;

    public MappingAdvisorTests()
    {
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        _sut = new MappingAdvisor(registry);
    }

    private static string Routes(string nestedType, params string[] mappedProps)
        => JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes =
            [
                new BlockRoute
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = nestedType,
                    PropertyMappings = mappedProps
                        .Select(p => new NestedPropertyMapping { SchemaProperty = p, ContentProperty = p })
                        .ToList(),
                },
            ],
        });

    // --- Check 1: stripHtml. headline is a pure-text range ([String]); description is NOT
    // ([String, TextObject]) so it is deliberately exempt — a TextObject value is legitimate there. ---

    [Fact]
    public void AdviseEntry_RichTextToPlainTextPropertyWithoutTransform_SuggestsStripHtml()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "headline", "property", ContentEditorAlias: "Umbraco.RichText"));

        advice.Should().ContainSingle(a => a.Kind == MappingAdviceKind.StripHtml);
        advice.Single().Fix!.TransformType.Should().Be("stripHtml");
        advice.Single().Path.Should().Be("headline");
    }

    [Fact]
    public void AdviseEntry_RichTextToPlainTextPropertyWithStripHtmlAlready_NoAdvice()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "headline", "property", ContentEditorAlias: "Umbraco.RichText", TransformType: "stripHtml"));

        advice.Should().BeEmpty();
    }

    [Fact]
    public void AdviseEntry_NonHtmlEditorToPlainTextProperty_NoAdvice()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "headline", "property", ContentEditorAlias: "Umbraco.TextBox"));

        advice.Should().BeEmpty();
    }

    [Fact]
    public void AdviseEntry_RichTextToMixedTextObjectRangeProperty_NoAdvice()
    {
        // description accepts [String, TextObject] — a structured value is valid, so don't suggest stripHtml.
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "description", "property", ContentEditorAlias: "Umbraco.RichText"));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.StripHtml);
    }

    [Fact]
    public void AdviseEntry_RichTextToHtmlAllowedProperty_NoAdvice()
    {
        // articleBody legitimately carries HTML — must not nag.
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "articleBody", "property", ContentEditorAlias: "Umbraco.RichText"));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.StripHtml);
    }

    [Fact]
    public void AdviseEntry_RichTextToObjectRangeProperty_NoAdvice()
    {
        // author accepts Person/Organization — not a plain-text range, so stripHtml is irrelevant.
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "Article", "author", "property", ContentEditorAlias: "Umbraco.RichText"));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.StripHtml);
    }

    // --- Check 2: wrapInListItem ---

    [Fact]
    public void AdviseEntry_BlockContentToItemListElementWithoutWrap_SuggestsWrapInListItem()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "ItemList", "itemListElement", "blockContent", ResolverConfig: Routes("Service", "name")));

        advice.Should().ContainSingle(a => a.Kind == MappingAdviceKind.WrapInListItem);
        advice.Single(a => a.Kind == MappingAdviceKind.WrapInListItem).Fix!.WrapInListItem.Should().BeTrue();
    }

    [Fact]
    public void AdviseEntry_BlockContentToItemListElementAlreadyWrapped_NoAdvice()
    {
        var config = JsonSerializer.Serialize(new ResolverConfigModel
        {
            WrapInListItem = true,
            Routes = [new BlockRoute { BlockAlias = "serviceItem", NestedSchemaType = "Service" }],
        });

        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "ItemList", "itemListElement", "blockContent", ResolverConfig: config));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.WrapInListItem);
    }

    [Fact]
    public void AdviseEntry_BlockContentStringListExtraction_NoWrapAdvice()
    {
        var config = JsonSerializer.Serialize(new ResolverConfigModel
        {
            ExtractAs = "stringList",
            ContentProperty = "ingredient",
        });

        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "ItemList", "itemListElement", "blockContent", ResolverConfig: config));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.WrapInListItem);
    }

    // --- Check 3: missing required nested property ---

    [Fact]
    public void AdviseEntry_QuestionRouteMissingAcceptedAnswer_SuggestsAcceptedAnswer()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "FAQPage", "mainEntity", "blockContent", ResolverConfig: Routes("Question", "name")));

        advice.Should().ContainSingle(a => a.Kind == MappingAdviceKind.MissingRequiredNestedProperty);
        advice.Single(a => a.Kind == MappingAdviceKind.MissingRequiredNestedProperty)
            .Message.Should().Contain("acceptedAnswer");
    }

    [Fact]
    public void AdviseEntry_QuestionRouteWithAcceptedAnswerMapped_NoAdvice()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "FAQPage", "mainEntity", "blockContent", ResolverConfig: Routes("Question", "name", "acceptedAnswer")));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.MissingRequiredNestedProperty);
    }

    [Fact]
    public void AdviseEntry_UnknownNestedType_NoAdvice()
    {
        var advice = _sut.AdviseEntry(new MappingEntryInput(
            "ItemList", "itemListElement", "blockContent", ResolverConfig: Routes("Service", "name")));

        advice.Should().NotContain(a => a.Kind == MappingAdviceKind.MissingRequiredNestedProperty);
    }

    // --- Check 4: persistence ---

    [Fact]
    public void AdvisePersistence_DbOnlyUSyncAvailableExportDisabled_SuggestsEnableExport()
    {
        var advice = _sut.AdvisePersistence("Article",
            new PersistenceFacts(MappingDriftStatus.DbOnly, USyncAvailable: true, ExportOnSaveEnabled: false));

        advice.Should().NotBeNull();
        advice!.Kind.Should().Be(MappingAdviceKind.ExportToUSync);
        advice.Message.Should().Contain("ExportMappingsToUSyncOnSave");
    }

    [Fact]
    public void AdvisePersistence_ContentDiffersExportEnabled_SuggestsRunExport()
    {
        var advice = _sut.AdvisePersistence("Article",
            new PersistenceFacts(MappingDriftStatus.ContentDiffers, USyncAvailable: true, ExportOnSaveEnabled: true));

        advice.Should().NotBeNull();
        advice!.Message.Should().Contain("export-mappings-to-usync");
    }

    [Fact]
    public void AdvisePersistence_InSync_NoAdvice()
    {
        var advice = _sut.AdvisePersistence("Article",
            new PersistenceFacts(MappingDriftStatus.InSync, USyncAvailable: true, ExportOnSaveEnabled: false));

        advice.Should().BeNull();
    }

    [Fact]
    public void AdvisePersistence_USyncUnavailable_NoAdvice()
    {
        var advice = _sut.AdvisePersistence("Article",
            new PersistenceFacts(MappingDriftStatus.DbOnly, USyncAvailable: false, ExportOnSaveEnabled: false));

        advice.Should().BeNull();
    }
}
