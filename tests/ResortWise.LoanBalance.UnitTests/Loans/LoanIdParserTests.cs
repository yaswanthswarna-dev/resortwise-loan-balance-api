using ResortWise.LoanBalance.Api.Loans;

namespace ResortWise.LoanBalance.UnitTests.Loans;

public sealed class LoanIdParserTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("007")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    [InlineData("12\n")]
    [InlineData("1.0")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("１２")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    public void Rejects_invalid_ids(string? raw)
    {
        Assert.False(LoanIdParser.TryParse(raw, out _));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100245", 100245)]
    [InlineData("2147483647", int.MaxValue)]
    public void Accepts_plain_digits_up_to_the_maximum(string raw, int expected)
    {
        Assert.True(LoanIdParser.TryParse(raw, out var loanId));
        Assert.Equal(expected, loanId);
    }
}
