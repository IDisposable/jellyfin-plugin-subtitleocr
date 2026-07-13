using SubtitleOcr.Core.NOcr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class NOcrDbTests
{
    [Fact]
    public void LoadEmbeddedLatin_LoadsAll690Glyphs()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        Assert.Equal(690, db.TotalCharacterCount);
    }

    [Fact]
    public void LoadEmbeddedLatin_Has19ExpandedEntries()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        Assert.Equal(19, db.OcrCharactersExpanded.Count);
    }

    [Fact]
    public void LoadEmbeddedLatin_ContainsItalicGlyphs()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        Assert.Contains(db.OcrCharacters, c => c.Italic);
    }

    [Fact]
    public void SaveLoad_RoundTripsGlyphCountAndContent()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        var tmp = Path.Combine(Path.GetTempPath(), $"roundtrip-{Guid.NewGuid():N}.nocr");
        try
        {
            db.Save(tmp);
            var reloaded = NOcrDb.LoadFile(tmp);

            Assert.Equal(db.TotalCharacterCount, reloaded.TotalCharacterCount);

            var original = db.OcrCharacters[0];
            var loaded = reloaded.OcrCharacters[0];
            Assert.Equal(original.Text, loaded.Text);
            Assert.Equal(original.Width, loaded.Width);
            Assert.Equal(original.LinesForeground.Count, loaded.LinesForeground.Count);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // Rasterizing each glyph's own foreground and matching it back at a zero error budget
    // must recover the glyph at least 95% of the time. Guards the matcher and the DB parse together.
    [Fact]
    public void Matcher_SelfRecognizesAtLeast95Percent()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        var tested = 0;
        var correct = 0;

        foreach (var glyph in db.OcrCharacters)
        {
            if (glyph.Width < 5 || glyph.Height < 5 || glyph.LinesForeground.Count < 3)
            {
                continue;
            }

            var bmp = TestFixtures.RasterizeForeground(glyph);
            var match = db.GetMatch(bmp, glyph.MarginTop, deepSeek: false, maxWrongPixels: 0);
            tested++;
            if (match?.Text == glyph.Text)
            {
                correct++;
            }
        }

        Assert.True(tested > 300, $"expected a meaningful sample, tested only {tested}");
        Assert.True(correct >= tested * 95 / 100, $"self-recognition {correct}/{tested} below 95%");
    }
}
