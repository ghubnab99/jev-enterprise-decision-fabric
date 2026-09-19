namespace DecisionFabric.Core;

/// <summary>
/// Provider answers that have been validated against a <see cref="DecisionContract"/>:
/// every question has an answer of the matching type, probabilities are in range and
/// every choice is one of the contract's declared options.
/// </summary>
public sealed class DecisionEvidence
{
    private DecisionEvidence(
        DecisionContract contract,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        Contract = contract;
        Answers = answers;
    }

    public DecisionContract Contract { get; }

    public IReadOnlyDictionary<string, DecisionAnswer> Answers { get; }

    public NoulAnswer Noul(string questionId) => Get<NoulAnswer>(questionId);

    public ChoiceAnswer Choice(string questionId) => Get<ChoiceAnswer>(questionId);

    public ScoreAnswer Score(string questionId) => Get<ScoreAnswer>(questionId);

    public static DecisionEvidence Validate(
        DecisionContract contract,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(answers);

        var validated = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var (questionId, question) in contract.Questions)
        {
            if (!answers.TryGetValue(questionId, out var answer) || answer is null)
            {
                throw Violation(contract, questionId, "no answer was returned");
            }

            switch (question, answer)
            {
                case (NoulQuestion, NoulAnswer noul):
                    RequireProbability(contract, questionId, "Noul", noul.Noul);
                    break;
                case (ChoiceQuestion choiceQuestion, ChoiceAnswer choice):
                    if (!choiceQuestion.Criteria.ContainsKey(choice.Choice))
                    {
                        throw Violation(
                            contract,
                            questionId,
                            $"choice '{choice.Choice}' is not a declared option");
                    }

                    RequireProbability(contract, questionId, "confidence", choice.Confidence);
                    break;
                case (ScoreQuestion, ScoreAnswer score):
                    RequireProbability(contract, questionId, "confidence", score.Confidence);
                    break;
                default:
                    throw Violation(
                        contract,
                        questionId,
                        $"expected an answer for {question.GetType().Name} but received {answer.GetType().Name}");
            }

            validated[questionId] = answer;
        }

        return new DecisionEvidence(contract, validated);
    }

    private TAnswer Get<TAnswer>(string questionId)
        where TAnswer : DecisionAnswer
    {
        if (!Answers.TryGetValue(questionId, out var answer))
        {
            throw new KeyNotFoundException(
                $"Contract '{Contract.Id}' does not declare question '{questionId}'.");
        }

        return answer as TAnswer ?? throw new InvalidCastException(
            $"Question '{questionId}' in contract '{Contract.Id}' is answered by " +
            $"{answer.GetType().Name}, not {typeof(TAnswer).Name}.");
    }

    private static void RequireProbability(
        DecisionContract contract,
        string questionId,
        string name,
        double value)
    {
        if (double.IsNaN(value) || value is < 0 or > 1)
        {
            throw Violation(contract, questionId, $"{name} {value} is outside [0, 1]");
        }
    }

    private static DecisionContractViolationException Violation(
        DecisionContract contract,
        string questionId,
        string detail) =>
        new(contract.Id, contract.Version, questionId, detail);
}

public sealed class DecisionContractViolationException : Exception
{
    public DecisionContractViolationException(
        string contractId,
        string contractVersion,
        string questionId,
        string detail)
        : base($"Answer for '{questionId}' violates contract '{contractId}' {contractVersion}: {detail}.")
    {
        ContractId = contractId;
        ContractVersion = contractVersion;
        QuestionId = questionId;
    }

    public string ContractId { get; }

    public string ContractVersion { get; }

    public string QuestionId { get; }
}
