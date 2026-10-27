using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using ResortWise.LoanBalance.Api.Errors;
using ResortWise.LoanBalance.Api.Loans;
using ResortWise.LoanBalance.Api.Loans.Models;
using ResortWise.LoanBalance.UnitTests.Infrastructure;

namespace ResortWise.LoanBalance.UnitTests.Contract;

/// <summary>The app, the error codes, and the response model match the OpenAPI contract (Principles I, VI).</summary>
public sealed class ContractConsistencyTests
{
    private static readonly Lazy<OpenApiDocument> Document = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Contract", "loan-balance-summary.openapi.yaml"));
        var document = new OpenApiStreamReader().Read(stream, out var diagnostic);
        Assert.Empty(diagnostic.Errors);
        return document;
    });

    [Fact]
    public void App_routes_equal_contract_paths()
    {
        var serverPrefix = Assert.Single(Document.Value.Servers).Url.TrimEnd('/');
        var contractRoutes = Document.Value.Paths.Keys.Select(path => serverPrefix + path).Order(StringComparer.Ordinal);

        using var app = new TestApp();
        var appRoutes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(contractRoutes, appRoutes);
    }

    [Fact]
    public void Error_code_enum_equals_the_error_code_constants()
    {
        var codes = Document.Value.Components.Schemas["Error"].Properties["code"].Enum
            .Select(value => ((OpenApiString)value).Value);

        Assert.Equal(ErrorCodes.All.Order(StringComparer.Ordinal), codes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Response_model_matches_the_contract_schema()
    {
        string[] required = ["asOfDate", "loanId", "loanStatus", "principalBalance", "principalBalanceAvailable"];
        string[] all = [.. required, "principalUnavailableReason"];
        var schema = Document.Value.Components.Schemas["LoanBalanceSummary"];

        var modelFields = typeof(LoanBalanceSummaryResponse).GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(required, schema.Required.Order(StringComparer.Ordinal));
        Assert.Equal(all.Order(StringComparer.Ordinal), schema.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(all.Order(StringComparer.Ordinal), modelFields);
    }

    // OpenAPI 3.0.3 applies nullable only with a type in the same schema object, so null must be
    // allowed there, not through a $ref or allOf (O2; analyze D1).
    [Fact]
    public void Principal_balance_is_a_nullable_number_in_its_own_schema()
    {
        var principal = Document.Value.Components.Schemas["LoanBalanceSummary"].Properties["principalBalance"];

        Assert.Equal("number", principal.Type);
        Assert.True(principal.Nullable);
        Assert.Null(principal.Reference);
        Assert.Empty(principal.AllOf);
    }

    [Fact]
    public void Unavailable_reason_enum_equals_the_reason_constants()
    {
        var reasons = Document.Value.Components.Schemas["LoanBalanceSummary"].Properties["principalUnavailableReason"].Enum
            .Select(value => ((OpenApiString)value).Value);

        Assert.Equal(PrincipalUnavailableReasons.All.Order(StringComparer.Ordinal), reasons.Order(StringComparer.Ordinal));
    }

    // O2 and O5: exactly these three reasons, so neither side can add or drop one unnoticed.
    [Fact]
    public void Unavailable_reasons_are_exactly_the_o2_and_o5_codes()
    {
        var reasons = Document.Value.Components.Schemas["LoanBalanceSummary"].Properties["principalUnavailableReason"].Enum
            .Select(value => ((OpenApiString)value).Value);

        string[] expected = ["CROSS_LOAN_REVERSAL", "LOAN_NOT_ACTIVE", "PAID_OFF_FLAG_SET"];
        Assert.Equal(expected, reasons.Order(StringComparer.Ordinal));
        Assert.Equal(expected, PrincipalUnavailableReasons.All.Order(StringComparer.Ordinal));
    }
}
