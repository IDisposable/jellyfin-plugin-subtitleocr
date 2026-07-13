using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class LanguageCodesTests
{
    [Theory]
    [InlineData("gre", "ell")]   // 639-2/B -> /T (Greek)
    [InlineData("ger", "deu")]   // 639-2/B -> /T (German)
    [InlineData("el", "ell")]    // 639-1 -> /T
    [InlineData("ru", "rus")]    // 639-1 -> /T
    [InlineData("ELL", "ell")]   // case fold
    [InlineData(" rus ", "rus")] // trim
    [InlineData("rus", "rus")]   // already canonical
    [InlineData("xyz", "xyz")]   // unknown passes through
    public void Normalize_CanonicalizesToTerminological(string input, string expected)
    {
        Assert.Equal(expected, LanguageCodes.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyBecomesUndetermined(string? input)
    {
        Assert.Equal(LanguageCodes.Undetermined, LanguageCodes.Normalize(input));
    }

    [Theory]
    [InlineData("eng", true)]
    [InlineData("fra", true)]
    [InlineData("und", true)]    // unknown defaults to Latin (preserves bundled-database behavior)
    [InlineData("ell", false)]   // Greek
    [InlineData("gre", false)]   // Greek via /B code, still non-Latin after normalization
    [InlineData("rus", false)]   // Cyrillic
    [InlineData("ru", false)]    // Cyrillic via 639-1
    public void IsLatinScript_ClassifiesByNormalizedCode(string code, bool expected)
    {
        Assert.Equal(expected, LanguageCodes.IsLatinScript(code));
    }
}
