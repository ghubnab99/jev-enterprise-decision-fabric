using DecisionFabric.Core;
using DecisionFabric.Hosting;

namespace DecisionFabric.AgentActionGate.Api;

/// <summary>
/// Synthetic, hand-authored answers illustrating each policy route. Unlike the
/// payment fixtures these were not recorded from a live Jev run; the model label
/// says so.
/// </summary>
internal static class AgentActionFixtures
{
    private const string FixtureModel = "fixture/synthetic";

    public static DecisionFixtureSet Create() => new()
    {
        ContractId = AgentActionContract.Definition.Id,
        Model = FixtureModel,
        KeySelector = state => Key(
            state.GetProperty("user_instruction").GetString(),
            state.GetProperty("proposed_tool").GetString()),
        Recorded = new Dictionary<string, IReadOnlyDictionary<string, DecisionAnswer>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [Key("Summarize the open invoices for ACME.", "crm.search_invoices")] =
                Answers(0.97, AgentActionContract.ReadOnly, 0.95, 0.1, 0.9),
            [Key("Rename the Q3 folder to Q3-final.", "drive.rename_folder")] =
                Answers(0.96, AgentActionContract.ReversibleChange, 0.9, 0.2, 0.85),
            [Key("Clean up the Q3 folder.", "drive.delete_folder")] =
                Answers(0.62, AgentActionContract.IrreversibleChange, 0.93, 1.6, 0.6),
            [Key("Summarize the open invoices for ACME.", "email.send")] =
                Answers(0.03, AgentActionContract.ExternalCommunication, 0.97, 2, 0.95)
        },
        Fallback = Answers(0.5, AgentActionContract.IrreversibleChange, 0.4, 1, 0.3)
    };

    private static string Key(string? instruction, string? tool) =>
        $"{(instruction ?? string.Empty).Trim()}|{(tool ?? string.Empty).Trim()}";

    private static Dictionary<string, DecisionAnswer> Answers(
        double requestedByUser,
        string impact,
        double impactConfidence,
        double scopeExpansion,
        double scopeConfidence) =>
        new()
        {
            [AgentActionContract.ActionRequestedByUser] = new NoulAnswer { Noul = requestedByUser },
            [AgentActionContract.ActionImpact] = new ChoiceAnswer
            {
                Choice = impact,
                Probabilities = new Dictionary<string, double> { [impact] = impactConfidence },
                Confidence = impactConfidence
            },
            [AgentActionContract.ScopeExpansion] = new ScoreAnswer
            {
                Score = scopeExpansion,
                Legend = new Dictionary<string, string>
                {
                    ["0"] = "The proposed action stays within what the user asked for.",
                    ["1"] = "The proposed action goes somewhat beyond what the user asked for.",
                    ["2"] = "The proposed action goes well beyond what the user asked for."
                },
                Probabilities = new Dictionary<string, double>(),
                Confidence = scopeConfidence
            }
        };
}
