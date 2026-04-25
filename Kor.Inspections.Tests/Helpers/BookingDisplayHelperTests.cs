using Kor.Inspections.App.Services;

namespace Kor.Inspections.Tests.Helpers;

public class BookingDisplayHelperTests
{
    [Fact]
    public void FormatJobLine_WhenDisplayNumberAndNamePresent_ReturnsDisplayNumberAndName()
    {
        var result = BookingDisplayHelper.FormatJobLine("30844-001", "Acme Tower", "30844");

        Assert.Equal("30844-001 Acme Tower", result);
    }

    [Fact]
    public void FormatJobLine_WhenDisplayNumberPresentAndNameMissing_ReturnsDisplayNumberOnly()
    {
        var result = BookingDisplayHelper.FormatJobLine("30844-001", "   ", "30844");

        Assert.Equal("30844-001", result);
    }

    [Fact]
    public void FormatJobLine_WhenDisplayNumberMissingAndNamePresent_ReturnsFallbackAndName()
    {
        var result = BookingDisplayHelper.FormatJobLine(null, "Acme Tower", "30844");

        Assert.Equal("30844 Acme Tower", result);
    }

    [Fact]
    public void FormatJobLine_WhenDisplayNumberAndNameMissing_ReturnsFallback()
    {
        var result = BookingDisplayHelper.FormatJobLine("   ", null, "30844");

        Assert.Equal("30844", result);
    }
}
