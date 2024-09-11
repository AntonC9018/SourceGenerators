using System.Buffers;
using System;

namespace SourceGeneration.Helpers;

public readonly record struct RentedArray<T> : IDisposable
{
    private readonly T[] _array;
    private readonly int _count;
    public Memory<T> Memory => _array.AsMemory(0, _count);
    public Span<T> Span => _array.AsSpan(0, _count);

    private RentedArray(T[] array, int count)
    {
        _array = array;
        _count = count;
    }

    internal static RentedArray<T> Create(int count)
    {
        var arr = ArrayPool<T>.Shared.Rent(count);
        return new RentedArray<T>(arr, count);
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_array);
    }
}

public static class RentedArray
{
    public static RentedArray<T> Rent<T>(int length)
    {
        return RentedArray<T>.Create(length);
    }
}
