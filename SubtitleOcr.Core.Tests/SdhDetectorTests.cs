using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class SdhDetectorTests
{
    // Proportions taken from a real pair of untagged PGS tracks on the same disc: the SDH track ran
    // 223 sound cues in 1215, the dialogue track 0 in 1021.
    [Fact]
    public void IsHearingImpaired_SoundCueDensity_DetectsSdhTrack()
    {
        var texts = Build(total: 1215, withSoundCue: 223);

        Assert.True(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    [Fact]
    public void IsHearingImpaired_PlainDialogue_IsNotSdh()
    {
        var texts = Build(total: 1021, withSoundCue: 0);

        Assert.False(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    // An ordinary track still uses the odd parenthesis; that must not tip it over.
    [Fact]
    public void IsHearingImpaired_OccasionalParenthetical_IsNotSdh()
    {
        var texts = Build(total: 1000, withSoundCue: 10);

        Assert.False(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    // A dialogue track that leans on inline parentheticals is not SDH: the bracket is mid-line, not opening it.
    [Fact]
    public void IsHearingImpaired_InlineParentheticals_IsNotSdh()
    {
        var texts = new List<string>();
        for (var i = 0; i < 200; i++)
        {
            texts.Add("Come here (John), would you?");
        }

        Assert.False(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    // Detection reads content, not markup: an italic-wrapped sound cue still counts.
    [Fact]
    public void IsHearingImpaired_ItalicWrappedSoundCue_Counts()
    {
        var texts = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            texts.Add("<i>[radio chatter]</i>");
        }

        Assert.True(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    [Theory]
    [InlineData("[engine roaring and stuttering]")]
    [InlineData("(EXPLOSIONS)")]
    [InlineData("[en*one ro*ri** and S*uttering**")] // OCR dropped the closing bracket
    [InlineData("MAN: [shouting] Go!")]
    public void IsHearingImpaired_RecognizesSoundCueForms(string soundCue)
    {
        var texts = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            texts.Add(soundCue);
        }

        Assert.True(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    // A short track's ratio is noise, so it is left alone.
    [Fact]
    public void IsHearingImpaired_TooFewCues_IsNotSdh()
    {
        var texts = Build(total: 10, withSoundCue: 10);

        Assert.False(SdhDetector.IsHearingImpaired(texts, SdhDetector.DefaultRatio, SdhDetector.DefaultMinimumCues));
    }

    private static List<string> Build(int total, int withSoundCue)
    {
        var texts = new List<string>(total);
        for (var i = 0; i < total; i++)
        {
            texts.Add(i < withSoundCue ? "[door slams]" : "I'm looking for Commodore Jensen.");
        }

        return texts;
    }
}
