using System.Text.Json.Serialization;

namespace ResortWise.LoanBalance.Api.Errors;

/// <summary>The contract's <c>Error</c> schema. <c>field</c> is omitted when null.</summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("field"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);
