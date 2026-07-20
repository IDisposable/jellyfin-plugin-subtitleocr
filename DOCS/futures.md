# Backlog

The reviewed backlog is cleared: the output, text-correction, accent, and word-space work shipped; the italic
model was refactored to carry clean text plus spans; and the recognition-matcher heuristics were all tried and
rejected on the real-disc corpus (below), which is itself the finding: fewer placeholders come from more
`.nocr` training, not from matcher-level guessing.

## Open ideas

Nothing reviewed is outstanding. New ideas go here.

## Tried and rejected (do not re-attempt naively)

All three measured on the real-disc corpus:

- **Recursive, best-scoring glyph split.** Extending `TrySplit` to three or four touching glyphs measured
  worse every way it was built: capping the cut columns missed cuts the old exhaustive two-level search finds
  (placeholders 2050 to 2498 on AvP Requiem); trying every column unbounded exploded in cost (~2W squared
  full-scan matches on each wide unreadable blob, thousands per movie); and both forms invent wrong letters
  (`clan` read as `ctan`, `cream` as `c□am`), since each extra cut is another chance to match garbage. The old
  two-glyph, first-passing `TrySplit` is the sweet spot.
- **Vertical accent split.** Cropping the accent off an untrained precomposed letter and matching the base. On
  English it did nothing (byte-identical output: accents are rare there). On a French track it fired only 9
  times in 2621 cues, because the trained database already reads French accents whole (café, être, côtoyer all
  correct); where it did fire it resolved the untrained `ï` in "androïde" to `î`, a confidently wrong accent
  where `□` had honestly flagged the unknown. Net benefit near zero, and it trades an honest placeholder for a
  wrong letter, against the deliberate use of `□`.
- **Italic run preservation over an untrained variant.** Raised mid-word italic openings from 17 to 31 on the
  same corpus; the decisive-evidence prior stands.
