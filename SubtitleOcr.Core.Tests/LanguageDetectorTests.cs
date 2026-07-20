using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("en", "the boy and the girl were with you that day and they have what you want not his")]
    [InlineData("de", "und der die das ist nicht ein ich sie mit auch sich wir was für den")]
    [InlineData("fr", "les est pas vous une que qui dans pour je nous avec mais ce sur")]
    [InlineData("cs", "se na že je ne co jak ale tak jsem prosím první den tady ještě")]
    // Danish and Norwegian share most of their function words; the distinctive ones must still separate them.
    [InlineData("da", "og er jeg ikke det til har som den med dig mig hvad skal være")]
    [InlineData("no", "og er jeg ikke det til har som den med deg meg hva være ikkje")]
    public void Detect_ClearText_ReturnsLanguage(string expected, string text)
        => Assert.Equal(expected, LanguageDetector.Detect(text));

    // The italic markup carried in a cue is presentation; detection reads the words, not the tags.
    [Fact]
    public void Detect_IgnoresItalicMarkup()
        => Assert.Equal("en", LanguageDetector.Detect("<i>the boy</i> and the girl were with you that day and they have what you want not his"));

    [Fact]
    public void Detect_TooShort_ReturnsNull()
        => Assert.Null(LanguageDetector.Detect("the and you"));

    [Fact]
    public void Detect_Gibberish_ReturnsNull()
        => Assert.Null(LanguageDetector.Detect("xqz wvk *** ??? zzz qqq"));
}
