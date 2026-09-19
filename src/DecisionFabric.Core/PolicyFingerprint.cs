using System.Security.Cryptography;
using System.Text.Json;

namespace DecisionFabric.Core;

public static class PolicyFingerprint
{
    /// <summary>
    /// Creates a stable identifier such as <c>payment-dispute-gate/sha256:1a2b3c4d5e6f</c>
    /// from a policy name and its effective parameters, so any parameter change
    /// produces a different version without relying on a manually bumped number.
    /// </summary>
    public static string Create<TParameters>(string policyName, TParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentNullException.ThrowIfNull(parameters);

        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parameters));
        return $"{policyName}/sha256:{Convert.ToHexStringLower(hash)[..12]}";
    }
}
