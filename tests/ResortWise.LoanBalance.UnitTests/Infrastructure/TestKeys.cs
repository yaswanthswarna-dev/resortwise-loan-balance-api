using System.Security.Cryptography;
using System.Text;

namespace ResortWise.LoanBalance.UnitTests.Infrastructure;

/// <summary>Generates API keys and their hashes per test run, as in quickstart step 3. Never hard-coded.</summary>
public static class TestKeys
{
    public sealed record ApiKey(string Key, string Hash);

    public static ApiKey Create()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new ApiKey(key, Hash(key));
    }

    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
