# Third-Party Notices

## Subtitle Edit (MIT)

The following components are derived from or format-compatible with
[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit),
Copyright (c) Nikolaj Olsson, MIT License:

- `SubtitleOcr.Core/Assets/Latin.nocr` — trained nOCR database, included verbatim
- `SubtitleOcr.Core/NOcr/NOcrChar.cs`, `NOcrLine.cs`, `NOcrDb.cs` — binary format,
  point interpolation, and match cascade thresholds ported from
  `src/libuilogic/Ocr/` to preserve database interchange
- `SubtitleOcr.Core/VobSub/SubPictureDecoder.cs` — RLE and control-sequence
  handling cross-checked against `src/libse/VobSub/SubPicture.cs`

Note: the MIT license applies to the current Subtitle Edit 5 (Avalonia) source
tree; the legacy 3.6.x WinForms tree was GPL-3.0. All ported code here derives
from the MIT tree.

## WeCantSpell.Hunspell (MPL 1.1)

[WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell), a pure managed
C# port of Hunspell, is referenced as an unmodified NuGet dependency and used under the
Mozilla Public License 1.1 (elected from its MPL 1.1 / GPL 2.0 / LGPL 2.1 tri-license).
Hunspell dictionaries themselves are not bundled; users drop their own {language}.dic /
{language}.aff files into the plugin dictionaries folder.

## Format documentation

DVD subpicture format reference: http://www.mpucoder.com/DVD/spu.html
