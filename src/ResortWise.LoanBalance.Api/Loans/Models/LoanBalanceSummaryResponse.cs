using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResortWise.LoanBalance.Api.Loans.Models;

/// <summary>The contract's <c>LoanBalanceSummary</c>: exactly these four fields (FR-001, FR-007).</summary>
public sealed record LoanBalanceSummaryResponse(
    [property: JsonPropertyName("loanId")] long LoanId,
    [property: JsonPropertyName("loanStatus")] string LoanStatus,
    [property: JsonPropertyName("asOfDate")] DateOnly AsOfDate,
    [property: JsonPropertyName("principalBalance"), JsonConverter(typeof(TwoPlaceAmountConverter))] decimal PrincipalBalance);

/// <summary>
/// Writes an amount with exactly two decimal places, including <c>0.00</c> (FR-006). The value
/// is already rounded by the view; an amount with more places is an error, never rounded here
/// (FR-003).
/// </summary>
public sealed class TwoPlaceAmountConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new InvalidOperationException("An amount has more than two decimal places.");
        }

        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
