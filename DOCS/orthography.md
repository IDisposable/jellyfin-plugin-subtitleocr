# Legal accents per language

The diacritic fold (`OcrPostProcessor`, `LanguageDiacritics`) folds an accented Latin letter that is
foreign to the track's language to its base letter, on the reasoning that an accent the language never
writes is an OCR misread. A letter the language does use is kept. This file is the source of that table.

Keyed by ISO 639-2/T code (what `LanguageCodes.Normalize` produces). Lowercase only; the fold lowercases
each candidate before the lookup. English is present with an empty set, so it folds every accent. A
language absent from the table is unknown: its accents cannot be judged, so nothing is folded.

| Code | Language | Legal accented letters |
|------|----------|------------------------|
| eng | English | (none) |
| fra | French | à â ç é è ê ë î ï ô ù û ü ÿ œ |
| deu | German | ä ö ü ß |
| spa | Spanish | á é í ó ú ü ñ |
| por | Portuguese | á â ã à ç é ê í ó ô õ ú |
| ita | Italian | à è é ì ò ó ù |
| nld | Dutch | á é í ó ú ë ï ö ü |
| swe | Swedish | å ä ö é |
| nob nno nor | Norwegian | æ ø å é |
| dan | Danish | æ ø å é |
| fin | Finnish | ä ö å |
| isl | Icelandic | á é í ó ú ý þ æ ö ð |
| pol | Polish | ą ć ę ł ń ó ś ź ż |
| ces | Czech | á č ď é ě í ň ó ř š ť ú ů ý ž |
| slk | Slovak | á ä č ď é í ĺ ľ ň ó ô ŕ š ť ú ý ž |
| hun | Hungarian | á é í ó ö ő ú ü ű |
| ron | Romanian | ă â î ș ț |
| hrv | Croatian | č ć đ š ž |
| slv | Slovenian | č š ž |
| tur | Turkish | ç ğ ı ö ş ü |
| cat | Catalan | à é è í ï ó ò ú ü ç ŀ |
| est | Estonian | ä ö õ ü š ž |
| lav | Latvian | ā č ē ģ ī ķ ļ ņ š ū ž |
| lit | Lithuanian | ą č ę ė į š ų ū ž |

Cross-verified against Wikipedia orthography/alphabet articles. Where a letter was a genuine judgment
call, the choice erred toward keeping a letter that is formally part of the standard alphabet, so a
legitimate accent is never wrongly folded.

## Judgment calls and exclusions

- **fra** `æ` excluded (Latin/Greek phrases only, "et cætera"); `ÿ` kept though it occurs essentially
  only in proper nouns (Aÿ, Croÿ). https://en.wikipedia.org/wiki/French_orthography
- **por** `ü` excluded: abolished by the 1990 Orthographic Agreement (in force from 2009). Re-add only
  for pre-2009 subtitles. https://en.wikipedia.org/wiki/Portuguese_orthography
- **ita** `í î` excluded as nonstandard/archaic. https://en.wikipedia.org/wiki/Italian_orthography
- **nld** `á í ó ú` are the acute stress mark, far rarer than `é`; kept for safety. Grave `à è` excluded.
  https://en.wikipedia.org/wiki/Dutch_orthography
- **nob nno nor dan** `é` is a legitimate but optional accent (idé, allé, kafé; Danish én vs en), not a
  core alphabet letter; kept for consistency with Swedish. Drop to bare `æøå` for core-alphabet-only.
  https://en.wikipedia.org/wiki/Danish_and_Norwegian_alphabet
- **fin** `š ž` excluded (rare foreign letters); native set is `äöå`.
- **ces slk** `ó` occurs mostly in loanwords but is a formal alphabet letter; kept.
- **est** `š ž` are official but classed as foreign letters (võõrtähed); kept. Native-only is `äöõü`.
  https://en.wikipedia.org/wiki/Estonian_orthography
- **lav** `ō ŗ` excluded as archaic (dropped in the 1946/1957 reforms); not in the modern 33-letter
  alphabet. https://en.wikipedia.org/wiki/Latvian_orthography
- **slv** `ć đ` excluded; standard Slovene is only `č š ž`, the others appear solely in non-Slovene names.
  https://en.wikipedia.org/wiki/Slovene_alphabet
- **cat** `ŀ` (U+0140, l with middle dot, the ela geminada in col·legi) is standard, so included, but
  real files very often type it as `l·l` (l + U+00B7) or `l.l`. https://en.wikipedia.org/wiki/Catalan_orthography
- **ron / tur** the standard Romanian `ș ț` are the comma-below forms (U+0219, U+021B); the standard Turkish
  `ş ţ` are the cedilla forms (U+015F, U+0163). Files routinely carry the other language's form, so
  `LanguageDiacritics.Canonicalize` rewrites the lookalike to the track language's own form before the fold
  (Romanian keeps only the comma-below set, Turkish only the cedilla). https://en.wikipedia.org/wiki/Romanian_alphabet
- **Vietnamese** and other unaccented-fallback languages are deliberately absent: an unknown language
  folds nothing, which already keeps all of Vietnamese's tone-marked letters.

## Implementation notes

- The table stores precomposed (NFC) letters, and the fold normalizes each word to NFC before comparing,
  so combining-mark (NFD) input still matches.
- Digraphs (dž, lj, nj, dz) are excluded by design: they are ASCII letter combinations, not single glyphs.
- Stroked letters and ligatures (ł, ø, đ, ħ, æ, œ, ß) carry no combining mark to drop, so the fold maps
  them to their base by hand when they are foreign to the language.
