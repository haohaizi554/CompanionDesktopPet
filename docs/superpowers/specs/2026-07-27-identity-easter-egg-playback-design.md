# Identity Easter Egg Playback Design

## Goal

Keep exactly 3,000 of the 30,000 authored rows in the `easter_egg` category
group while making the explicitly user-authorized identity markers
`雷琳玥`, `小玥`, `玥仔`, and `玥玥` feel natural during normal use.  The
identity is character lore for the fictional desktop companion, not a claim
to observe a real person or the user.

This design supersedes the earlier authored-corpus rule that nickname eggs
must be rare, source-line-pinned legacy rows with long wall-clock cooldowns.

## Content allocation

`b083.tsv` through `b092.tsv` are the fixed 3,000-row identity Easter Egg
segment.  Each file contains 300 literal, independently authored rows and
12 semantic groups of 25 rows.

| Batches | Rows | Direct identity allocation |
| --- | ---: | --- |
| `b083` | 300 | `雷琳玥` |
| `b084` | 300 | `小玥` |
| `b085` | 300 | `玥仔` |
| `b086` | 300 | `玥玥` |
| `b087`–`b092` | 1,800 | related character lore, seasonal details, programming in-jokes, and gentle life fragments |
| **Total** | **3,000** | **10% of the 30,000-row corpus** |

The first four batches guarantee 300 literal direct-marker rows per marker.
The remaining six batches belong to the same identity Easter Egg collection
but do not need to repeat a marker in every sentence.  This preserves
recognition without turning normal playback into repetitive name calls.

Identity markers are permitted, rather than required, in any other category
when an authored sentence makes them natural.  They may therefore occur in
technical, growth, daily-care, emotional-reflection, or character-life rows.
The category still determines the row's configured output mode.  No
unapproved name-like marker is permitted anywhere.

## Relationship and writing rules

Every identity-marker row may use `neutral`, `warm_friend`,
`playful_friend`, or `nickname_easter_egg`; the profile follows the sentence
instead of forcing all names into a single rare profile.  All prose remains
one-way companion narration and must not contain a question prompt,
dependency, exclusivity, coercion, sexual material, clinical diagnosis, or a
claim that the app can observe uncollected user activity.

Allowed identity references are character-signature details, personal
nicknames, small celebrations, code-and-life metaphors, seasonal notes, and
ordinary affectionate self-reference.  They must not present biographical
claims as information about a real person.

## Playback model

Wall-clock cooldowns are not suitable for a desktop pet that may only run
for a short session.  Direct-marker selection uses a session exposure policy:

- the same semantic group requires at least three intervening emitted bubbles;
- a group is ineligible while it appears in the most recent eight bubbles;
- a continuous app session may emit at most three direct-marker rows of the
  same identity class; a fresh app launch starts a new session;
- special festival and memorial semantic groups still use their ordinary
  semantic-group recent window, not a calendar-day lockout.

The existing global minimum interval and category selection constraints stay
in effect.  The policy is applied before candidate scoring and is tracked in
in-process selection state for auditability.  It is deliberately not
persisted, so a fresh app launch starts a fresh exposure session while
degraded fallback remains deterministic and safe.

## Contract and validation

`persona-contract.json` becomes the sole policy source.  It retains all four
markers in `privacy.pii_markers` and adds a versioned `authored_identity`
section with the marker list, direct-marker batch allocation, allowed
profiles, all-category placement permission, and session exposure limits.
The prior legacy editorial manifest remains provenance for legacy data only;
it does not authorize new authored rows.

The authored loader and runtime builder must fail closed when any of these
conditions is false:

1. `b083`–`b092` contain exactly 3,000 `EasterEgg/easter_egg` rows, so the
   source corpus has an exact 10% Easter Egg allocation.
2. `b083`–`b086` contain exactly 300 direct occurrences for their assigned
   marker, and no direct marker is misspelled or unregistered.
3. A marker-bearing row uses a configured category, group, output mode, and
   allowed relationship profile; all ordinary category contracts continue to
   apply.
4. Marker-bearing rows satisfy the normal safety rules and cannot claim
   uncollected user state.
5. Session exposure tests prove the three-intervening-bubble, recent-eight,
   per-identity-session-cap, and restart-reset boundaries.

The validators report the batch, variant ID, marker, and failed invariant so
an editor can correct a literal row without guessing.  Exact text and
high-similarity gates remain active across all 30,000 rows.

## Acceptance evidence

- Python tests cover malformed marker placement, wrong direct-marker counts,
  a wrong category output mode, an unknown marker, and every session exposure
  boundary.
- C# tests cover deterministic selector behavior across the session limit and
  app restart boundary.
- The final authored source audit reports 30,000 rows, 3,000 Easter Egg rows,
  300 guaranteed direct rows for each of the four markers, no duplicate IDs or
  normalized text, no semantic metadata drift, and no similarity violation.
- The generated manifest, ledger, runtime corpus, C# contract, and release
  evidence bind the policy and corpus hashes together.
