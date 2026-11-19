using System.Collections;

namespace Caravan.Support;

/// <summary>
/// A thread-safe list for rarely changed, often enumerated collections (event notifiers): writers copy the items, enumeration works on a
/// snapshot and never fails because of a concurrent change.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class CopyOnWriteList<T> : IList<T>
{
    private readonly Lock _lock = new();
    private volatile T[] _items = [];

    public int Count => _items.Length;

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => _items[index];
        set => Mutate(list => list[index] = value);
    }

    public void Add(T item) => Mutate(list => list.Add(item));

    public void Clear() => Mutate(list => list.Clear());

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public int IndexOf(T item) => Array.IndexOf(_items, item);

    public void Insert(int index, T item) => Mutate(list => list.Insert(index, item));

    public bool Remove(T item)
    {
        var removed = false;
        Mutate(list => removed = list.Remove(item));
        return removed;
    }

    public void RemoveAt(int index) => Mutate(list => list.RemoveAt(index));

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Mutate(Action<List<T>> change)
    {
        lock (_lock)
        {
            var list = new List<T>(_items);
            change(list);
            _items = [.. list];
        }
    }
}
