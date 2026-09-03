using System.Collections;

namespace kv_store.Implementations;

class QuadNode<TKey, TValue>(TKey key, TValue value, int level)
    where TKey : IComparable<TKey>
{
    public TKey? Key { get; set; } = key;
    public TValue? Value { get; set; } = value;
    public readonly QuadNode<TKey, TValue>?[] ForwardPointers = new QuadNode<TKey, TValue>?[level];
}

public class SkipList<TKey, TValue>(int MaxLevel = 20) : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    readonly int _maxLevel = MaxLevel;
    readonly QuadNode<TKey, TValue> _head = new(default!, default!, MaxLevel);
    private readonly Random random = new();
    private bool Flip => random.NextDouble() > 0.5;

    public bool Insert(TKey key, TValue value)
    {
        if (!TryGetNode(key, out var existing, out var update))
        {
            int level = 0;
            while (Flip)
                ++level;
            level = level < _maxLevel ? level : _maxLevel;

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

    public bool TryGetValue(TKey key, out TValue? value)
    {
        var ret = TryGetNode(key, out var currentNode, out var _);
        value = currentNode == null ? default : currentNode.Value;
        return ret;
    }

    public bool Delete(TKey key)
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
        bool found = FindFirstAtOrAfter(startKey, out var node);
        if (!found)
            return [];
        List<KeyValuePair<TKey, TValue>> retList = [];
        while (node != null && node.Key!.CompareTo(endKey) <= 0)
        {
            retList.Add(new(node.Key, node.Value!));
            node = node.ForwardPointers[0];
        }
        return retList;
    }

    bool FindFirstAtOrAfter(TKey key, out QuadNode<TKey, TValue>? outNode)
    {
        var current = _head;

        for (int i = _maxLevel - 1; i >= 0; i--)
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

    bool TryGetNode(
        TKey key,
        out QuadNode<TKey, TValue>? currentNode,
        out QuadNode<TKey, TValue>[] update
    )
    {
        var current = _head;
        update = new QuadNode<TKey, TValue>[_maxLevel];
        currentNode = null;

        for (int i = _maxLevel - 1; i >= 0; i--)
        {
            while (
                current!.ForwardPointers[i] != null
                && key.CompareTo(current.ForwardPointers[i]!.Key) > 0
            )
            {
                current = current.ForwardPointers[i];
            }

            update[i] = current;

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

    public void Clear()
    {
        Array.Clear(_head.ForwardPointers);
        Count = 0;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
