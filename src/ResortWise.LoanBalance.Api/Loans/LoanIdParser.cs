using System.Globalization;
using System.Text.RegularExpressions;

namespace ResortWise.LoanBalance.Api.Loans;

/// <summary>
/// Accepts a loan ID only as plain digits with no sign, leading zero, decimal point, or
/// whitespace, within the SPI loan record ID type (<c>int</c>; research R8). No normalization
/// (FR-016).
/// </summary>
public static partial class LoanIdParser
{
    public const int Maximum = int.MaxValue;

    public static bool TryParse(string? raw, out int loanId)
    {
        loanId = 0;
        return raw is not null
            && PlainDigits().IsMatch(raw)
            && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out loanId);
    }

    // \z, not $, so a trailing newline is rejected.
    [GeneratedRegex(@"\A[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlainDigits();
}
