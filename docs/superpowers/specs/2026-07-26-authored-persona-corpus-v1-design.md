# Authored Persona Corpus v1 Design

## Goal

Replace the legacy runtime corpus with at least 30,000 static, individually
traceable Chinese companion lines.  Every runtime line must be a complete,
authored sentence; prefix/core/suffix materialisation and legacy surface
variants are archive-only and must not be selectable at runtime.

## Product boundaries

- The character is an adult fictional companion.  The writing is warm,
  introverted, occasionally teasing, and tailored to a computer-science
  developer.
- `warm_friend` may be gently affectionate.  It must remain non-explicit,
  non-sexual, non-coercive, non-exclusive, and never demand a reply.
- Nickname easter eggs (`小玥` and `玥玥`) are exact approved lines only.  They
  are rare and cannot be inferred from a broad keyword rule.
- No line may claim to observe uncollected user state, diagnose health or
  mental state, imitate a real person, or suggest that the user should depend
  on the character instead of people around them.
- The desktop pet remains a one-way broadcaster: `requires_reply=false` for
  all authored runtime lines.

## Source layout and provenance

The source of truth is a directory of 100 independently reviewable TSV
batches rather than a generated Python tuple or a template program:

```text
data/authored/v1/b001.tsv ... data/authored/v1/b100.tsv
config/persona-authorship-manifest.json
data/optimized/persona-authorship-ledger.tsv       # derived, tracked
data/optimized/persona-corpus-v2.tsv               # derived runtime asset
```

Each batch has exactly 300 literal rows and its own UTF-8 TSV header.  The
complete source inventory is exactly 30,000 rows.  A batch row carries the
existing semantic metadata plus `batch_id` and `relationship_profile`.
`variant_id` is stable and descriptive, for example
`authored.b001.tech.debugging.stacktrace.0001`.  The builder derives the
runtime `id` and source reference:

```text
catalog:authored-v1:b001;variant:authored.b001.tech.debugging.stacktrace.0001
```

The authorship manifest fixes the expected 100 batch IDs, 300 rows per batch,
the sorted text/metadata SHA-256 digest of each batch, and one root digest.
The derived ledger has one row per runtime line with its batch, variant ID,
text hash, metadata hash, review status, and relationship profile.  A text
change requires a new variant ID; it cannot silently overwrite a released
line.

## Runtime data contract

`relationship_profile` becomes an explicit versioned field in the TSV and
C# contract.  It is not overloaded into `tone` or hidden in a source
reference.  Valid values are:

| Profile | Meaning | Runtime budget |
| --- | --- | --- |
| `neutral` | normal technical, life, care, and ambient narration | default |
| `warm_friend` | light non-exclusive affection and encouragement | at most 2 in recent 20 |
| `playful_friend` | gentle familiar teasing without belittling | ordinary scene cooldowns |
| `nickname_easter_egg` | exact approved `小玥` / `玥玥` easter egg | at most 1 in recent 100 |

All lines in one semantic scene must have the same relationship profile,
alongside the existing scene-signature metadata.  The selector applies the
profile budget before scoring a scene, then preserves the current category,
context, cooldown, line-exposure, and deterministic selection rules.  Batch
identity is audit metadata only and is never a playback preference.

## Content allocation

The 100 batches are fixed before writing.  This both matches the scheduler
targets and prevents a technical-only corpus from crowding out everyday
presence.

| Category group | Batches | Lines |
| --- | ---: | ---: |
| `technical` | 18 | 5,400 |
| `growth` | 10 | 3,000 |
| `career` | 7 | 2,100 |
| `daily_care` | 10 | 3,000 |
| `emotional_reflection` | 10 | 3,000 |
| `character_life` | 27 | 8,100 |
| `easter_egg` | 10 | 3,000 |
| `system_ambient` | 8 | 2,400 |
| **Total** | **100** | **30,000** |

Every sentence is written as a standalone thought.  A batch may have a main
topic and a few subtopics, but the authoring process must not create a grid of
openers × technical cores × endings or a masked equivalent.  Most rows use
`required_context=none`; rows using time, season, holiday, app-start, or
other signals are permitted only when the signal is implemented and covered
by a runtime simulation.

## Build and validation design

The builder accepts the authored directory and manifest explicitly.  It
loads batches lazily and strictly, verifies all row and batch hashes, then
creates exactly one enabled runtime row for each authored source row.  It
does not invoke `prepare_legacy_surface_candidates` or
`materialize_legacy_surface_candidates` for the runtime result.  Legacy
source and surface files remain immutable audit evidence but must produce
zero enabled `legacy_surface_variant` rows.

The published contract changes from an expanded 50k+ surface inventory to an
authored 30,000-row inventory and replaces the legacy-surface release count
with an authored count and manifest/root hashes.  The C# embedded-resource
reader validates the same inventory and controlled relationship values before
constructing scenes.  Loading remains background/warm-up work so the WPF UI
never blocks on a 30k-row parse.

Validation has hard errors, not warnings, for:

- missing or malformed batch/header/UTF-8 data;
- wrong batch count or total count;
- duplicate ID, variant ID, normalized text, or manifest entry;
- changed text/metadata/root hash;
- any enabled legacy surface or generated source kind;
- Cartesian 2×2 and 2×2×2 construction fixtures, regardless of source kind;
- unreviewed nickname lines, unsafe relationship language, PII, question
  prompts, `requires_reply`, or unsupported runtime context;
- high-similarity candidate pairs not covered by a recorded editorial
  adjudication.

Similarity checking uses indexed character n-grams/LSH candidates followed by
exact normalized similarity checks.  It must not perform an impractical
all-pairs scan on 30,000 rows.

## Acceptance and release gates

1. Source, manifest, ledger, runtime TSV, C# generated contract, and embedded
   resource agree on 30,000 authored rows and zero legacy surfaces.
2. A deterministic clean build reconstructs byte-identical derived corpus,
   ledger, and manifest outputs.
3. Python validation and simulation have zero warnings and exercise every
   implemented daypart, season, holiday, direct event, signal, and profile
   quota.  Budget-rule tests include deliberately invalid night, hourly,
   minimum-interval, and adjacent-category cases.
4. C# parsing, scene construction, profile quota, selection smoke, and
   warm-up tests pass.  A 30k-corpus performance test confirms the UI-facing
   path remains bounded without relaxing existing thresholds.
5. CI parses actual Python and .NET test evidence, compares discovery with
   execution, and publishes the audited inventory/hash evidence.  No warnings
   may be allowlisted for this release.

