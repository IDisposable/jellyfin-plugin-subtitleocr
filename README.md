<p align="center">
  <img src="assets/social.png" alt="SubtitleOCR Jellyfin Plugin" width="820">
</p>

# jellyfin-plugin-subtitleocr

Converts embedded image-based DVD subtitles (dvdsub/VobSub) to SRT (`.srt`) files
using nOCR pattern matching — no Tesseract, no native dependencies, pure managed code.
Database format is interchange-compatible with [Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)'s
`.nocr` files, so databases trained or corrected in SE's GUI drop straight in.

<p align="center">
<img alt="Build" src="https://img.shields.io/github/actions/workflow/status/IDisposable/jellyfin-plugin-mindthegaps/build.yaml?branch=main">
<img alt="License" src="https://img.shields.io/badge/license-MIT-blue">
<img alt="Jellyfin 10.11" src="https://img.shields.io/badge/Jellyfin-10.11-blueviolet">
</p>

## Installation

### From a Jellyfin repository (recommended)

1. In the Jellyfin dashboard, go to **Plugins > Repositories > +**.
2. Add a repository with this manifest URL:
   ```
   https://raw.githubusercontent.com/IDisposable/jellyfin-plugin-subtitleocr/main/manifest.json
   ```
3. Open the **Catalog** tab and find **Subtitle OCR** under the *Subtitles* category.
4. Click **Install**.
5. Restart your Jellyfin server.

New releases show up in the catalog automatically.

### Beta channel

For pre-release builds, use this manifest URL instead:
```
https://raw.githubusercontent.com/IDisposable/jellyfin-plugin-subtitleocr/main/manifest-beta.json
```

Both channels publish the same builds; the stable channel carries only stable releases,
the beta channel carries all releases. Add one repository or the other, not both.

### Manual

Download the `.zip` from the latest [GitHub release](https://github.com/IDisposable/jellyfin-plugin-subtitleocr/releases),
extract it into a folder under your server's `config/plugins/` directory (e.g.
`config/plugins/SubtitleOcr/`), and restart Jellyfin.

Your server version must match the plugin's target ABI (currently `10.11.0.0`, `net9.0`).

## Status: Initial beta

The core library is implemented and verified. The Jellyfin host layer (plugin, scheduled
task, config page) is written against the 10.11 API surface but has not been run against
a live server yet.

## Architecture

```
Jellyfin.Plugin.SubtitleOcr        host layer (Jellyfin.Controller 10.11)
├── Plugin.cs                      registration + config page
├── ScheduledTasks/OcrSubtitlesTask   finds VobSub/PGS items; refresh + progress reporting
└── Pipeline/SubtitleOcrPipeline   per-file orchestration

SubtitleOcr.Core                   zero Jellyfin dependencies, fully testable
├── Extraction/FfprobeSubtitleReader  ffprobe JSON → packets + stream list (VobSub/PGS)
├── VobSub/                        SPU control sequences + RLE → RGBA; per-packet timing
├── Pgs/                           PGS segment/RLE decoder; show/clear display-set timing
├── Subtitles/SubtitleImage        format-agnostic timed bitmap the OCR consumes
├── Imaging/                       binarization, projection-profile segmentation
├── NOcr/                          .nocr DB read/write, match cascade, engine
├── Ocr/                           language codes + conservative l/I-class fixes
└── Output/SrtWriter               timing normalization + serialization
```

Data flow per file:

1. `ffprobe -show_streams -show_data` lists image-based subtitle streams (VobSub and
   PGS). VobSub palette is parsed from the idx-style extradata (ffmpeg default as
   fallback); PGS carries its palette in-band.
2. `ffprobe -show_packets -show_data` yields assembled display units regardless of
   container (MKV, VOB/PS, M2TS) — no PES demux or mkvextract needed.
3. VobSub SPUs decode per packet (end time from the StopDisplay delay, then packet
   duration, then the next packet). PGS display sets decode across packets: a "show"
   set is bounded by the next "show" or "clear". Both yield a timed `SubtitleImage`.
