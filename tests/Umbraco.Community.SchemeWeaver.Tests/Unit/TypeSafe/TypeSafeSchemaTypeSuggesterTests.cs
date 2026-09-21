using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="TypeSafeSchemaTypeSuggester"/>: hierarchical classification from <c>Thing</c> by
/// beam search, with a scripted client standing in for the model. The real type graph supplies
/// the levels, so the script only has to say which child to take at each type it is asked about.
/// </summary>
public class TypeSafeSchemaTypeSuggesterTests
{
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key" };

    public TypeSafeSchemaTypeSuggesterTests()
    {
        // Built BEFORE the Returns call: the builder configures other substitutes, which would
        // otherwise reset NSubstitute's "last call" between Get() and Returns().
        var blogPost = ContentType("blogPost", "Blog Post", ("title", TextBox), ("body", "Umbraco.RichText"));
        _contentTypes.Get("blogPost").Returns(blogPost);
        _contentTypes.Get("missing").Returns((IContentType?)null);
    }

    private TypeSafeSchemaTypeSuggester Create(ITypeSafeClient client) => new(
        client,
        SharedSchemaRegistry.Graph,
        _contentTypes,
        _provider,
        Options.Create(_options),
        new RecordingLogger<TypeSafeSchemaTypeSuggester>());

    /// <summary>A client that, at each descent level, follows <paramref name="script"/> for the type it is asked from and stops everywhere else.</summary>
    private static FakeTypeSafeClient Scripted(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> script)
        => new((_, q) =>
        {
            var current = FakeTypeSafeClient.CurrentTypeOf(q);
            return current is not null && script.TryGetValue(current, out var distribution)
                ? FakeTypeSafeClient.Distribution(distribution)
                : FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["__stop"] = 1.0 });
        });

    private static IReadOnlyDictionary<string, double> Take(string child, double p = 0.9)
        => new Dictionary<string, double> { [child] = p };

    [Fact]
    public async Task Suggest_DescendsToBlogPosting_WithRootFirstPathAndPercentConfidence()
    {
        // Schema.org places BlogPosting under SocialMediaPosting, under Article: every level is
        // one real edge of the registry's graph, so the script has to walk all of them.
        var client = Scripted(new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["Thing"] = Take("CreativeWork"),
            ["CreativeWork"] = Take("Article"),
            ["Article"] = Take("SocialMediaPosting"),
            ["SocialMediaPosting"] = Take("BlogPosting"),
            ["BlogPosting"] = Take("__stop", 1.0),
        });

        var results = await Create(client).SuggestAsync("blogPost");

        results.Should().NotBeEmpty();
        var top = results[0];
        top.SchemaTypeName.Should().Be("BlogPosting");
        top.Path.Should().Equal("Thing", "CreativeWork", "Article", "SocialMediaPosting", "BlogPosting");
        top.Confidence.Should().BeInRange(1, 100);
        top.Confidence.Should().Be(92, "the geometric mean of four 0.9 edges and the 1.0 stop edge is 0.919");
        client.Requests.Should().NotBeEmpty();
        client.AskedIds.Should().OnlyContain(id => id.StartsWith("desc__type__", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Suggest_AnswerNamingOnlyUnofferedOptions_StopsWhereItIsInsteadOfThrowing()
    {
        // The wire is untrusted: a distribution over options the question never offered must
        // degrade like a missing answer (stop, unpenalised), never surface as an exception.
        var client = new FakeTypeSafeClient((_, _) =>
            FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["NotAType"] = 0.7, ["AlsoNotOffered"] = 0.3 }));

        var results = await Create(client).SuggestAsync("blogPost");

        results.Should().ContainSingle();
        results[0].SchemaTypeName.Should().Be("Thing");
        results[0].Confidence.Should().Be(100);
    }

    [Fact]
    public async Task Suggest_StopAtRoot_YieldsThing()
    {
        var client = Scripted(new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["Thing"] = Take("__stop", 1.0),
        });

        var results = await Create(client).SuggestAsync("blogPost");

        results.Should().ContainSingle();
        results[0].SchemaTypeName.Should().Be("Thing");
        results[0].Path.Should().Equal("Thing");
        results[0].Confidence.Should().Be(100);
    }

    [Fact]
    public async Task Suggest_UnknownContentType_IsEmptyWithoutAskingTheModel()
    {
        var client = Scripted(new Dictionary<string, IReadOnlyDictionary<string, double>>());

        var results = await Create(client).SuggestAsync("missing");

        results.Should().BeEmpty();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Suggest_ReturnsDistinctTypes_WhenTwoPathsReachTheSameType()
    {
        // LocalBusiness is reachable via Organization and via Place; only the better path
        // represents it, and the list never repeats a type.
        var client = Scripted(new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["Thing"] = new Dictionary<string, double> { ["Organization"] = 0.5, ["Place"] = 0.5 },
            ["Organization"] = Take("LocalBusiness", 0.9),
            ["Place"] = Take("LocalBusiness", 0.8),
            ["LocalBusiness"] = Take("__stop", 1.0),
        });

        var results = await Create(client).SuggestAsync("blogPost", maxResults: 3);

        results.Select(r => r.SchemaTypeName).Should().OnlyHaveUniqueItems();
        var localBusiness = results.Should().ContainSingle(r => r.SchemaTypeName == "LocalBusiness").Subject;
        localBusiness.Path.Should().Equal("Thing", "Organization", "LocalBusiness");
    }

    [Fact]
    public async Task Suggest_HonoursMaxResults()
    {
        var client = Scripted(new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["Thing"] = new Dictionary<string, double> { ["CreativeWork"] = 0.4, ["Organization"] = 0.3, ["Person"] = 0.3 },
        });

        (await Create(client).SuggestAsync("blogPost", maxResults: 0)).Should().BeEmpty();
        (await Create(client).SuggestAsync("blogPost", maxResults: 1)).Should().ContainSingle()
            .Which.SchemaTypeName.Should().Be("CreativeWork");
        (await Create(client).SuggestAsync("blogPost", maxResults: 3)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Suggest_ApiFailure_PropagatesToTheCaller()
    {
        var client = new FakeTypeSafeClient((_, _) => null) { Throws = new TypeSafeApiException("HTTP 529", 529) };

        var act = () => Create(client).SuggestAsync("blogPost");

        await act.Should().ThrowAsync<TypeSafeApiException>();
    }
}
