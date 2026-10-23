namespace Rroms.Web.Services;

/// <summary>
/// The one rule for dish codes (S01, G02, P03 ...), shared by <see cref="MenuService"/> and the sample menu seeder.
/// </summary>
/// <remarks>
/// A code is the first letter of the dish's category plus a number. The number is the highest one already used
/// with that letter plus one, so categories that share a first letter share one numbering and a code is unique on
/// the whole menu. Written once here so the admin page and the seeder can never disagree.
/// </remarks>
public static class MenuCodeRule
{
    /// <summary>Digits in the number part: S01, S02 ... S10, S11 (it grows past 99 without breaking).</summary>
    public const int NumberMinDigits = 2;

    /// <summary>Letter used when a category name does not start with a letter.</summary>
    public const char FallbackLetter = 'X';

    /// <summary>Makes the next free code for a category.</summary>
    /// <param name="categoryName">The name of the dish's category.</param>
    /// <param name="existingCodes">Every code already used on the menu (null entries are ignored).</param>
    public static string Next(string categoryName, IEnumerable<string?> existingCodes)
    {
        char letter = FallbackLetter;
        string trimmedName = categoryName.Trim();

        if (trimmedName.Length > 0 && char.IsLetter(trimmedName[0]))
        {
            letter = char.ToUpperInvariant(trimmedName[0]);
        }

        string prefix = letter.ToString();
        int highest = 0;

        foreach (string? code in existingCodes)
        {
            if (code == null || !code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string numberPart = code.Substring(prefix.Length);

            if (int.TryParse(numberPart, out int number) && number > highest)
            {
                highest = number;
            }
        }

        string next = (highest + 1).ToString().PadLeft(NumberMinDigits, '0');

        return prefix + next;
    }
}
