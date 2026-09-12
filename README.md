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

> **Status: 1.0.** The public API is stable, and breaking changes wait for a major version.

## What makes it different

1. **Zero-config discovery** — you never register a save type. Discovery happens through `TypeCache` in
   the editor and a generated registry in builds, so it survives IL2CPP stripping.
2. **Dictionaries that survive, on every version you support** — `SerializableDictionary<K,V>` and
   its drawer work from Unity 2021.3 on. Unity 6.6 added native `Dictionary` serialization, and
   ImplicitSave saves those fields too when it runs on 6.6+ - it follows the editor's own rules
   rather than a hard-coded list.
3. **First-class schema migration** — versioned save envelopes and a migration pipeline, for the pain
   that only shows up on your first patch in production.
4. **Polymorphism that survives class renames** — subtypes are persisted through a stable registry
   discriminator, never through .NET type names.

## Requirements

- Unity 2021.3 or newer — the full test suite runs on every push against 2021.3 LTS, 2022.3 LTS,
  Unity 6.0 LTS and Unity 6.6
- [`com.unity.nuget.newtonsoft-json`](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html) 3.2.2 (resolved automatically as a package dependency)

## Installation

**From this repository (works today).** In Unity, `Window > Package Manager`, then `+` >
*Add package from git URL*:

```
https://github.com/bionics07/Implicit-Save.git?path=/Packages/com.bionics.implicitsave
```

That tracks `main`. To pin a released version once tags exist, add `#v1.0.0` to the end. Json.NET is
declared as a dependency and resolves on its own through the Package Manager.

**Asset Store and OpenUPM:** not published yet. Both arrive with 1.0.

## Documentation

End-user documentation ships with the 1.0 release. Until then, the API surface is described in the
XML docs on the public types.

## Samples

Import them from the Package Manager: pick ImplicitSave, open **Samples**, press **Import**. Each one is
a scene to press Play on.

| Sample | What it shows |
|---|---|
| **Basic Usage** | One save class changed from buttons, written by autosave, and still there on the next Play. Includes the demo scene. |
| **Multi Profile** | Save slots: create, switch, copy, rename and delete, with the same save class stored once per slot. |
| **Polymorphism** | A list declared as a base type holding mixed subtypes, and the file that comes out of it. |
| **Migration** | Files written by older versions of a class, loaded through migration steps - and what happens with a file from a newer build. |

## Troubleshooting

Saves live in `<persistentDataPath>/<save folder>/<profile id>/<save id>.json`, in plain JSON. The folder
is set in **Project Settings > ImplicitSave**, and **Tools > ImplicitSave > Save Editor** opens the files
from inside the editor.

| Symptom | Likely cause | Fix |
|---|---|---|
| `The type 'JsonConvert' exists in both ...` | Json.NET is in the project twice - usually a loose DLL shipped by another asset alongside the UPM package | Keep `com.unity.nuget.newtonsoft-json` and remove the other copy. ImplicitSave never ships a Json.NET DLL of its own. |
| `The type or namespace name 'Newtonsoft' could not be found` | The dependency did not resolve | Install `com.unity.nuget.newtonsoft-json` from the Package Manager and let the project recompile |
| A save type works in the editor but is missing from a build | A build cannot scan the project, so it reads a generated registry that is out of date | `Tools > ImplicitSave > Regenerate Registry`, then build again. `Tools > ImplicitSave > Save Types` shows what was found and what a build would miss. |
| `SaveRegistry.g.cs` does not compile after a save class was deleted | The generated file still names the class that is gone | It repairs itself on the next compile. If it does not, delete `Assets/ImplicitSave.Generated` and regenerate. |
| A polymorphic value comes back `null`, with an error naming an id | The `[SaveType]` id changed, or that class was deleted | Add `PreviousIds = new[] { "old_id" }` to the `[SaveType]` on the class it became. Otherwise the value is dropped on purpose and the rest of the save still loads. |
| A field shows up in the editor but never reaches the file | The field is not something Unity serializes: a class without `[Serializable]`, a property, or a `Dictionary` before Unity 6.6 | Use `SerializableDictionary<K,V>`, add `[Serializable]`, or make it a field. `Tools > ImplicitSave > Validate Save Types` lists every case in the project. |
| A save refuses to be written, and `Failed` reports a future version | The file was written by a newer build of the game | Intentional. The newer file is never overwritten, so that progress survives a downgrade - check `IsReadOnly` before letting the player carry on. |
| Files stay behind after renaming a save class | The file is named after the save id, so the old name keeps its file | The Save Editor lists them under "no class for these", with a Delete on each row |
| Autosave never writes anything | The interval is zero or autosave is off | **Project Settings > ImplicitSave**. A tick that finds no change writes nothing, which is by design. |

Anything else, or any of these that does not match what you see: open an issue.

## Repository layout

This repository is the **Unity development project**. The distributable package is the embedded
package under [`Packages/com.bionics.implicitsave/`](Packages/com.bionics.implicitsave).

## License

Dual-licensed by distribution channel:

- **GitHub / OpenUPM** — [MIT](LICENSE).
- **Unity Asset Store** — the Unity Asset Store EULA, which is imposed by the Asset Store Provider
  Agreement and is not the publisher's choice.

Same code, same feature set, different license terms depending on where you got it.

## Donations

ImplicitSave is free, on every channel, with the same features everywhere. If it saves you time, donations
are accepted on [Ko-fi](https://ko-fi.com/bionics07) — they are what keeps the project maintained.

## Support

Support happens exclusively through [GitHub Issues](https://github.com/bionics07/Implicit-Save/issues).
Please open an issue rather than emailing — the history stays public and searchable, and the same
question gets answered once.
