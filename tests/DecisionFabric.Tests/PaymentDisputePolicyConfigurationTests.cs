using DecisionFabric.PaymentDisputes.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace DecisionFabric.Tests;

public sealed class PaymentDisputePolicyConfigurationTests
{
    private const string Section = PaymentDisputePolicyOptions.SectionName;

    [Theory]
    [InlineData("PositiveAtOrAbove", "0.1")]
    [InlineData("NegativeAtOrBelow", "-0.5")]
    [InlineData("MinimumIntentConfidence", "1.5")]
    [InlineData("PositiveAtOrAbove", "")]
    public void InvalidPolicyConfigurationPreventsStartup(string key, string value)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting($"{Section}:{key}", value));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(Flatten(exception), inner => inner is OptionsValidationException);
    }

    [Fact]
    public void PolicyVersionChangesWhenThresholdsChange()
    {
        var baseline = Pack(0.25, 0.75, 0.8);
        var same = Pack(0.25, 0.75, 0.8);
        var stricter = Pack(0.25, 0.8, 0.8);

        Assert.Equal(baseline.PolicyVersion, same.PolicyVersion);
        Assert.NotEqual(baseline.PolicyVersion, stricter.PolicyVersion);
        Assert.StartsWith("payment-dispute-gate/sha256:", baseline.PolicyVersion, StringComparison.Ordinal);
    }

    private static PaymentDisputePack Pack(double negative, double positive, double confidence) =>
        new(Options.Create(new PaymentDisputePolicyOptions
        {
            NegativeAtOrBelow = negative,
            PositiveAtOrAbove = positive,
            MinimumIntentConfidence = confidence
        }));

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }
}
