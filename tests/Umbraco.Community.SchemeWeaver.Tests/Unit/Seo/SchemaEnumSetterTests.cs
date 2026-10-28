using System.Text.Json;
using FluentAssertions;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

/// <summary>The catalogue's fixed enum values (availability, event status…) must reach the output.</summary>
public class SchemaEnumSetterTests
{
    [Theory]
    [InlineData("https://schema.org/InStock", "InStock")]
    [InlineData("InStock", "InStock")]
    [InlineData("http://schema.org/OutOfStock", "OutOfStock")]
    [InlineData("preorder", "PreOrder")]
    public void Offer_Availability_IsSetFromText(string input, string expected)
    {
        var offer = new Offer();
        SchemaPropertySetter.SetPropertyValue(offer, "Availability", input);
        var json = JsonDocument.Parse(offer.ToString()).RootElement;
        json.GetProperty("availability").GetString().Should().EndWith(expected);
    }

    [Fact]
    public void Event_StatusAndAttendanceMode_AreSetFromText()
    {
        var ev = new Event();
        SchemaPropertySetter.SetPropertyValue(ev, "EventStatus", "https://schema.org/EventScheduled");
        SchemaPropertySetter.SetPropertyValue(ev, "EventAttendanceMode", "OnlineEventAttendanceMode");
        var json = JsonDocument.Parse(ev.ToString()).RootElement;
        json.GetProperty("eventStatus").GetString().Should().Contain("EventScheduled");
        json.GetProperty("eventAttendanceMode").GetString().Should().Contain("OnlineEventAttendanceMode");
    }

    [Theory]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("-1")]
    public void NumericText_IsNotTreatedAsAnEnumMember(string input)
    {
        var offer = new Offer();
        SchemaPropertySetter.SetPropertyValue(offer, "Availability", input);
        JsonDocument.Parse(offer.ToString()).RootElement.TryGetProperty("availability", out _).Should().BeFalse();
    }

    [Fact]
    public void UnknownMember_IsIgnoredWithoutThrowing()
    {
        var offer = new Offer();
        SchemaPropertySetter.SetPropertyValue(offer, "Availability", "https://schema.org/Nope");
        JsonDocument.Parse(offer.ToString()).RootElement.TryGetProperty("availability", out _).Should().BeFalse();
    }
}
