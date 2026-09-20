using DecisionFabric.PaymentDisputes.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DecisionFabric.Tests;

public sealed class PaymentDisputePolicyConfigurationTests
{
    private const string Section = PaymentDisputePolicyOptions.SectionName;

    /// <summary>
    /// Which exception surfaces from a host that fails to start is a race: when the entry point
    /// throws, the host it built is disposed while the test factory is still reaching into it, so
    /// the validation failure is sometimes replaced by an ObjectDisposedException for that host.
    /// The guarantee worth asserting here is that the app does not come up. That the reason is a
    /// validation failure is pinned deterministically by
    /// <see cref="InvalidPolicyConfigurationFailsValidation"/>.
    /// </summary>
    [Theory]
    [InlineData("PositiveAtOrAbove", "0.1")]
    [InlineData("NegativeAtOrBelow", "-0.5")]
    [InlineData("MinimumIntentConfidence", "1.5")]
    [InlineData("PositiveAtOrAbove", "")]
    public void InvalidPolicyConfigurationPreventsStartup(string key, string value)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting($"{Section}:{key}", value));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("PositiveAtOrAbove", "0.1")]
    [InlineData("NegativeAtOrBelow", "-0.5")]
    [InlineData("MinimumIntentConfidence", "1.5")]
    [InlineData("PositiveAtOrAbove", "")]
    public void InvalidPolicyConfigurationFailsValidation(string key, string value)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:NegativeAtOrBelow"] = "0.25",
            [$"{Section}:PositiveAtOrAbove"] = "0.75",
            [$"{Section}:MinimumIntentConfidence"] = "0.8",
            [$"{Section}:{key}"] = value
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<PaymentDisputePolicyOptions>, PaymentDisputePolicyOptionsValidator>();
        services.AddOptions<PaymentDisputePolicyOptions>().Bind(configuration.GetSection(Section));
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<PaymentDisputePolicyOptions>>().Value);

        // The messages describe the broken relationship ("the negative threshold must be lower
        // than the positive threshold") rather than naming a property, so assert only that the
        // validator spoke.
        Assert.NotEmpty(error.Failures);
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
}
