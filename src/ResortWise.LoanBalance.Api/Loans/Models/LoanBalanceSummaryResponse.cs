using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResortWise.LoanBalance.Api.Loans.Models;

/// <summary>
/// The contract's <c>LoanBalanceSummary</c> (FR-002, FR-007). <c>principalBalance</c> is always
/// written, as <c>null</c> when not available; <c>principalUnavailableReason</c> is written only
/// when the principal is not available (FR-008a, O2).
/// </summary>
public sealed record LoanBalanceSummaryResponse(
    [property: JsonPropertyName("loanId")] long LoanId,
    [property: JsonPropertyName("loanStatus")] string LoanStatus,
    [property: JsonPropertyName("asOfDate")] DateOnly AsOfDate,
    [property: JsonPropertyName("principalBalance"), JsonConverter(typeof(TwoPlaceAmountConverter))] decimal? PrincipalBalance,
    [property: JsonPropertyName("principalBalanceAvailable")] bool PrincipalBalanceAvailable,
    [property: JsonPropertyName("principalUnavailableReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrincipalUnavailableReason);

/// <summary>
/// Writes an amount with exactly two decimal places, including <c>0.00</c> (FR-006), and a missing
/// amount as <c>null</c>, never <c>0</c> (FR-008a). The value is already rounded by the view; an
/// amount with more places is an error, never rounded here (FR-003).
/// </summary>
public sealed class TwoPlaceAmountConverter : JsonConverter<decimal?>
{
    public override bool HandleNull => true;

    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is not { } amount)
        {
            writer.WriteNullValue();
            return;
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new InvalidOperationException("An amount has more than two decimal places.");
        }

        writer.WriteRawValue(amount.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
