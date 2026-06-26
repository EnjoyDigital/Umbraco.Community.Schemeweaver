using FluentAssertions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class MappingReachabilityClassifierTests
{
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly MappingReachabilityClassifier _sut;

    public MappingReachabilityClassifierTests()
    {
        _sut = new MappingReachabilityClassifier(_contentTypeService);
    }

    [Fact]
    public void Classify_ElementType_ReturnsComposedFromBlock()
    {
        var ct = Substitute.For<IContentType>();
        ct.IsElement.Returns(true);
        _contentTypeService.Get("reviewBlock").Returns(ct);

        _sut.Classify("reviewBlock").Should().Be("composed-from-block");
    }

    [Fact]
    public void Classify_DocumentType_ReturnsRoutedPage()
    {
        var ct = Substitute.For<IContentType>();
        ct.IsElement.Returns(false);
        _contentTypeService.Get("article").Returns(ct);

        _sut.Classify("article").Should().Be("routed-page");
    }

    [Fact]
    public void Classify_UnknownAlias_ReturnsUnknown()
    {
        _contentTypeService.Get("ghost").Returns((IContentType?)null);

        _sut.Classify("ghost").Should().Be("unknown");
    }

    [Fact]
    public void Classify_BlankAlias_ReturnsUnknown()
    {
        _sut.Classify("").Should().Be("unknown");
    }

    [Fact]
    public void ComposedFromBlockWarning_IsHedged_AndNeverAssertsEmission()
    {
        // The classifier can't know whether any page actually routes the element,
        // so the surfaced message must be conditional ("only when ... if no page
        // routes this type ... never emits on its own route") and must not claim
        // the mapping will emit.
        var warning = MappingReachabilityClassifier.ComposedFromBlockWarning;

        warning.Should().Contain("only when");
        warning.Should().Contain("if no page routes this type");
        warning.Should().Contain("never emits on its own route");
    }
}
