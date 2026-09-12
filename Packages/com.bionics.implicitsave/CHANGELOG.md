# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Pre-1.0 development. Nothing released yet; this is what 1.0 will ship with.

### Added

- Zero-config discovery: a class deriving from `SaveData` is found and given a file with no registration.
  `TypeCache` in the editor, a generated registry in builds, so it survives IL2CPP managed stripping.
- `SaveManager` facade: read, write, autosave, flush on pause, focus loss and quit.
- Atomic writes with a `.bak` backup, and recovery from it when a file cannot be read.
- Profiles (save slots): create, switch, copy, rename, delete, with a rebuildable index.
- Schema migration: versioned envelopes and `ISaveMigration` steps applied in order. A file from a newer
  build is never overwritten.
- Polymorphic fields through `[SerializeReference]`, persisted by a `[SaveType]` id rather than a .NET
  type name, with `PreviousIds` for renaming an id.
- `SerializableDictionary<K,V>` and a drawer for it, since Unity's Inspector draws no dictionary.
- Unity's built-in structs (`Vector2/3/4`, `Quaternion`, `Color`, `Rect`, `Bounds`, `LayerMask`,
  `Hash128` and the rest) saved with the names their C# API uses.
- Save Editor window: inspect and edit saves, in Play Mode against the live instance.
- Validation of save classes on compile, and `Tools > ImplicitSave > Validate Save Types` on demand.
- Project Settings page under **ImplicitSave**, and an optional settings asset created from it.
- Four samples: Basic Usage (with the demo scene), Multi Profile, Polymorphism and Migration.

[Unreleased]: https://github.com/bionics07/Implicit-Save
