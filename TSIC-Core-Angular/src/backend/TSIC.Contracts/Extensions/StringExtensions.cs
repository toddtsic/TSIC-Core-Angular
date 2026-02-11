namespace TSIC.Contracts.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Formats a phone number string as xxx-xxx-xxxx when the digits-only
    /// length is exactly 10.  Returns the original value otherwise.
    /// </summary>
    public static string? FormatPhone(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        // Strip to digits only
        var digits = new string(value.Where(char.IsDigit).ToArray());

        if (digits.Length == 10)
            return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";

        return value;
    }
}
