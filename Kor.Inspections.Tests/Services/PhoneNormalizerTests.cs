namespace Kor.Inspections.Tests.Services;

public class PhoneNormalizerTests
{
    [Fact]
    public void Format_NullInput_ReturnsEmptyString()
    {
        var result = PhoneNormalizer.Format(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_EmptyString_ReturnsEmptyString()
    {
        var result = PhoneNormalizer.Format(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_StandardTenDigitNumber_ReturnsFormattedPhone()
    {
        var result = PhoneNormalizer.Format("6045551234");

        Assert.Equal("(604)-555-1234", result);
    }

    [Fact]
    public void Format_ElevenDigitNumberWithLeadingOne_ReturnsFormattedPhone()
    {
        var result = PhoneNormalizer.Format("16045551234");

        Assert.Equal("(604)-555-1234", result);
    }

    [Fact]
    public void Format_NonNumericString_ReturnsOriginalInput()
    {
        var result = PhoneNormalizer.Format("hello");

        Assert.Equal("hello", result);
    }
}
