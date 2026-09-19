using System.Text.RegularExpressions;

namespace DecisionFabric.Policy;

[Flags]
public enum LinguisticRiskSignal
{
    None = 0,
    MultipleNegations = 1,
    SelfCorrection = 2,
    Hedging = 4
}

public static partial class LinguisticRiskDetector
{
    public static LinguisticRiskSignal Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LinguisticRiskSignal.None;
        }

        var signals = LinguisticRiskSignal.None;
        if (SentenceBoundaryRegex()
            .Split(text)
            .Any(sentence => NegationTokenRegex().Count(sentence) >= 2))
        {
            signals |= LinguisticRiskSignal.MultipleNegations;
        }

        if (SelfCorrectionRegex().IsMatch(text))
        {
            signals |= LinguisticRiskSignal.SelfCorrection;
        }

        if (HedgingRegex().IsMatch(text))
        {
            signals |= LinguisticRiskSignal.Hedging;
        }

        return signals;
    }

    [GeneratedRegex(@"[.!?;]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(
        @"\b(?:no|not|never|cannot|can['’]t|don['’]t|doesn['’]t|didn['’]t|won['’]t|wouldn['’]t|shouldn['’]t|isn['’]t|aren['’]t|wasn['’]t|weren['’]t)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NegationTokenRegex();

    [GeneratedRegex(
        @"\b(?:actually|on second thought|rather|instead)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelfCorrectionRegex();

    [GeneratedRegex(
        @"\b(?:maybe|perhaps|possibly|not sure|uncertain)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HedgingRegex();
}
