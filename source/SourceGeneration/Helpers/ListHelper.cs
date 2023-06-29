using System;
using System.Collections.Generic;
using SourceGeneration.Helpers;

namespace AutoConstructor.SourceGenerator;

public readonly struct RentedList<T> : IDisposable
{
    private readonly List<T> _list;
    public RentedList(List<T> list) => _list = list;
    public void Dispose()
    {
        _list.Clear();
        L<T>.Pool.Free(_list);
    }
    public void Add(T item) => _list.Add(item);
    public int Count => _list.Count;
    public T this[int index]
    {
        get => _list[index];
        set => _list[index] = value;
    }
    public IEnumerable<T> Enumerable => _list;
    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
    public void AddRange(IEnumerable<T> range) => _list.AddRange(range);
}

public static class ListHelper
{
    public static RentedList<T> Rent<T>(int size = 4)
    {
        var list = L<T>.Pool.Allocate();
        list.Clear();
        list.Capacity = Math.Max(list.Capacity, size);
        return new RentedList<T>(list);
    }
}

static file class L<T>
{
    public static readonly ObjectPool<List<T>> Pool = new(() => new(0));
}
