using SubtitleOcr.Core.Imaging;
using SubtitleOcr.Core.NOcr;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class NOcrEngineTests
{
    // Composes a synthetic "Hi" from two trained glyphs on one canvas and checks the engine
    // segments and recognizes the leading capital.
    [Fact]
    public void Recognize_SyntheticGlyphs_RecognizesLeadingCapital()
    {
        var db = NOcrDb.LoadEmbeddedLatin();
        var glyphH = db.OcrCharacters.Find(c => c.Text == "H" && !c.Italic && c.LinesForeground.Count > 5);
        var glyphI = db.OcrCharacters.Find(c => c.Text == "i" && !c.Italic && c.LinesForeground.Count > 3);
        Assert.NotNull(glyphH);
        Assert.NotNull(glyphI);

        var width = glyphH!.Width + 2 + glyphI!.Width + 20;
        var height = Math.Max(glyphH.MarginTop + glyphH.Height, glyphI.MarginTop + glyphI.Height) + 2;
        var canvas = new SubBitmap(width, height);

        Draw(canvas, glyphH, offsetX: 0);
        Draw(canvas, glyphI, offsetX: glyphH.Width + 2);

        var engine = new NOcrEngine(db, new NOcrEngineOptions { MaxWrongPixels = 0, DeepSeek = false });
        var result = engine.Recognize(canvas);

        Assert.Contains('H', result.Text);
    }

    // A comma is the same shape either way, so it carries no italic signal.
    [Theory]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData("…")]
    public void Recognize_ItalicPunctuation_EmitsNoItalicTags(string punctuation)
    {
        var result = RecognizeAsItalic(punctuation);

        Assert.DoesNotContain("<i>", result.Text, StringComparison.Ordinal);
        Assert.Contains(punctuation, result.Text, StringComparison.Ordinal);
    }

    // The counterpart: a letter does carry the signal, so it must still be tagged.
    [Fact]
    public void Recognize_ItalicLetter_StillEmitsItalicTags()
    {
        var result = RecognizeAsItalic("H");

        Assert.Contains("<i>", result.Text, StringComparison.Ordinal);
    }

    // The rule keys off the matched entry's text, so this reuses one shape known to match itself and
    // relabels it: rasterized punctuation is too small to match, and an italic shape never matches itself.
    private static NOcrResult RecognizeAsItalic(string text)
    {
        var latin = NOcrDb.LoadEmbeddedLatin();
        var glyph = latin.OcrCharacters.Find(c => c.Text == "H" && !c.Italic && c.LinesForeground.Count > 5);
        Assert.NotNull(glyph);
        glyph!.Italic = true;
        glyph.Text = text;

        var db = new NOcrDb();
        db.OcrCharacters.Add(glyph);

        var canvas = new SubBitmap(glyph.Width + 4, glyph.MarginTop + glyph.Height + 2);
        Draw(canvas, glyph, offsetX: 0);

        return new NOcrEngine(db, new NOcrEngineOptions { MaxWrongPixels = 0, DeepSeek = false }).Recognize(canvas);
    }

    private static void Draw(SubBitmap canvas, NOcrChar glyph, int offsetX)
    {
        foreach (var line in glyph.LinesForeground)
        {
            foreach (var p in line.GetPoints())
            {
                var x = p.X + offsetX;
                var y = p.Y + glyph.MarginTop;
                if (x >= 0 && y >= 0 && x < canvas.Width && y < canvas.Height)
                {
                    canvas.SetPixel(x, y, 255, 255, 255, 255);
                }
            }
        }
    }
}
