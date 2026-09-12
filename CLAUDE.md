# Working in this repository

ImplicitSave is a zero-config save system for Unity, published free on GitHub, OpenUPM and the Unity
Asset Store. This file is the briefing for an AI assistant working here; the README is the one for
people using the package.

## What this repository is

The whole Unity development project, not only the package. The distributable is the embedded package
under `Packages/com.bionics.implicitsave/`.

```
Packages/com.bionics.implicitsave/   the package - this is what ships
  Runtime/                           no UnityEditor references, ever
  Editor/                            includePlatforms: ["Editor"] - never in a build
  Tests/{Runtime,Editor}/            not shipped to the Asset Store (see Releasing)
  Samples~/                          four samples, each with a demo scene
Assets/ImplicitSaveSandbox/          manual test scenes; never referenced by tests
.ci/compat-host/                     bare project the CI runs the suite in
.ci/make-assetstore-copy.py          builds and verifies the Asset Store copy
.github/workflows/tests.yml          the suite on four Unity versions
Docs/                                gitignored on purpose - planning and store copy
```

## Non-negotiables

These exist because breaking them caused real, silent data loss more than once.

1. **Parity with Unity's serializer.** What the editor window shows and what the file contains must be
   the same set of members. `UnitySerializationRules` is the single source of that truth, read by both
   `UnityContractResolver` and `SaveDataValidator`. `MemberParityTests` compares our list against
   Unity's own `SerializedObject`. Never remove it, never relax it, never add an exception list.
   - The package follows the editor it runs in. Unity 6.6 serializes `Dictionary` natively, so the
     rules accept it there and refuse it before — gated behind `UNITY_6000_6_OR_NEWER`.
2. **The file stores ids, never .NET type names.** `[SaveId]` names the file, `[SaveType]` gives a
   subtype its `$t`. Renaming a class must not break a save; changing an id needs `PreviousIds`.
3. **Json.NET is a declared dependency and is never shipped inside the package.** Embedding a copy
   would conflict with every other asset that uses it.
4. **Runtime never references `UnityEditor`.** Editor code lives in the editor-only assembly.
5. **A failed save never crashes the game.** Report through `SaveManager.Failed`. A file written by a
   newer build is never overwritten, not even with `force`.
6. **No mention of donations anywhere inside the package** — Asset Store Provider Agreement 4.9.1.3.
   The repository README and the asset's store page are the only places it may appear.
7. **Static state is reset in `SubsystemRegistration`**, events included. The project runs with
   *Disable Domain Reload* on so this breaks loudly instead of quietly.

## Tests

322 tests: 91 EditMode, 231 PlayMode. Everything that ships has tests; a bug fix starts with the test
that reproduces it.

- In the editor: `Window > General > Test Runner`.
- Headless, any installed version: `unity test <project> --mode EditMode|PlayMode --editor-version <v>`
  (the Unity CLI lives in `%LOCALAPPDATA%\Unity\bin` on Windows).
- CI runs the suite on **2021.3.45f2, 2022.3.62f3, 6000.0.67f1 and 6000.6.0f1** on every push, plus the
  four samples, in `.ci/compat-host`.

Two rules learned the hard way:

- A green run can be a lie: it may have run the previous assembly after a compile error. Check the
  console and the test count.
- Any test asserting "nothing was written because nothing changed" must let more than a second pass —
  the envelope's `$savedAt` has one-second resolution.

The IL2CPP stripping test on a real device is required for every release: Android, IL2CPP, managed
stripping High. The scenario lives in `Assets/ImplicitSaveSandbox/StrippingTest/`.

## Conventions

- Public API carries XML docs. The check is the compiler: build with `-doc` and expect zero CS1591.
- Code, comments and documentation are in English. The repository is public.
- `.meta` files are committed. Their GUIDs are what keep a buyer's scene references alive across
  updates, so they are never regenerated casually.
- Semantic versioning. The version lives in `Packages/com.bionics.implicitsave/package.json` and must
  match the git tag.

## Releasing

1. Bump `package.json`, write the `CHANGELOG.md` entry (there is one at the root and one in the
   package - keep them identical).
2. Tag `vX.Y.Z` and push. **OpenUPM publishes from the tag on its own.**
3. Asset Store: `python .ci/make-assetstore-copy.py --verify`. It builds a copy without `Tests`, with
   `Samples~` renamed to `Samples` (the `~` hides the folder from Unity, and inside `Assets/` that
   would leave the buyer with no samples), keeps every `.meta`, and refuses to finish if any of our
   test types would be visible to a buyer. Upload it by hand with the Asset Store Publishing Tools,
   with *Include Package Manifest* selecting only `com.unity.nuget.newtonsoft-json`.

An Asset Store package cannot install its own dependencies from `package.json` — that manifest option
is what brings Json.NET in.

## Traps worth knowing before they bite

- **Batch mode aborts on a broken `SaveRegistry.g.cs`.** The self-repair only runs in the interactive
  editor, so delete `Assets/ImplicitSave.Generated` before a headless run.
- **An embedded package compiles its tests even without `testables`.** A buyer installing through UPM
  sees none of our fixtures; one installing from the Asset Store would, which is why `Tests` is
  excluded from that copy.
- **A module removed in a newer Unity breaks package resolution before the editor opens**
  (`com.unity.modules.vr` is gone in 6.6). Module lists may only contain what exists in every
  supported version.
- **Test fixtures are found by the project scan.** A fixture with a colliding `[SaveId]`, or a test
  migration pointing at a real save type, poisons the registry for the whole project.

## Working with the author

The author reviews every file before committing, and creates the branches. Do not commit, push or tag
without being asked to.
