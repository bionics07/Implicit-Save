using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave
{
    /// <summary>
    /// A dictionary Unity can serialize and the Inspector can draw. Use it in save data wherever you
    /// would reach for <c>Dictionary&lt;TKey, TValue&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Unity's serializer has no notion of a dictionary, so a plain <c>Dictionary</c> field is
    /// silently dropped - it survives play mode and vanishes on reload. This type keeps two parallel
    /// lists that Unity does understand, and rebuilds the dictionary from them on load.
    /// <para>
    /// On disk it does not look like two lists: a converter writes it as an ordinary JSON object,
    /// <c>{"potion": 5}</c>, so save files stay readable.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">Key type. Must be serializable by Unity.</typeparam>
    /// <typeparam name="TValue">Value type. Must be serializable by Unity.</typeparam>
    /// <example>
    /// <code>
    /// [SaveId("items")]
    /// public class ItemsSaveData : SaveData
    /// {
    ///     public SerializableDictionary&lt;string, int&gt; Inventory = new SerializableDictionary&lt;string, int&gt;();
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public class SerializableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<TKey> _keys = new List<TKey>();
        [SerializeField] private List<TValue> _values = new List<TValue>();

        [NonSerialized] private Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();

        /// <summary>
        /// Whether the last load found repeated keys. The lists allow duplicates and a dictionary
        /// cannot, so the drawer uses this to point at the problem instead of hiding it.
        /// </summary>
        [NonSerialized] public bool HasDuplicateKeys;

        /// <summary>Creates an empty dictionary.</summary>
        public SerializableDictionary()
        {
        }

        /// <summary>Creates a dictionary holding a copy of <paramref name="source"/>.</summary>
        public SerializableDictionary(IDictionary<TKey, TValue> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            foreach (var pair in source)
            {
                _dictionary[pair.Key] = pair.Value;
            }
        }

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set => _dictionary[key] = value;
        }

        /// <inheritdoc />
        public ICollection<TKey> Keys => _dictionary.Keys;

        /// <inheritdoc />
        public ICollection<TValue> Values => _dictionary.Values;

        /// <inheritdoc />
        public int Count => _dictionary.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            _dictionary.Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Contains(item);
        }

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            return _dictionary.Remove(key);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Remove(item);
        }

        /// <inheritdoc />
        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _dictionary.Clear();
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Flattens the dictionary into the two lists Unity persists.</summary>
        public void OnBeforeSerialize()
        {
            _keys.Clear();
            _values.Clear();

            foreach (var pair in _dictionary)
            {
                _keys.Add(pair.Key);
                _values.Add(pair.Value);
            }
        }

        /// <summary>Rebuilds the dictionary from the two lists.</summary>
        /// <remarks>
        /// This must never throw. Unity calls it while loading, and an exception here would take the
        /// whole load down - so a repeated or null key is reported and skipped, keeping the first
        /// occurrence, rather than being treated as fatal.
        /// </remarks>
        public void OnAfterDeserialize()
        {
            _dictionary.Clear();
            HasDuplicateKeys = false;

            var count = Math.Min(_keys.Count, _values.Count);

            for (var i = 0; i < count; i++)
            {
                var key = _keys[i];

                if (key == null)
                {
                    ImplicitSaveLog.Warning(
                        $"SerializableDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> has a null key at " +
                        $"index {i}. The entry was dropped.");
                    continue;
                }

                if (_dictionary.ContainsKey(key))
                {
                    HasDuplicateKeys = true;
                    ImplicitSaveLog.Warning(
                        $"SerializableDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> has the key '{key}' " +
                        "more than once. The first one was kept.");
                    continue;
                }

                _dictionary.Add(key, _values[i]);
            }
        }
    }
}
