using System.Text.RegularExpressions;

public static class PhoneNormalizer
{
    public static string Normalize(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = Regex.Replace(phone, @"\D", "");

        // Handle leading 1 (North America)
        if (digits.Length == 11 && digits.StartsWith("1"))
            digits = digits[1..];

        return digits;
    }

    public static string Format(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = Normalize(phone);

        if (digits.Length != 10)
            return phone; // don't corrupt unexpected formats

        return $"({digits[..3]})-{digits[3..6]}-{digits[6..]}";
    }
}