4. Binarize, segment into glyphs, match against the per-language nOCR database,
   assemble text with word spacing and `<i>` runs, post-process, write the SRT file
   (`{name}.{lang}.srt`). The task then queues a library refresh so Jellyfin attaches
   it immediately.

## Verified (SubtitleOcr.Core.Tests, xunit)

- Bundled `Latin.nocr` (from Subtitle Edit, MIT): loads all 690 glyphs; parse
  consumes the file byte-exactly
- Save/load round-trip is lossless
- Matcher self-recognition: 96% at zero error budget (rasterizing a glyph's own
  trained foreground lines and matching it back)
- SPU decoder: hand-encoded subpicture decodes to pixel-exact output, including
  the end-of-line nibble realignment path
- PGS decoder: hand-encoded display set decodes to pixel-exact output; show/clear
  display sets pair into correctly timed events
- VobSub and PGS track timing (StopDisplay / next-packet / show-clear bounding)
- Hex dump parser matches ffprobe's `print_data_xxd` exactly (verified against
  fftools source), including the ambiguous single-space-pad full-line case
- Language-code normalization (639-1/2B/2T) and Latin-script gating
- SRT timing normalization and serialization

Run: `dotnet test`

## Building the plugin

```bash
dotnet publish Jellyfin.Plugin.SubtitleOcr -c Release
# or via jprm using build.yaml
```

Targets Jellyfin 10.11 / net9.0 by default. To build against another ABI without
editing the csproj, pair the two overrides, e.g.
`dotnet build -p:JellyfinVersion=10.10.7 -p:TargetFramework=net9.0` (restore separately
first when overriding `TargetFramework`).

## Languages

The stream language selects the OCR database. Latin-script languages (English, French,
German, Spanish, ...) all use the bundled `Latin.nocr`; the output is tagged with the
stream's language (`{name}.{lang}.srt`, multiple same-language tracks keep the source
stream index). Non-Latin scripts need their own database (Cyrillic, Greek, ...); only the
Latin database is bundled, so obtain or train the others in Subtitle Edit's nOCR window.

To install one, drop a file named `{language}.nocr` (e.g. `rus.nocr`, `ell.nocr`) into the
plugin's `nocr` data folder and it is used automatically; or add a per-language entry on the
config page (its path may be absolute or a bare file name in that folder). Resolution order
per language: config entry, then drop-in `{language}.nocr`, then the global database path,
then bundled Latin. English-only OCR fixups are skipped for non-Latin scripts.

Enable "Skip image tracks whose language already has a text subtitle" on the config page to
OCR only the languages you are missing: an image track is skipped when the item already has a
text-based subtitle (embedded stream or external sidecar) in the same language. Untagged
image tracks are always converted, since their language cannot be matched with confidence.

## Known gaps (roughly in priority order)

1. **Segmentation is projection-profile only.** Italic glyphs that overlap
   vertically merge into one segment and fail to match. SE's splitter handles
   this with per-pixel flood fill and italic-shear heuristics — port candidate.
2. **Expanded (multi-segment) glyph matching not wired into the engine.**
   The DB loads the 19 expanded entries but `NOcrEngine` only does single-glyph
   matching. Ligature-heavy fonts will show as unknowns.
3. **Binarization is luma-threshold only.** Works for the standard light-text/
   dark-outline case (with `InvertLuma` for the reverse); discs with unusual
   CLUT usage may need per-color-index selection instead.
4. **No unknown-glyph export.** Dumping unmatched glyph bitmaps (BMP) to a
   training folder would close the loop with SE's nOCR training window.
5. **PGS timing assumes one display set per packet.** Holds for MKV and typical
   M2TS demuxes; a display set split across packets would need cross-packet ODS
   accumulation. Cropped-object composition is handled but rarely exercised.

## License

MIT. See NOTICE.md for Subtitle Edit attribution (nOCR format/matcher port and
the bundled Latin database).
