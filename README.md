# ImplicitSave — Zero-Config Save System

[![Tests](https://github.com/bionics07/Implicit-Save/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/bionics07/Implicit-Save/actions/workflows/tests.yml)

A save system for Unity where **declaring a class is enough**. No central ScriptableObject, no manual
registration, no references to maintain.

```csharp
// This is all you write:
[SaveId("items")]
public class ItemsSaveData : SaveData
{
    public SerializableDictionary<string, int> Inventory = new();
    public int SelectedSlot;
}

// And use it anywhere:
var items = SaveManager.Get<ItemsSaveData>();
items.Inventory["potion"] = 5;
```

No `AddComponent`, no dragging references, no central file to edit.

> ⚠️ **Status: pre-1.0, under active development.** Nothing is released yet and the API is not stable.

## What makes it different

1. **Zero-config discovery** — you never register a save type. Discovery happens through `TypeCache` in
   the editor and a generated registry in builds, so it survives IL2CPP stripping.
2. **An editor that actually handles `Dictionary`** — Unity's native Inspector does not draw
   dictionaries. ImplicitSave ships `SerializableDictionary<K,V>` and a drawer for it.
3. **First-class schema migration** — versioned save envelopes and a migration pipeline, for the pain
   that only shows up on your first patch in production.
4. **Polymorphism that survives class renames** — subtypes are persisted through a stable registry
   discriminator, never through .NET type names.

## Requirements

- Unity 2021.3 or newer — the full test suite runs on every push against 2021.3 LTS, 2022.3 LTS and
  Unity 6.0 LTS
- [`com.unity.nuget.newtonsoft-json`](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html) 3.2.2 (resolved automatically as a package dependency)

## Installation

Not published yet. Once released it will be available through the Unity Asset Store, OpenUPM, and as a
UPM git dependency.

## Documentation

End-user documentation ships with the 1.0 release. Until then, the API surface is described in the
XML docs on the public types.

## Repository layout

This repository is the **Unity development project**. The distributable package is the embedded
package under [`Packages/com.bionics.implicitsave/`](Packages/com.bionics.implicitsave).

## License

Dual-licensed by distribution channel:

- **GitHub / OpenUPM** — [MIT](LICENSE).
- **Unity Asset Store** — the Unity Asset Store EULA, which is imposed by the Asset Store Provider
  Agreement and is not the publisher's choice.

Same code, same feature set, different license terms depending on where you got it.

## Support

Support happens exclusively through [GitHub Issues](https://github.com/bionics07/Implicit-Save/issues).
Please open an issue rather than emailing — the history stays public and searchable, and the same
question gets answered once.
