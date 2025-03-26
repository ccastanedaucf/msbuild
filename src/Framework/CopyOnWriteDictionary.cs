// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Collections
{
    internal abstract class CopyOnWriteDictionary<TValue> : IDictionary<string, TValue>, IDictionary, ISerializable
    {
        internal static CopyOnWriteDictionary<TValue> Create() =>
            new CopyOnWriteDictionary<TValue, TValue>(SimpleToBackingValue, SimpleFromBackingValue);

        internal static CopyOnWriteDictionary<TValue> Create(IEqualityComparer<string> comparer) =>
            new CopyOnWriteDictionary<TValue, TValue>(SimpleToBackingValue, SimpleFromBackingValue, comparer);

        internal static CopyOnWriteDictionary<TValue> Create(ImmutableDictionary<string, TValue> backing) =>
            new CopyOnWriteDictionary<TValue, TValue>(SimpleToBackingValue, SimpleFromBackingValue, backing);

        internal static CopyOnWriteDictionary<TValue> Create(IDictionary<string, TValue> backing) =>
            new CopyOnWriteDictionary<TValue, TValue>(SimpleToBackingValue, SimpleFromBackingValue, backing);

        private static TValue SimpleToBackingValue(string key, TValue value) => value;

        private static TValue SimpleFromBackingValue(TValue value) => value;

        /// <summary>
        /// Returns the collection of keys in the dictionary.
        /// </summary>
        public abstract ICollection<string> Keys { get; }

        /// <summary>
        /// Returns the collection of values in the dictionary.
        /// </summary>
        public abstract ICollection<TValue> Values { get; }

        /// <summary>
        /// Returns the number of items in the collection.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Returns true if the collection is read-only.
        /// </summary>
        public virtual bool IsReadOnly { get; }

        /// <summary>
        /// Comparer used for keys
        /// </summary>
        internal abstract IEqualityComparer<string> Comparer { get; }

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        bool IDictionary.IsFixedSize => false;

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        ICollection IDictionary.Keys => (ICollection)Keys;

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        ICollection IDictionary.Values => (ICollection)Values;

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        bool ICollection.IsSynchronized => false;

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        object ICollection.SyncRoot => this;

        /// <summary>
        /// Accesses the value for the specified key.
        /// </summary>
        public abstract TValue this[string key] { get; set; }

        /// <summary>
        /// IDictionary implementation
        /// </summary>
        object? IDictionary.this[object key]
        {
            get => this[(string)key];
            set => this[(string)key] = (TValue)value!;
        }

        /// <summary>
        /// Adds a value to the dictionary.
        /// </summary>
        public abstract void Add(string key, TValue value);

        /// <summary>
        /// Adds several value to the dictionary.
        /// </summary>
        public abstract void SetItems(IEnumerable<KeyValuePair<string, TValue>> items);

        public abstract IEnumerable<KeyValuePair<string, TValue>> Where(Func<KeyValuePair<string, TValue>, bool> predicate);

        /// <summary>
        /// Returns true if the dictionary contains the specified key.
        /// </summary>
        public abstract bool ContainsKey(string key);

        /// <summary>
        /// Removes the entry for the specified key from the dictionary.
        /// </summary>
        public abstract bool Remove(string key);

        /// <summary>
        /// Attempts to find the value for the specified key in the dictionary.
        /// </summary>
        public abstract bool TryGetValue(string key, out TValue value);

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        public abstract void Add(KeyValuePair<string, TValue> item);

        /// <summary>
        /// Clears the collection.
        /// </summary>
        public abstract void Clear();

        /// <summary>
        /// Returns true ff the collection contains the specified item.
        /// </summary>
        public abstract bool Contains(KeyValuePair<string, TValue> item);

        /// <summary>
        /// Copies all of the elements of the collection to the specified array.
        /// </summary>
        public abstract void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex);

        /// <summary>
        /// Remove an item from the dictionary.
        /// </summary>
        public abstract bool Remove(KeyValuePair<string, TValue> item);

        /// <summary>
        /// Implementation of generic IEnumerable.GetEnumerator()
        /// </summary>
        public abstract IEnumerator<KeyValuePair<string, TValue>> GetEnumerator();

        /// <summary>
        /// Implementation of IEnumerable.GetEnumerator()
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<string, TValue>>)this).GetEnumerator();
        }

