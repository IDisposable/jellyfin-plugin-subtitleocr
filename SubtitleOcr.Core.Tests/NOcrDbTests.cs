using SubtitleOcr.Core.NOcr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class NOcrDbTests
{
    // The bundled database is retrained over its lifetime, so assert it loads a plausible set rather than an
    // exact count that a retrain would break. Byte-exact parse coverage is checked by the round-trip test.
    [Fact]
    public void LoadEmbeddedLatin_LoadsGlyphs()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        Assert.True(db.OcrCharacters.Count > 600, $"expected the Latin set, got {db.OcrCharacters.Count} single glyphs");
        Assert.Equal(db.OcrCharacters.Count + db.OcrCharactersExpanded.Count, db.TotalCharacterCount);
    }

    [Fact]
    public void LoadEmbeddedLatin_HasExpandedEntries()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        Assert.NotEmpty(db.OcrCharactersExpanded);
    }

    /// <summary>
    /// A multi-blob glyph must match at whatever size the disc drew it, and wherever in its line it sits.
    /// Neither holds under the single-glyph cascade, which screens MarginTop in raw pixels and never relaxes
    /// past 17: a glyph at twice its trained size sits at twice the offset, and the bundled "%" is trained at
    /// offsets (27, 150) that describe its training line rather than itself.
    /// </summary>
    [Theory]
    [InlineData("%")]
    [InlineData("ø")]
    [InlineData("\"")]
    public void GetExpandedMatch_RecognizesItsOwnGlyphAtAnyScaleAndLinePosition(string text)
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        var glyphs = db.OcrCharactersExpanded.FindAll(c => string.Equals(c.Text, text, StringComparison.Ordinal));
        Assert.NotEmpty(glyphs);

        foreach (var glyph in glyphs)
        {
            foreach (var scale in new[] { 1.0, 1.4, 2.0, 3.0 })
            {
                var bitmap = TestFixtures.RasterizeForegroundScaled(glyph, scale);

                // Top of its line, and at the trained offset scaled up: the matcher must not care which.
                foreach (var topMargin in new[] { 0, (int)Math.Round(glyph.MarginTop * scale) })
                {
                    var match = db.GetExpandedMatch(
                        bitmap, topMargin, glyph.ExpandCount, deepSeek: true, maxWrongPixels: 25);

                    Assert.True(
                        match is not null && string.Equals(match.Text, text, StringComparison.Ordinal),
                        $"\"{text}\" at scale {scale} with topMargin {topMargin} read as {match?.Text ?? "nothing"}");
                }
            }
        }
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
