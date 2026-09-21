using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="TypeSafeSchemaAutoMapper"/>, the decorator on the auto-map seam: when to consult
/// TypeSafe, what it hands over as priors, and the never-breaks fallback to the prior mapper.
/// </summary>
public class TypeSafeSchemaAutoMapperTests
{
    private readonly ITypeSafePropertyMapper _mapper = Substitute.For<ITypeSafePropertyMapper>();
    private readonly ITypeSafeClient _client = Substitute.For<ITypeSafeClient>();
    private readonly ISchemaAutoMapper _prior = Substitute.For<ISchemaAutoMapper>();
    private readonly IContentTypeService _heuristicContentTypes = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _heuristicRegistry = Substitute.For<ISchemaTypeRegistry>();
    private readonly RecordingLogger<TypeSafeSchemaAutoMapper> _logger = new();

    private static readonly PropertyMappingSuggestion PriorRow = new() { SchemaPropertyName = "FromPrior", Confidence = 80 };
    private static readonly PropertyMappingSuggestion TypeSafeRow = new() { SchemaPropertyName = "FromTypeSafe", Confidence = 91 };

    public TypeSafeSchemaAutoMapperTests()
    {
        // The concrete heuristic is real: a content type with "headline" against a registry
        // that says BlogPosting has Headline, so it yields one exact-alias row at 100.
        var blogPost = ContentType("blogPost", "Blog Post", ("headline", TextBox));
        _heuristicContentTypes.Get("blogPost").Returns(blogPost);
        _heuristicRegistry.GetProperties("BlogPosting").Returns([new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }]);

        _prior.SuggestMappingsAsync("blogPost", "BlogPosting").Returns(Task.FromResult<IEnumerable<PropertyMappingSuggestion>>([PriorRow]));
        _prior.SuggestMappings("blogPost", "BlogPosting").Returns([PriorRow]);
    }

    private TypeSafeSchemaAutoMapper Create() => new(
        _mapper,
        _client,
        _prior,
        new SchemaAutoMapper(_heuristicContentTypes, _heuristicRegistry),
        Options.Create(new TypeSafeOptions { ApiKey = "test-key", Model = "jev-test" }),
        _logger);

    [Fact]
    public async Task NotConfigured_GoesStraightToThePrior()
    {
        _client.IsConfigured.Returns(false);

        var result = await Create().SuggestMappingsAsync("blogPost", "BlogPosting");

        result.Should().ContainSingle().Which.Should().BeSameAs(PriorRow);
        await _mapper.DidNotReceiveWithAnyArgs().MapAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Configured_PassesHeuristicRowsAsPriorsAndReturnsTheMapperResult()
    {
        _client.IsConfigured.Returns(true);
        IReadOnlyList<PropertyMappingSuggestion>? captured = null;
        _mapper.MapAsync("blogPost", "BlogPosting", Arg.Do<IReadOnlyList<PropertyMappingSuggestion>>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PropertyMappingSuggestion>>([TypeSafeRow]));

        var result = await Create().SuggestMappingsAsync("blogPost", "BlogPosting");

        result.Should().ContainSingle().Which.Should().BeSameAs(TypeSafeRow);
        captured.Should().NotBeNull();
        captured!.Should().ContainSingle(p => p.SchemaPropertyName == "Headline" && p.Confidence == 100,
            "the heuristic's own suggestions are the priors, whatever the prior mapper is");
        await _prior.DidNotReceiveWithAnyArgs().SuggestMappingsAsync(default!, default!);
    }

    [Fact]
    public async Task MapperThrows_FallsBackToThePriorAndLogsAWarning()
    {
        _client.IsConfigured.Returns(true);
        var failure = new TypeSafeApiException("HTTP 529 and retries exhausted", 529);
        _mapper.MapAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(failure);

        var result = await Create().SuggestMappingsAsync("blogPost", "BlogPosting");

        result.Should().ContainSingle().Which.Should().BeSameAs(PriorRow);
        var warning = _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Exception.Should().BeSameAs(failure);
        warning.Message.Should().Contain("falling back");
    }

    [Fact]
    public async Task MapperThrowsAnythingElse_StillFallsBack()
    {
        // By policy: any failure in the assist path degrades to the prior, never to an error.
        _client.IsConfigured.Returns(true);
        _mapper.MapAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("unexpected"));

        var result = await Create().SuggestMappingsAsync("blogPost", "BlogPosting");

        result.Should().ContainSingle().Which.Should().BeSameAs(PriorRow);
    }

    [Fact]
    public void SuggestMappings_DelegatesToThePrior()
    {
        _client.IsConfigured.Returns(true);

        var result = Create().SuggestMappings("blogPost", "BlogPosting");

        result.Should().ContainSingle().Which.Should().BeSameAs(PriorRow);
        _mapper.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void RankSchemaProperties_DelegatesToThePrior()
    {
        var ranked = new[] { new RankedSchemaPropertyInfo { Name = "Headline", Confidence = 90 } };
        _prior.RankSchemaProperties("BlogPosting").Returns(ranked);

        var result = Create().RankSchemaProperties("BlogPosting");

        result.Should().BeSameAs(ranked);
        _prior.Received(1).RankSchemaProperties("BlogPosting");
    }

    [Fact]
    public void PriorTypeName_ReportsTheWrappedMapper()
    {
        var sut = Create();

        sut.Prior.Should().BeSameAs(_prior);
        sut.PriorTypeName.Should().Be(_prior.GetType().Name);
    }
}
