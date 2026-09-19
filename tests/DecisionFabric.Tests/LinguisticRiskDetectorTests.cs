using DecisionFabric.Policy;

namespace DecisionFabric.Tests;

public sealed class LinguisticRiskDetectorTests
{
    [Fact]
    public void DetectFlagsMultipleNegationsWithinOneSentence()
    {
        var signals = LinguisticRiskDetector.Detect("I don't want you not to block my card.");

        Assert.True(signals.HasFlag(LinguisticRiskSignal.MultipleNegations));
    }

    [Fact]
    public void DetectDoesNotMergeNegationsAcrossSentences()
    {
        var signals = LinguisticRiskDetector.Detect(
            "I didn't make this payment. Don't block the card yet.");

        Assert.False(signals.HasFlag(LinguisticRiskSignal.MultipleNegations));
    }

    [Fact]
    public void DetectFlagsSelfCorrectionAndHedging()
    {
        var signals = LinguisticRiskDetector.Detect(
            "Keep the card active—actually, maybe freeze it. I'm not sure.");

        Assert.True(signals.HasFlag(LinguisticRiskSignal.SelfCorrection));
        Assert.True(signals.HasFlag(LinguisticRiskSignal.Hedging));
    }

    [Fact]
    public void DetectLeavesClearPositiveInstructionUnflagged()
    {
        var signals = LinguisticRiskDetector.Detect(
            "Freeze this card immediately. I do not recognize the transaction.");

        Assert.Equal(LinguisticRiskSignal.None, signals);
    }
}