#nullable disable
        /// <summary>
        /// IDictionary implementation.
        /// </summary>
        void IDictionary.Add(object key, object value) => Add((string)key, (TValue)value);
#nullable enable

        /// <summary>
        /// IDictionary implementation.
        /// </summary>
        bool IDictionary.Contains(object key) => ContainsKey((string)key);

        /// <summary>
        /// IDictionary implementation.
        /// </summary>
        IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)this).GetEnumerator();

        /// <summary>
        /// IDictionary implementation.
        /// </summary>
        void IDictionary.Remove(object key) => Remove((string)key);

        /// <summary>
        /// IDictionary implementation.
        /// </summary>
        void ICollection.CopyTo(Array array, int index)
        {
            int i = 0;
            foreach (KeyValuePair<string, TValue> entry in this)
            {
                array.SetValue(new DictionaryEntry(entry.Key, entry.Value), index + i);
                i++;
            }
        }

        /// <summary>
        /// Clone, with the actual clone deferred
        /// </summary>
        internal abstract CopyOnWriteDictionary<TValue> Clone();

        internal abstract CopyOnWriteDictionary<TValue> CloneEmpty();


        internal abstract CopyOnWriteDictionary<TValue> CloneFrom(CopyOnWriteDictionary<TValue> source);

        internal virtual bool IsBuilder { get; }

        internal abstract CopyOnWriteDictionary<TValue> ToBuilder();

        internal abstract CopyOnWriteDictionary<TValue> FromBuilder();

        public abstract void GetObjectData(SerializationInfo info, StreamingContext context);

        internal abstract bool HasSameBacking(CopyOnWriteDictionary<TValue> other);
    }

    public delegate B ToBackingValue<V, B>(string key, V value);

    public delegate V FromBackingValue<V, B>(B backingValue);

    internal class CopyOnWriteDictionary<TValue, TBacking> : CopyOnWriteDictionary<TValue>
    {
        /// <summary>
        /// Empty dictionary with a <see cref="MSBuildNameIgnoreCaseComparer" />,
        /// used as the basis of new dictionaries with that comparer to avoid
        /// allocating new comparers objects.
        /// </summary>
        private static readonly ImmutableDictionary<string, TBacking> NameComparerDictionaryPrototype = ImmutableDictionary.Create<string, TBacking>(MSBuildNameIgnoreCaseComparer.Default);

        /// <summary>
        /// Empty dictionary with <see cref="StringComparer.OrdinalIgnoreCase" />,
        /// used as the basis of new dictionaries with that comparer to avoid
        /// allocating new comparers objects.
        /// </summary>
        private static readonly ImmutableDictionary<string, TBacking> OrdinalIgnoreCaseComparerDictionaryPrototype = ImmutableDictionary.Create<string, TBacking>(StringComparer.OrdinalIgnoreCase);

        private readonly ToBackingValue<TValue, TBacking> _toBackingValue;

        private readonly FromBackingValue<TValue, TBacking> _fromBackingValue;

        /// <summary>
        /// The backing dictionary.
        /// Lazily created.
        /// </summary>
        private ImmutableDictionary<string, TBacking> _backing;

#if TASKHOST
        private IDictionary<string, TBacking> _builder;
#else
        private ImmutableDictionary<string, TBacking>.Builder? _builder;
#endif

        private ImmutableDictionary<string, TBacking>? _cacheBacking;

        private Dictionary<string, TValue>? _getCache;

        /// <summary>
        /// Constructor. Consider supplying a comparer instead.
        /// </summary>
        internal CopyOnWriteDictionary(
            ToBackingValue<TValue, TBacking> toBackingValue,
            FromBackingValue<TValue, TBacking> fromBackingValue)
            : this(toBackingValue, fromBackingValue, ImmutableDictionary<string, TBacking>.Empty)
        {
        }

        /// <summary>
        /// Constructor taking a specified comparer for the keys
        /// </summary>
        internal CopyOnWriteDictionary(
            ToBackingValue<TValue, TBacking> toBackingValue,
            FromBackingValue<TValue, TBacking> fromBackingValue,
            IEqualityComparer<string>? keyComparer)
            : this(toBackingValue, fromBackingValue, GetInitialDictionary(keyComparer))
        {
        }

        internal CopyOnWriteDictionary(
            ToBackingValue<TValue, TBacking> toBackingValue,
            FromBackingValue<TValue, TBacking> fromBackingValue,
            ImmutableDictionary<string, TBacking> backing)
        {
            _toBackingValue = toBackingValue;
            _fromBackingValue = fromBackingValue;
            _backing = backing ?? ImmutableDictionary<string, TBacking>.Empty;
        }

        internal CopyOnWriteDictionary(
            ToBackingValue<TValue, TBacking> toBackingValue,
            FromBackingValue<TValue, TBacking> fromBackingValue,
            IDictionary<string, TValue> backing)
        {
            _toBackingValue = toBackingValue;
            _fromBackingValue = fromBackingValue;

            if (backing is ImmutableDictionary<string, TBacking> immutableDictionary)
            {
                _backing = immutableDictionary;
                return;
            }

            var tempBacking = backing.ToImmutableDictionary();
            IEnumerable<KeyValuePair<string, TBacking>> enumerable = backing.Select(entry => new KeyValuePair<string, TBacking>(entry.Key, _toBackingValue(entry.Key, entry.Value)));
            _backing = GetInitialDictionary(tempBacking.KeyComparer);
            _backing = _backing.SetItems(enumerable);
        }

        /*
                /// <summary>
                /// Serialization constructor, for crossing appdomain boundaries
                /// </summary>
                [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "context", Justification = "Not needed")]
                protected CopyOnWriteDictionary(SerializationInfo info, StreamingContext context)
                {
                    object v = info.GetValue(nameof(_backing), typeof(KeyValuePair<string, TValue>[]))!;

                    object comparer = info.GetValue(nameof(Comparer), typeof(IEqualityComparer<string>))!;

                    ImmutableDictionary<string, TBacking> b = GetInitialDictionary((IEqualityComparer<string>?)comparer);

                    _backing = b.AddRange((KeyValuePair<string, TBacking>[])v);
                }
        */

        private static ImmutableDictionary<string, TBacking> GetInitialDictionary(IEqualityComparer<string>? keyComparer)
        {
            return keyComparer is MSBuildNameIgnoreCaseComparer
                            ? NameComparerDictionaryPrototype
                            : keyComparer == StringComparer.OrdinalIgnoreCase
                              ? OrdinalIgnoreCaseComparerDictionaryPrototype
                              : ImmutableDictionary.Create<string, TBacking>(keyComparer);
        }

        /// <summary>
        /// Cloning constructor. Defers the actual clone.
        /// </summary>
        private CopyOnWriteDictionary(
            ToBackingValue<TValue, TBacking> toBackingValue,
            FromBackingValue<TValue, TBacking> fromBackingValue,
            CopyOnWriteDictionary<TValue> that)
        {
            _toBackingValue = toBackingValue;
            _fromBackingValue = fromBackingValue;

            if (that is CopyOnWriteDictionary<TValue, TBacking> sameType)
            {
                _backing = sameType._backing;
            }
            else
            {
                _backing = GetInitialDictionary(that.Comparer);
                SetItems(that);
            }
        }

        // public CopyOnWriteDictionary(IDictionary<string, TValue> dictionary)
        // {
        //     _backing = dictionary.ToImmutableDictionary();
        // }

        /// <summary>
        /// Returns the collection of keys in the dictionary.
        /// </summary>
        public override ICollection<string> Keys => ((IDictionary<string, TBacking>)_backing).Keys;

        /// <summary>
        /// Returns the collection of values in the dictionary.
        /// </summary>
        public override ICollection<TValue> Values
        {
            get
            {
                int index = 0;
                TValue[] values = new TValue[_backing.Count];

                foreach (TBacking backingValue in _backing.Values)
                {
                    values[index] = _fromBackingValue(backingValue);
                }

                return values;
            }
        }

        /// <summary>
        /// Returns the number of items in the collection.
        /// </summary>
        public override int Count => _backing.Count;

        /// <summary>
        /// Returns true if the collection is read-only.
        /// </summary>
        public override bool IsReadOnly => ((IDictionary<string, TBacking>)_backing).IsReadOnly;

        /// <summary>
        /// Comparer used for keys
        /// </summary>
        internal override IEqualityComparer<string> Comparer => EqualityComparer;

        internal IEqualityComparer<string> EqualityComparer
        {
            get => _backing.KeyComparer;
            private set => _backing = _backing.WithComparers(keyComparer: value);
        }

        /// <summary>
        /// Accesses the value for the specified key.
        /// </summary>
        public override TValue this[string key]
        {
            get => _fromBackingValue(_backing[key]);

            set
            {
                _backing = _backing.SetItem(key, _toBackingValue(key, value));
            }
        }

        /// <summary>
        /// Adds a value to the dictionary.
        /// </summary>
        public override void Add(string key, TValue value)
        {
            TBacking backingValue = _toBackingValue(key, value);

            if (_builder != null)
            {
                _builder.Add(key, backingValue);
                return;
            }

            _backing = _backing.SetItem(key, backingValue);
        }

        /// <summary>
        /// Adds several value to the dictionary.
        /// </summary>
        public override void SetItems(IEnumerable<KeyValuePair<string, TValue>> items)
        {
            if (_builder != null)
            {
#if TASKHOST
                throw new NotImplementedException();
#else
                _builder.AddRange(EnumerateBackingValues(items, _toBackingValue));
#endif
                return;
            }

            _backing = _backing.SetItems(EnumerateBackingValues(items, _toBackingValue));

            static IEnumerable<KeyValuePair<string, TBacking>> EnumerateBackingValues(IEnumerable<KeyValuePair<string, TValue>> items, ToBackingValue<TValue, TBacking> toBackingValue)
            {
                foreach (KeyValuePair<string, TValue> item in items)
                {
                    yield return new KeyValuePair<string, TBacking>(item.Key, toBackingValue(item.Key, item.Value));
                }
            }
        }

        public override IEnumerable<KeyValuePair<string, TValue>> Where(Func<KeyValuePair<string, TValue>, bool> predicate)
        {
            if (_builder != null)
            {
                return WhereBackingValues(predicate, _builder, _fromBackingValue);
            }

            return WhereBackingValues(predicate, _backing, _fromBackingValue);

            static IEnumerable<KeyValuePair<string, TValue>> WhereBackingValues(Func<KeyValuePair<string, TValue>, bool> predicate, IEnumerable<KeyValuePair<string, TBacking>> backing, FromBackingValue<TValue, TBacking> fromBackingValue)
            {
                foreach (KeyValuePair<string, TBacking> backingItem in backing)
                {
                    KeyValuePair<string, TValue> item = new(backingItem.Key, fromBackingValue(backingItem.Value));
                    if (predicate(item))
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the dictionary contains the specified key.
        /// </summary>
        public override bool ContainsKey(string key)
        {
            if (_builder != null)
            {
                return _builder.ContainsKey(key);
            }

            return _backing.ContainsKey(key);
        }

        /// <summary>
        /// Removes the entry for the specified key from the dictionary.
        /// </summary>
        public override bool Remove(string key)
        {
            if (_builder != null)
            {
                return _builder.Remove(key);
            }

            ImmutableDictionary<string, TBacking> initial = _backing;

            _backing = _backing.Remove(key);

            return initial != _backing; // whether the removal occured
        }

#nullable disable
        /// <summary>
        /// Attempts to find the value for the specified key in the dictionary.
        /// </summary>
        public override bool TryGetValue(string key, out TValue value)
        {
            bool result = _builder == null
                ? _backing.TryGetValue(key, out TBacking backingValue)
                : _builder.TryGetValue(key, out backingValue);

            value = result && backingValue != null ? _fromBackingValue(backingValue) : default(TValue);
            return result;
        }
#nullable restore

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        public override void Add(KeyValuePair<string, TValue> item)
        {
            TBacking backingValue = _toBackingValue(item.Key, item.Value);

            if (_builder != null)
            {
                _builder.Add(item.Key, backingValue);
            }
            else
            {
                _backing = _backing.SetItem(item.Key, backingValue);
            }
        }

        /// <summary>
        /// Clears the collection.
        /// </summary>
        public override void Clear()
        {
            if (_builder != null)
            {
                _builder.Clear();
            }
            else
            {
                _backing = _backing.Clear();
            }
        }

        /// <summary>
        /// Returns true ff the collection contains the specified item.
        /// </summary>
        public override bool Contains(KeyValuePair<string, TValue> item)
        {
            KeyValuePair<string, TBacking> backingItem = new(item.Key, _toBackingValue(item.Key, item.Value));

            return _builder != null ? _builder.Contains(backingItem) : _backing.Contains(backingItem);
        }

        /// <summary>
        /// Copies all of the elements of the collection to the specified array.
        /// </summary>
        public override void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            if (_builder != null)
            {
                throw new NotImplementedException();
            }

            int index = arrayIndex;
            foreach (KeyValuePair<string, TBacking> item in _backing)
            {
                array[index] = new KeyValuePair<string, TValue>(item.Key, _fromBackingValue(item.Value));
                index++;
            }
        }

        /// <summary>
        /// Remove an item from the dictionary.
        /// </summary>
        public override bool Remove(KeyValuePair<string, TValue> item)
        {
            if (_builder != null)
            {
                return _builder.Remove(item.Key);
            }

            ImmutableDictionary<string, TBacking> initial = _backing;

            _backing = _backing.Remove(item.Key);

            return initial != _backing; // whether the removal occured
        }

        /// <summary>
        /// Implementation of generic IEnumerable.GetEnumerator().
        /// </summary>
        public override IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            if (_builder != null)
            {
                return EnumerateBackingValues(_builder, _fromBackingValue);
            }

            return EnumerateBackingValues(_backing, _fromBackingValue);

            static IEnumerator<KeyValuePair<string, TValue>> EnumerateBackingValues(IEnumerable<KeyValuePair<string, TBacking>> items, FromBackingValue<TValue, TBacking> fromBackingValue)
            {
                foreach (KeyValuePair<string, TBacking> item in items)
                {
                    yield return new KeyValuePair<string, TValue>(item.Key, fromBackingValue(item.Value));
                }
            }
        }

        /// <summary>
        /// Clone, with the actual clone deferred.
        /// </summary>
        internal override CopyOnWriteDictionary<TValue> Clone() => new CopyOnWriteDictionary<TValue, TBacking>(_toBackingValue, _fromBackingValue, this);

        internal override CopyOnWriteDictionary<TValue> CloneEmpty() =>
            new CopyOnWriteDictionary<TValue, TBacking>(_toBackingValue, _fromBackingValue, Comparer);

        internal override CopyOnWriteDictionary<TValue> CloneFrom(CopyOnWriteDictionary<TValue> source)
        {
            if (source is CopyOnWriteDictionary<TValue, TBacking> sameType)
            {
                return Clone();
            }

            return new CopyOnWriteDictionary<TValue, TBacking>(_toBackingValue, _fromBackingValue, source);
        }

        internal ImmutableDictionary<string, TBacking> ToImmutableDictionary() => _backing;

        internal override bool IsBuilder => _builder != null;

        internal override CopyOnWriteDictionary<TValue> ToBuilder()
        {
#if TASKHOST
            throw new NotImplementedException();
#else
            if (_builder != null)
            {
                throw new InvalidOperationException("already a builder oof");
            }

            CopyOnWriteDictionary<TValue, TBacking> builderDictionary = new(_toBackingValue, _fromBackingValue, this);
            builderDictionary._builder = _backing.ToBuilder();
            return builderDictionary;
#endif
        }

        internal override CopyOnWriteDictionary<TValue> FromBuilder()
        {
#if TASKHOST
            throw new NotImplementedException();
#else
            if (_builder == null)
            {
                throw new InvalidOperationException("not a builder oof");
            }

            return new CopyOnWriteDictionary<TValue, TBacking>(_toBackingValue, _fromBackingValue, _builder.ToImmutable());
#endif
        }

        /// <summary>
        /// Returns true if these dictionaries have the same backing.
        /// </summary>
        internal override bool HasSameBacking(CopyOnWriteDictionary<TValue> other) => other is CopyOnWriteDictionary<TValue, TBacking> sameType
                && ReferenceEquals(sameType._backing, _backing);

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ImmutableDictionary<string, TBacking> snapshot = _backing;
            KeyValuePair<string, TBacking>[] array = [.. snapshot];

            info.AddValue(nameof(_backing), array);
            info.AddValue(nameof(Comparer), Comparer);
        }
    }
}
