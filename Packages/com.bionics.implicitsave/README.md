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

> Status: pre-1.0, under active development. The API is not stable yet.

## Requirements

- Unity 2021.3 or newer
- `com.unity.nuget.newtonsoft-json` 3.2.2 (resolved automatically)

## Documentation, source and issues

https://github.com/bionics07/Implicit-Save

## License

See `LICENSE.md`. Copies obtained through the Unity Asset Store are governed by the Unity Asset Store
EULA instead.
