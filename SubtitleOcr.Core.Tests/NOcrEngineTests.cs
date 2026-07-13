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
