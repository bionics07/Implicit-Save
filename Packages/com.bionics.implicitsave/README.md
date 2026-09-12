# ImplicitSave

Zero-config save system for Unity. Declaring a class is enough — no central ScriptableObject, no manual
registration, no references to maintain.

```csharp
[SaveId("items")]
public class ItemsSaveData : SaveData
{
    public SerializableDictionary<string, int> Inventory = new();
    public int SelectedSlot;
}

var items = SaveManager.Get<ItemsSaveData>();
items.Inventory["potion"] = 5;
```

> Status: 1.0. The public API is stable, and breaking changes wait for a major version.

## Requirements

- Unity 2021.3 or newer
- `com.unity.nuget.newtonsoft-json` 3.2.2 (resolved automatically)

## Where things are

- **Tools > ImplicitSave > Save Editor** - inspect and edit saves, including while the game runs.
- **Tools > ImplicitSave > Save Types** - every save type found, and what each is called on disk.
- **Tools > ImplicitSave > Validate Save Types** - fields that would never reach the file, and why.
- **Project Settings > ImplicitSave** - autosave interval, backups, save folder. Optional: without the
  settings asset the package runs on its defaults.
- Save files: `<persistentDataPath>/<save folder>/<profile id>/<save id>.json`, in plain JSON.

## Samples

In the Package Manager, open **Samples** on this package and import any of them. Each is a scene to press
Play on: Basic Usage (with the demo scene), Multi Profile, Polymorphism and Migration.

## Troubleshooting

| Symptom | Fix |
|---|---|
| A `Dictionary` field is not saved | Before Unity 6.6 Unity serializes no dictionary: use `SerializableDictionary<K,V>`. On 6.6+ the field also needs `[SerializeField]`, even when it is public. |
| `The type 'JsonConvert' exists in both ...` | Json.NET is in the project twice. Keep `com.unity.nuget.newtonsoft-json` and remove the loose DLL - this package never ships one. |
| `The type or namespace name 'Newtonsoft' could not be found` | Install `com.unity.nuget.newtonsoft-json` from the Package Manager. |
| A save type is missing from a build but fine in the editor | `Tools > ImplicitSave > Regenerate Registry`, then build again. A build cannot scan the project. |
| A field shows in the editor but never reaches the file | It is not a type Unity serializes. Run `Validate Save Types` - it names each one and what to do. |

The full list, with the rest of the cases, is in the repository README.

## Documentation, source and issues

https://github.com/bionics07/Implicit-Save

## License

See `LICENSE.md`. Copies obtained through the Unity Asset Store are governed by the Unity Asset Store
EULA instead.
