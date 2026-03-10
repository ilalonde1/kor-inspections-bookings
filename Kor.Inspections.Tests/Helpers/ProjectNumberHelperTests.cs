using Kor.Inspections.App.Services;

namespace Kor.Inspections.Tests.Helpers;

public class ProjectNumberHelperTests
{
    [Fact]
    public void Base5_ProjectNumberWithSuffix_ReturnsLeadingFiveDigits()
    {
        var result = ProjectNumberHelper.Base5("30844-01");

        Assert.Equal("30844", result);
    }

    [Fact]
    public void Base5_FiveDigitsOnly_ReturnsFiveDigits()
    {
        var result = ProjectNumberHelper.Base5("30844");

        Assert.Equal("30844", result);
    }

    [Fact]
    public void Base5_NullInput_ReturnsEmptyString()
    {
        var result = ProjectNumberHelper.Base5(null);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Base5_AnotherProjectNumberWithSuffix_ReturnsLeadingFiveDigits()
    {
        var result = ProjectNumberHelper.Base5("12345-99");

        Assert.Equal("12345", result);
    }
}
