using DecisionFabric.Core;
using DecisionFabric.TypeSafe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DecisionFabric.Hosting;

public static class DecisionFabricServiceCollectionExtensions
{
    public const string SectionName = "DecisionFabric";

    /// <summary>
    /// Registers <see cref="IDecisionFabric"/> with the provider selected by
    /// <c>DecisionFabric:Provider</c>: <c>Fixture</c> (default, uses registered
    /// <see cref="DecisionFixtureSet"/> instances) or <c>TypeSafe</c> (requires
    /// <c>DecisionFabric:TypeSafeApiKey</c> or <c>TYPESAFE_API_KEY</c>).
    /// </summary>
    public static IServiceCollection AddDecisionFabric(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var model = section["Model"];
        var mode = section["Provider"] ?? "Fixture";

        if (string.Equals(mode, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDecisionProvider, FixtureDecisionProvider>();
        }
        else if (string.Equals(mode, "TypeSafe", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = section["TypeSafeApiKey"] ??
                Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "TypeSafe provider mode requires DecisionFabric:TypeSafeApiKey or TYPESAFE_API_KEY.");
            }

            services.AddHttpClient("typesafe");
            services.AddSingleton<IDecisionProvider>(provider =>
                new TypeSafeDecisionProvider(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("typesafe"),
                    apiKey,
                    new TypeSafeClientOptions { Model = model ?? "jev-1.13.0" }));
        }
        else
        {
            throw new InvalidOperationException(
                "DecisionFabric:Provider must be either 'Fixture' or 'TypeSafe'.");
        }

        services.AddSingleton(new DecisionFabricOptions { Model = model });
        services.AddSingleton<IDecisionFabric, DefaultDecisionFabric>();
        return services;
    }
}
