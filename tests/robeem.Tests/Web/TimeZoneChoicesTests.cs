using robeem.Web.Models;

namespace robeem.Tests.Web;

/// <summary>The time zone drop-down of the integration form: every zone it offers is one the Api accepts, and a saved value is never lost.</summary>
public sealed class TimeZoneChoicesTests
{
    [Fact]
    public void EveryZoneOffered_IsOneTheApiAccepts_AndNoneIsListedTwice()
    {
        var unknown = TimeZoneOptions.Common.Where(z => !TimeZoneInfo.TryFindSystemTimeZoneById(z, out _)).ToList();

        Assert.Empty(unknown);
        Assert.Equal(TimeZoneOptions.Common.Count, TimeZoneOptions.Common.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ASavedZoneOutsideTheList_IsStillOffered_FirstInTheList()
    {
        var choices = TimeZoneOptions.Choices("Pacific/Chatham");

        Assert.Equal("Pacific/Chatham", choices[0]);
        Assert.Equal(TimeZoneOptions.Common.Count + 1, choices.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Asia/Beirut")]
    public void NoZone_OrOneInTheList_AddsNothing(string? current) =>
        Assert.Equal(TimeZoneOptions.Common, TimeZoneOptions.Choices(current));
}
