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
        /// Whether the dictionary holds changes the backing lists have not seen yet.
        /// </summary>
        /// <remarks>
        /// Both halves can be written to - game code goes through the dictionary, the Inspector goes
        /// straight to the lists - and whichever is flattened last wins. Without knowing which one
        /// moved, editing in the Inspector was undone on the next frame: a repeated key was dropped
        /// while being typed and its row vanished before it could be corrected.
        /// </remarks>
        [NonSerialized] private bool _dictionaryIsAhead;

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

            _dictionaryIsAhead = true;
        }

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                _dictionary[key] = value;
                _dictionaryIsAhead = true;
            }
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
            _dictionaryIsAhead = true;
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            _dictionary.Add(item.Key, item.Value);
            _dictionaryIsAhead = true;
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
            _dictionaryIsAhead = true;
            return _dictionary.Remove(key);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            _dictionaryIsAhead = true;
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
            _dictionaryIsAhead = true;
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
        /// <remarks>
        /// Only when the dictionary actually moved. Flattening unconditionally would overwrite what
        /// the Inspector is holding on every frame, which is what made a half-typed duplicate key
        /// disappear instead of being reported.
        /// </remarks>
        public void OnBeforeSerialize()
        {
            if (!_dictionaryIsAhead)
            {
                return;
            }

            _keys.Clear();
            _values.Clear();

            foreach (var pair in _dictionary)
            {
                _keys.Add(pair.Key);
                _values.Add(pair.Value);
            }

            _dictionaryIsAhead = false;
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
            _dictionaryIsAhead = false;

            var count = Math.Min(_keys.Count, _values.Count);

            for (var i = 0; i < count; i++)
            {
                var key = _keys[i];

                if (key == null)
                {
                    HasDuplicateKeys = true;
                    continue;
                }

                if (_dictionary.ContainsKey(key))
                {
                    // The lists are left exactly as they are: while someone is typing a key in the
                    // Inspector, a repeated value is a normal intermediate state, and deleting their
                    // row would be worse than showing it in red.
                    HasDuplicateKeys = true;
                    continue;
                }

                _dictionary.Add(key, _values[i]);
            }
        }
    }
}
