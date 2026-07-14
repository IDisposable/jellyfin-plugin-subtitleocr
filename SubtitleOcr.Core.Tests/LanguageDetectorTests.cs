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
    public void Detect_ClearText_ReturnsLanguage(string expected, string text)
        => Assert.Equal(expected, LanguageDetector.Detect(text));

    [Fact]
    public void Detect_TooShort_ReturnsNull()
        => Assert.Null(LanguageDetector.Detect("the and you"));

    [Fact]
    public void Detect_Gibberish_ReturnsNull()
        => Assert.Null(LanguageDetector.Detect("xqz wvk *** ??? zzz qqq"));
}
