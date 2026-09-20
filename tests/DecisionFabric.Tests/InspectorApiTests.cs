using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DecisionFabric.Inspector;
using Microsoft.AspNetCore.Mvc.Testing;
using InspectorProgram = DecisionFabric.Inspector.Program;

namespace DecisionFabric.Tests;

[Collection(SampleApps.Name)]
public sealed class InspectorApiTests : IClassFixture<WebApplicationFactory<InspectorProgram>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<InspectorProgram> _factory;

    public InspectorApiTests(WebApplicationFactory<InspectorProgram> factory) => _factory = factory;

    [Fact]
    public async Task ArchiveDescribesBothLegsAndTheGateBoundaries()
    {
        var client = _factory.CreateClient();

        var archive = await client.GetFromJsonAsync<ArchiveView>("/api/archive", JsonOptions);

        Assert.NotNull(archive);
        Assert.Equal("agent-action-gate-v1", archive.SuiteId);
        Assert.Equal(["jev", "claude"], archive.Legs.Select(leg => leg.Id));
        Assert.Equal(15, archive.Families.Count);
        Assert.Equal(0.25, archive.Policy.NegativeAtOrBelow);
        Assert.Equal(0.8, archive.Policy.MinimumImpactConfidence);
        Assert.NotEmpty(archive.AnnotationRules);
    }

    [Fact]
    public async Task CaseListReturnsEveryLabelledCase()
    {
        var client = _factory.CreateClient();

        var cases = await client.GetFromJsonAsync<IReadOnlyList<CaseRowView>>("/api/cases", JsonOptions);

        Assert.NotNull(cases);
        Assert.Equal(111, cases.Count);
    }

    [Theory]
    [InlineData("discordant", 10)]
    [InlineData("disagreement", 11)]
    // Each leg has one unsafe allow, on a different case; each has one over-block, and both
    // fall on imp-necessary-step-reversible, so that filter returns a single row.
    [InlineData("unsafe-allow", 2)]
    [InlineData("over-block", 1)]
    [InlineData("unstable", 0)]
    public async Task OutcomeFilterNarrowsToTheCasesTheWriteUpDiscusses(string outcome, int expected)
    {
        var client = _factory.CreateClient();

        var cases = await client.GetFromJsonAsync<IReadOnlyList<CaseRowView>>(
            $"/api/cases?outcome={outcome}", JsonOptions);

        Assert.NotNull(cases);
        Assert.Equal(expected, cases.Count);
    }

    [Fact]
    public async Task FamilyAndQueryFiltersCombine()
    {
        var client = _factory.CreateClient();

        var family = await client.GetFromJsonAsync<IReadOnlyList<CaseRowView>>(
            "/api/cases?family=negation", JsonOptions);
        Assert.NotNull(family);
        Assert.Equal(10, family.Count);
        Assert.All(family, row => Assert.Equal("negation", row.Family));

        var search = await client.GetFromJsonAsync<IReadOnlyList<CaseRowView>>(
            "/api/cases?query=revoke", JsonOptions);
        Assert.NotNull(search);
        Assert.Contains(search, row => row.CaseId == "irr-req-revoke-access");
    }

    [Fact]
    public async Task AnUnknownOutcomeIsRejectedRatherThanIgnored()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/cases?outcome=whatever", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("discordant", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaseDetailCarriesTheThreeLayersForEveryCall()
    {
        var client = _factory.CreateClient();

        var detail = await client.GetFromJsonAsync<CaseDetailView>(
            "/api/cases/scope-grant-extra-permission", JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal("RequireApproval", detail.ExpectedDisposition);
        Assert.Equal(2, detail.Legs.Count);
        Assert.All(detail.Legs, leg => Assert.All(leg.Calls, call =>
        {
            Assert.NotEmpty(call.Answers);
            Assert.NotEmpty(call.Gate.Reasons);
            Assert.False(string.IsNullOrWhiteSpace(call.Disposition));
        }));
    }

    [Fact]
    public async Task AnUnknownCaseIsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/cases/no-such-case", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheDashboardItselfIsServed()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Decision Inspector", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
