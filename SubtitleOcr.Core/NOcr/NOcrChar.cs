using System.Text;

namespace SubtitleOcr.Core.NOcr;

/// <summary>
/// One trained glyph: dimensions plus foreground/background test lines.
/// Binary layout matches Subtitle Edit's NOcrChar (MIT) so .nocr databases interchange.
/// </summary>
public sealed class NOcrChar
{
    public string Text { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Glyph top relative to its text line's top; distinguishes e.g. p from P.</summary>
    public int MarginTop { get; set; }

    public bool Italic { get; set; }
    public List<NOcrLine> LinesForeground { get; } = new();
    public List<NOcrLine> LinesBackground { get; } = new();

    /// <summary>&gt; 0 when this entry covers a run of N segmented items (ligatures, split glyphs).</summary>
    public int ExpandCount { get; set; }

    public bool LoadedOk { get; private set; }

    /// <summary>Aspect key used as a coarse screen before pixel checks (square = 100).</summary>
    public double HeightToWidthPercent => Height * 100.0 / Width;

    // Sensitive glyphs are shape-ambiguous; the matcher applies looser aspect but stricter passes.
    public bool IsSensitive => Text is "O" or "o" or "0" or "'" or "-" or ":" or "\"";

    public NOcrChar()
    {
    }

    /// <summary>Reads one record; sets LoadedOk=false at end-of-data or corruption.</summary>
    public NOcrChar(ref int position, byte[] file, bool isVersion2)
    {
        try
        {
            if (isVersion2)
            {
                if (position + 4 >= file.Length)
                {
                    return;
                }

                var isShort = (file[position] & 0b0001_0000) > 0;
                Italic = (file[position] & 0b0010_0000) > 0;

                if (isShort)
                {
                    ExpandCount = file[position++] & 0b0000_1111;
                    Width = file[position++];
                    Height = file[position++];
                    MarginTop = file[position++];
                }
                else
                {
                    position++;
                    ExpandCount = file[position++];
                    Width = file[position++] << 8 | file[position++];
                    Height = file[position++] << 8 | file[position++];
                    MarginTop = file[position++] << 8 | file[position++];
                }

                ReadText(ref position, file);

                if (isShort)
                {
                    ReadPointsBytes(ref position, file, LinesForeground);
                    ReadPointsBytes(ref position, file, LinesBackground);
                }
                else
                {
                    ReadPoints(ref position, file, LinesForeground);
                    ReadPoints(ref position, file, LinesBackground);
                }
            }
            else
            {
                if (position + 9 > file.Length)
                {
                    return;
                }

                Width = file[position++] << 8 | file[position++];
                Height = file[position++] << 8 | file[position++];
                MarginTop = file[position++] << 8 | file[position++];
                Italic = file[position++] != 0;
                ExpandCount = file[position++];
                ReadText(ref position, file);
                ReadPoints(ref position, file, LinesForeground);
                ReadPoints(ref position, file, LinesBackground);
            }

            LoadedOk = Width > 0 && Height > 0 && Width <= 1920 && Height <= 1080 && Text.IndexOf('\0', StringComparison.Ordinal) < 0;
        }
        catch
        {
            LoadedOk = false;
        }
    }

    private void ReadText(ref int position, byte[] file)
    {
        var textLen = file[position++];
        if (textLen > 0)
        {
            Text = Encoding.UTF8.GetString(file, position, textLen);
            position += textLen;
        }
    }

    private static void ReadPoints(ref int position, byte[] file, List<NOcrLine> target)
    {
        var length = file[position++] << 8 | file[position++];
        target.Capacity = length;
        for (var i = 0; i < length; i++)
        {
            target.Add(new NOcrLine(
                new OcrPoint(file[position++] << 8 | file[position++], file[position++] << 8 | file[position++]),
                new OcrPoint(file[position++] << 8 | file[position++], file[position++] << 8 | file[position++])));
        }
    }

    private static void ReadPointsBytes(ref int position, byte[] file, List<NOcrLine> target)
    {
        var length = file[position++];
        target.Capacity = length;
        for (var i = 0; i < length; i++)
        {
            target.Add(new NOcrLine(
                new OcrPoint(file[position++], file[position++]),
                new OcrPoint(file[position++], file[position++])));
        }
    }

    public void Save(Stream stream)
    {
        if (IsAllByteValues())
        {
            SaveShort(stream);
        }
        else
        {
            SaveWide(stream);
        }
    }

    private bool IsAllByteValues() =>
        Width <= byte.MaxValue && Height <= byte.MaxValue && MarginTop <= byte.MaxValue && ExpandCount < 16 &&
        LinesForeground.Count <= byte.MaxValue && LinesBackground.Count <= byte.MaxValue &&
        AllPointsFitByte(LinesForeground) && AllPointsFitByte(LinesBackground);

    private static bool AllPointsFitByte(List<NOcrLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Start.X > byte.MaxValue || line.Start.Y > byte.MaxValue ||
                line.End.X > byte.MaxValue || line.End.Y > byte.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    private void SaveShort(Stream stream)
    {
        var flags = 0b0001_0000;
        if (Italic)
        {
            flags |= 0b0010_0000;
        }

        flags |= ExpandCount & 0x0F;
        stream.WriteByte((byte)flags);
        stream.WriteByte((byte)Width);
        stream.WriteByte((byte)Height);
        stream.WriteByte((byte)MarginTop);
        WriteText(stream);
        WritePointsBytes(stream, LinesForeground);
        WritePointsBytes(stream, LinesBackground);
    }

    private void SaveWide(Stream stream)
    {
        var flags = Italic ? 0b0010_0000 : 0;
        stream.WriteByte((byte)flags);
        stream.WriteByte((byte)ExpandCount);
        WriteUInt16(stream, (ushort)Width);
        WriteUInt16(stream, (ushort)Height);
        WriteUInt16(stream, (ushort)MarginTop);
        WriteText(stream);
        WritePoints(stream, LinesForeground);
        WritePoints(stream, LinesBackground);
    }

    private void WriteText(Stream stream)
    {
        var buffer = Encoding.UTF8.GetBytes(Text);
        stream.WriteByte((byte)buffer.Length);
        stream.Write(buffer, 0, buffer.Length);
    }

    private static void WritePointsBytes(Stream stream, List<NOcrLine> points)
    {
        stream.WriteByte((byte)points.Count);
        foreach (var p in points)
        {
            stream.WriteByte((byte)p.Start.X);
            stream.WriteByte((byte)p.Start.Y);
            stream.WriteByte((byte)p.End.X);
            stream.WriteByte((byte)p.End.Y);
        }
    }

    private static void WritePoints(Stream stream, List<NOcrLine> points)
    {
        WriteUInt16(stream, (ushort)points.Count);
        foreach (var p in points)
        {
            WriteUInt16(stream, (ushort)p.Start.X);
            WriteUInt16(stream, (ushort)p.Start.Y);
            WriteUInt16(stream, (ushort)p.End.X);
            WriteUInt16(stream, (ushort)p.End.Y);
        }
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    public override string ToString() => Text;
}
