using System.IO;
using SubtitleOcr.Core.Ocr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class OcrFixReplaceListTests
{
    private const string Xml =
        "<OCRFixReplaceList>" +
        "  <WholeWords>" +
        "    <Word from=\"0f\" to=\"of\" />" +
        "    <Word from=\"/got\" to=\"I got\" />" +
        "  </WholeWords>" +
        "  <PartialWords>" +
        "    <WordPart from=\"rn\" to=\"m\" />" +  // parsed but intentionally not applied (unsafe)
        "  </PartialWords>" +
        "  <RegularExpressions>" +
        "    <RegularExpression find=\"\\bvv\\b\" replaceWith=\"w\" />" +
        "  </RegularExpressions>" +
        "</OCRFixReplaceList>";

    private static OcrFixReplaceList Load()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, Xml);
        try
        {
            return OcrFixReplaceList.LoadFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0f", "of")]                 // whole-word token
    [InlineData("0f.", "of.")]               // whole word with trailing punctuation
    [InlineData("/got", "I got")]            // token expanding to two words
    [InlineData("burn corner", "burn corner")] // partial "rn"->"m" is NOT applied (would corrupt real words)
    [InlineData("vv", "w")]                  // regex
    [InlineData("nothing here", "nothing here")]
    public void Apply_FixesKnownPatterns(string input, string expected)
        => Assert.Equal(expected, Load().Apply(input));

    [Fact]
    public void Empty_IsNoOp()
        => Assert.Equal("unchanged 0f", OcrFixReplaceList.Empty.Apply("unchanged 0f"));
}
