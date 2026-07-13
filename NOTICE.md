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

## Format documentation

DVD subpicture format reference: http://www.mpucoder.com/DVD/spu.html
