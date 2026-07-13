using SubtitleOcr.Core.Extraction;
using Xunit;

namespace SubtitleOcr.Core.Tests;

public class FfprobeSubtitleReaderTests
{
    // ffprobe print_data_xxd format: 4-hex-char groups, single-space pad on full lines,
    // a gutter that can start with hex-lookalike characters.
    [Fact]
    public void ParseHexDump_DecodesXxdFormat()
    {
        const string dump =
            "\n00000000: 0a1b 2c3d 4e5f 6071 8293 a4b5 c6d7 e8f9 ..,=N_`qabcd ff.\n" +
            "00000010: 00ff                                     ..\n";

        var parsed = FfprobeSubtitleReader.ParseHexDump(dump);

        Assert.Equal(18, parsed.Length);
        Assert.Equal(0x0A, parsed[0]);
        Assert.Equal(0xF9, parsed[15]);
        Assert.Equal(0xFF, parsed[17]);
    }
}
