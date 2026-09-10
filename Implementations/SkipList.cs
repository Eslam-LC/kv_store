using System.Collections;
using kv_store.Enums;

namespace kv_store.Implementations;

class QuadNode<TKey, TValue>(TKey key, TValue value, int level)
    where TKey : IComparable<TKey>
{
    public TKey? Key { get; set; } = key;
    public TValue? Value { get; set; } = value;
    public readonly QuadNode<TKey, TValue>?[] ForwardPointers = new QuadNode<TKey, TValue>?[level];
}

public class SkipList<TKey, TValue>(int MaxLevel = 20)
    : IEnumerable<KeyValuePair<TKey, TValue>>,
        ICollection<KeyValuePair<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    readonly QuadNode<TKey, TValue> _head = new(default!, default!, MaxLevel);
    private readonly Random random = new();
    private bool Flip => random.NextDouble() > 0.5;

    public SkipList(IDictionary<TKey, TValue> dict, int MaxLevel = 20)
        : this(MaxLevel)
    {
        foreach (var item in dict)
        {
            Add(item.Key, item.Value);
        }
    }

    public SkipList(IEnumerable<KeyValuePair<TKey, TValue>> list, int MaxLevel = 20)
        : this(MaxLevel)
    {
        foreach (var item in list)
        {
            Add(item.Key, item.Value);
        }
    }

    public bool Add(TKey key, TValue value)
    {
        if (key == null)
            return false;
        if (!TryGetNode(key, out var existing, out var update))
        {
            int level = 1;
            while (Flip)
                ++level;
            level = level < MaxLevel ? level : MaxLevel;

            QuadNode<TKey, TValue> newNode = new(key, value, level);
            for (int i = 0; i < level; i++)
            {
                newNode.ForwardPointers[i] = update[i].ForwardPointers[i];
                update[i].ForwardPointers[i] = newNode;
            }
            ++Count;
            return true;
        }
        existing!.Value = value;
        return true;
    }

    public void Add(KeyValuePair<TKey, TValue> keyValue) => Add(keyValue.Key, keyValue.Value);

    public bool TryGetValue(TKey key, out TValue? value)
    {
        var ret = TryGetNode(key, out var currentNode, out var _, false);
        value = currentNode == null ? default : currentNode.Value;
        return ret;
    }

    public bool Contains(KeyValuePair<TKey, TValue> keyValue) =>
        TryGetValue(keyValue.Key, out var _);

    public bool Remove(TKey key)
    {
        if (TryGetNode(key, out var currentNode, out var update))
        {
            for (int i = 0; i < currentNode?.ForwardPointers.Length; i++)
            {
                update[i].ForwardPointers[i] = currentNode.ForwardPointers[i];
            }
            --Count;
            return true;
        }
        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> keyValue) => Remove(keyValue.Key);

    public bool SetDefault(TKey key)
    {
        if (TryGetNode(key, out var currentNode, out var _, false))
        {
            if (currentNode == null)
                return false;

            currentNode.Value = default;
            return true;
        }
        return false;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        var current = _head.ForwardPointers[0];
        while (current != null)
        {
            yield return new(current.Key!, current.Value!);
            current = current.ForwardPointers[0];
        }
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> Scan(TKey startKey, TKey endKey)
    {
        bool foundStartKey = FindFirstAtOrAfter(startKey, out var node);
        if (!foundStartKey)
            return [];
        List<KeyValuePair<TKey, TValue>> retList = [];
        while (node != null && node.Key!.CompareTo(endKey) <= 0)
        {
            retList.Add(new(node.Key, node.Value!));
            node = node.ForwardPointers[0];
        }
        return retList;
    }

    public TValue? this[TKey key]
    {
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            return default;
        }
        set { var _ = Add(key, value!); }
    }

    bool FindFirstAtOrAfter(TKey key, out QuadNode<TKey, TValue>? outNode)
    {
        var current = _head;

        for (int i = MaxLevel - 1; i >= 0; i--)
        {
            while (
                current!.ForwardPointers[i] != null
                && key.CompareTo(current.ForwardPointers[i]!.Key) > 0
            )
            {
                current = current.ForwardPointers[i];
            }
        }

        outNode = current.ForwardPointers[0];
        return outNode != null;
    }

    public bool GetValueAtOrBefore(TKey key, out TValue? outValue)
    {
        var current = _head;

        for (int i = MaxLevel - 1; i >= 0; i--)
        {
            while (
                current!.ForwardPointers[i] != null
                && key.CompareTo(current.ForwardPointers[i]!.Key) >= 0
            )
            {
                current = current.ForwardPointers[i];
            }
        }

        outValue = current.Value;
        return outValue != null;
    }

    bool TryGetNode(
        TKey key,
        out QuadNode<TKey, TValue>? currentNode,
        out QuadNode<TKey, TValue>[] update,
        bool collectUpdate = true
    )
    {
        var current = _head;
        if (collectUpdate)
            update = new QuadNode<TKey, TValue>[MaxLevel];
        else
            update = null!;
        currentNode = null;

        for (int i = MaxLevel - 1; i >= 0; i--)
        {
            while (
                current!.ForwardPointers[i] != null
                && key.CompareTo(current.ForwardPointers[i]!.Key) > 0
            )
            {
                current = current.ForwardPointers[i];
            }

            update?[i] = current;

            if (
                currentNode == null
                && current.ForwardPointers[i] != null
                && key.CompareTo(current.ForwardPointers[i]!.Key) == 0
            )
            {
                currentNode = current.ForwardPointers[i];
            }
        }

        return currentNode != null;
    }

    public int Count { get; private set; }
    public bool IsReadOnly => false;

    public void Clear()
    {
        Array.Clear(_head.ForwardPointers);
        Count = 0;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (arrayIndex + Count > array.Length)
            throw new ArgumentException("CopyTo destination is not big enough", nameof(array));

        int i = arrayIndex;
        foreach (var item in this)
        {
            array[i++] = item;
        }
    }
}

public class ImmutableSkipList<TKey, TValue>(SkipList<TKey, TValue> pairs) : IEnumerable
    where TKey : IComparable<TKey>
{
    public ImmutableSkipList()
        : this(new SkipList<TKey, TValue>()) { }

    public ImmutableSkipList(IEnumerable<KeyValuePair<TKey, TValue>> keyValues)
        : this(new SkipList<TKey, TValue>(keyValues)) { }

    public ImmutableSkipList(IDictionary<TKey, TValue> keyValues)
        : this(new SkipList<TKey, TValue>(keyValues)) { }

    public bool TryGetValue(TKey key, out TValue? value) => pairs.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => pairs.GetEnumerator();

    public IEnumerable<KeyValuePair<TKey, TValue>> Scan(TKey startKey, TKey endKey) =>
        pairs.Scan(startKey, endKey);

    public TValue? this[TKey key]
    {
        get
        {
            if (pairs.TryGetValue(key, out var value))
            {
                return value;
            }
            return default;
        }
    }

    public bool GetValueAtOrBefore(TKey key, out TValue? outValue) =>
        pairs.GetValueAtOrBefore(key, out outValue);

    public int Count => pairs.Count;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
