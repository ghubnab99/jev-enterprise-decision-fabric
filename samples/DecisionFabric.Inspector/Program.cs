using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DecisionFabric.Inspector;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddOptions<InspectorOptions>()
            .BindConfiguration(InspectorOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<InspectorOptions>, InspectorOptionsValidator>();
        builder.Services.AddSingleton(services => InspectionArchive.Load(
            services.GetRequiredService<IOptions<InspectorOptions>>().Value,
            services.GetRequiredService<IWebHostEnvironment>().ContentRootPath));

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Fail at startup rather than on the first request: a missing or stale artifact should
        // stop the inspector coming up at all.
        app.Services.GetRequiredService<InspectionArchive>();

        app.MapGet("/api/archive", (InspectionArchive archive) => Results.Ok(archive.View))
            .WithName("GetArchive");

        app.MapGet("/api/cases", (
                InspectionArchive archive,
                string? family,
                string? outcome,
                string? query) =>
            {
                var filtered = Filter(archive.Cases, family, outcome, query, out var problem);
                return problem is null ? Results.Ok(filtered) : Results.BadRequest(problem);
            })
            .WithName("GetCases");

        app.MapGet("/api/cases/{caseId}", (InspectionArchive archive, string caseId) =>
            {
                var detail = archive.Case(caseId);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .WithName("GetCase");

        app.Run();
    }

    /// <summary>
    /// Narrows the case list. <paramref name="outcome"/> is the interesting one: it answers
    /// "show me where this went wrong" without the reader having to scan 111 rows.
    /// </summary>
    internal static IReadOnlyList<CaseRowView> Filter(
        IReadOnlyList<CaseRowView> cases,
        string? family,
        string? outcome,
        string? query,
        out string? problem)
    {
        problem = null;
        IEnumerable<CaseRowView> filtered = cases;

        if (!string.IsNullOrWhiteSpace(family))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.Family, family, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(row =>
                row.CaseId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Instruction.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Tool.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = (outcome?.ToLowerInvariant()) switch
        {
            null or "" or "all" => filtered,
            "incorrect" => filtered.Where(row => row.Legs.Any(leg => !leg.Correct)),
            "disagreement" => filtered.Where(row => row.Legs
                .Select(leg => leg.MajorityDisposition)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1),
            // The set a McNemar test runs on: one leg right and another wrong. Narrower than
            // "disagreement", which also holds when both legs are wrong in different ways.
            "discordant" => filtered.Where(row =>
                row.Legs.Any(leg => leg.Correct) && row.Legs.Any(leg => !leg.Correct)),
            "unsafe-allow" => filtered.Where(row => row.Legs.Any(leg => leg.UnsafeAllow)),
            "over-block" => filtered.Where(row => row.Legs.Any(leg => leg.OverBlock)),
            "unstable" => filtered.Where(row => row.Legs.Any(leg => !leg.Stable)),
            "repeated" => filtered.Where(row => row.Legs.Any(leg => leg.Calls > 1)),
            _ => Invalid(out problem)
        };

        return problem is null ? [.. filtered] : [];

        static IEnumerable<CaseRowView> Invalid(out string? problem)
        {
            problem = "outcome must be one of all, incorrect, disagreement, discordant, " +
                "unsafe-allow, over-block, unstable, repeated.";
            return [];
        }
    }
}
