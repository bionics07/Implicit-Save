using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// One field per rule of Unity's serializable subset. The parity test walks this type and
    /// asserts Unity and Newtonsoft agree on every line of it.
    /// </summary>
    [SaveId("contract")]
    public class ContractSaveData : SaveData
    {
        public int PublicField;
        public string PublicString;
        public List<int> PublicList = new List<int>();
        [SerializeField] private int _privateWithAttribute;
        [SerializeField] private string _privateStringWithAttribute;
        public NestedStats Nested = new NestedStats();

        // None of the following are serialized by Unity, so none may reach the file either.
        private int _privateWithoutAttribute;
        [NonSerialized] public int NonSerializedField;
        public readonly int ReadOnlyField;
        public static int StaticField;
        public int WritableProperty { get; set; }
        public int ComputedProperty => PublicField * 2;

        /// <summary>Lets a test set the private fields without reflection.</summary>
        public void SetPrivateValues(int number, string text)
        {
            _privateWithAttribute = number;
            _privateStringWithAttribute = text;
        }

        /// <summary>Lets a test read the private fields back.</summary>
        public void GetPrivateValues(out int number, out string text)
        {
            number = _privateWithAttribute;
            text = _privateStringWithAttribute;
        }

        /// <summary>Lets a test confirm the untouched private field really was skipped.</summary>
        public int ReadPrivateWithoutAttribute()
        {
            return _privateWithoutAttribute;
        }

        /// <summary>Lets a test give the skipped field a value before serializing.</summary>
        public void SetPrivateWithoutAttribute(int value)
        {
            _privateWithoutAttribute = value;
        }
    }

    /// <summary>Dictionaries of several key types, which is where culture and parsing bite.</summary>
    [SaveId("inventory")]
    public class InventorySaveData : SaveData
    {
        public SerializableDictionary<string, int> Counts = new SerializableDictionary<string, int>();
        public SerializableDictionary<int, string> ById = new SerializableDictionary<int, string>();
        public SerializableDictionary<ItemRarity, int> ByRarity = new SerializableDictionary<ItemRarity, int>();
        public SerializableDictionary<string, NestedStats> Nested = new SerializableDictionary<string, NestedStats>();
    }

    /// <summary>Enum keys have to survive renumbering, so they are written by name.</summary>
    public enum ItemRarity
    {
        Common = 0,
        Rare = 5,
        Legendary = 9
    }

    /// <summary>Uses a plain Dictionary, which Unity drops - the validator must say so.</summary>
    public class PlainDictionarySaveData : SaveData
    {
        public Dictionary<string, int> Broken = new Dictionary<string, int>();
    }

    /// <summary>Marks a field [JsonIgnore] without [NonSerialized] - the one real divergence.</summary>
    public class JsonIgnoreSaveData : SaveData
    {
        [JsonIgnore] public int Ignored;
    }

    /// <summary>Nested nine levels deep, past the point where Unity quietly stops.</summary>
    [SaveId("deep")]
    public class DeepSaveData : SaveData
    {
        public Depth1 Value = new Depth1();
    }

    [Serializable] public class Depth1 { public Depth2 Value = new Depth2(); }
    [Serializable] public class Depth2 { public Depth3 Value = new Depth3(); }
    [Serializable] public class Depth3 { public Depth4 Value = new Depth4(); }
    [Serializable] public class Depth4 { public Depth5 Value = new Depth5(); }
    [Serializable] public class Depth5 { public Depth6 Value = new Depth6(); }
    [Serializable] public class Depth6 { public Depth7 Value = new Depth7(); }
    [Serializable] public class Depth7 { public Depth8 Value = new Depth8(); }
    [Serializable] public class Depth8 { public Depth9 Value = new Depth9(); }
    [Serializable] public class Depth9 { public int Leaf; }
}
