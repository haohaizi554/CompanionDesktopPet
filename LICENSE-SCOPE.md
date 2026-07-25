# License scope

This repository uses a split license. The word **software** in `LICENSE.md` means only the **Technical Code** described below. Making source files visible does not grant rights beyond the licenses and limited permissions stated here.

## Technical Code

Subject to the exclusions below, original program logic, build scripts, tests, validation tooling, and CI/CD workflow definitions are available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md). That license permits noncommercial use, study, research, experimentation, modification, and distribution under its terms.

This is a **source-available, noncommercial** license. It is not an OSI-approved open-source license and does not permit commercial use.

## Excluded materials

The PolyForm license does **not** apply to Character Assets or Persona Materials. Those materials are governed by [ASSET_AND_PERSONA_RIGHTS.md](ASSET_AND_PERSONA_RIGHTS.md), with all rights reserved except for the narrow permission stated there. Excluded materials include, without limitation:

- character artwork, icons, animations, visual design, and their derivatives, including `src/CompanionDesktopPet/Assets/character*.png` and `src/CompanionDesktopPet/Assets/pet.ico`;
- names, nicknames, identity, personality, voice, tone, backstory, relationship framing, catchphrases, and other character-defining expression;
- all dialogue and persona corpus content, semantic groups, story/decision trees, curated compilations, annotations, and editorial selections;
- `src/CompanionDesktopPet/Assets/persona-corpus.tsv`, `data/**`, `reports/**`, and persona-bearing portions of generated or configuration files;
- `src/persona_corpus/content_catalog.py`, `config/persona-contract.json`, `config/persona-scheduler.json`, `config/persona-editorial-manifest.json`, and `config/persona-review-allowlist.json` to the extent they contain Persona Materials or expressive editorial choices;
- `src/CompanionDesktopPet/Services/PersonaContract.g.cs` to the extent generated from excluded persona/configuration material; and
- `outputs/**`, release executables, and other packaged artifacts to the extent they contain or reproduce any excluded material.

If Technical Code and excluded material appear in the same file or artifact, the PolyForm license covers only the separable Technical Code. It does not license the excluded expression, data, artwork, identity, or compilation.

## Third-party material

Third-party components remain subject to their own license terms. No right is granted where the repository licensor does not own or cannot license the relevant rights.

## Required notice

Source redistributions of licensed Technical Code must preserve `LICENSE.md`, this scope document, `NOTICE`, and every line beginning with `Required Notice:` as required by the PolyForm license. Official release bundles carry the same byte-exact license text under the conventional asset name `LICENSE`.
