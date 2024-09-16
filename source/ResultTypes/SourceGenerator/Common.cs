using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ResultTypes.SourceGenerator;

internal enum OverloadTag
{
    Ok,
    Failure,
    Count,

    _Start = Ok,
    _End = Failure,
}

internal static class OneForEachOverloadSetHelper
{
    public static ref T Ref<T>(this ref OneForEachOverloadSet<T> val, OverloadTag overloadTag)
    {
        switch (overloadTag)
        {
            case OverloadTag.Ok:
                return ref val.Ok;
            case OverloadTag.Failure:
                return ref val.Failure;
            default:
                throw new ArgumentOutOfRangeException(nameof(overloadTag));
        }
    }

    public static T Get<T>(this in OneForEachOverloadSet<T> val, OverloadTag overloadTag)
    {
        switch (overloadTag)
        {
            case OverloadTag.Ok:
                return val.Ok;
            case OverloadTag.Failure:
                return val.Failure;
            default:
                throw new ArgumentOutOfRangeException(nameof(overloadTag));
        }
    }
}

internal struct OneForEachOverloadSet<T>
{
    public required T Ok;
    public required T Failure;

    public TState Reduce<TState>(Func<TState, T, TState> f, TState initialState)
    {
        var s = initialState;
        for (var i = OverloadTag._Start; i <= OverloadTag._End; i++)
        {
            s = f(s, this.Get(i));
        }
        return s;
    }
}

internal struct OverloadSetInfoAccessor
{
    private readonly AllOverloadsContext All;
    public readonly OverloadTag Tag;

    public OverloadSetInfoAccessor(AllOverloadsContext all, OverloadTag tag)
    {
        All = all;
        Tag = tag;
    }

    public bool IncludeExceptionParameter
    {
        get
        {
            return Tag == OverloadTag.Failure;
        }
    }

    public Model.OverloadSet Model => All.Model.OverloadsSets.Get(Tag);
    public string DefaultPrefix => All.DefaultPrefixes.Ref(Tag);
    public Span<string?> PayloadNames => All.PayloadNames.Array(All.Model, Tag);
    public Span<string> ResultPayloadNames => All.ResultPayloadNames.Array(All.Model, Tag);
    public string DefaultTag => All.DefaultTags.Ref(Tag);
}

internal sealed class AllOverloadsContext
{
    public required Model Model;
    public required SharedArrayForEachOverloadSet<string?> PayloadNames;
    public required SharedArrayForEachOverloadSet<string> ResultPayloadNames;
    public required OneForEachOverloadSet<string> DefaultPrefixes;
    public required OneForEachOverloadSet<string> DefaultTags;

    public OverloadSetInfoAccessor For(OverloadTag tag)
    {
        return new(this, tag);
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly AllOverloadsContext _context;
        private OverloadTag _current;

        public Enumerator(AllOverloadsContext context)
        {
            _context = context;
            _current = OverloadTag._Start - 1;
        }

        public OverloadSetInfoAccessor Current => _context.For(_current);

        public bool MoveNext()
        {
            _current++;
            return _current <= OverloadTag._End;
        }
    }
}

internal readonly struct SharedArrayForEachOverloadSet<T> : IDisposable
{
    private readonly T[] _underlyingMemory;
    private readonly Func<Model, OverloadTag, int> _getLength;

    public SharedArrayForEachOverloadSet(Model model, Func<Model, OverloadTag, int> getLength)
    {
        int len = 0;
        for (var t = OverloadTag._Start; t <= OverloadTag._End; t++)
        {
            len += getLength(model, t);
        }
        _getLength = getLength;
        _underlyingMemory = ArrayPool<T>.Shared.Rent(len);
    }

    public ArraySegment<T> Array(Model model, OverloadTag tag)
    {
        var start = tag switch
        {
            OverloadTag.Ok => 0,
            OverloadTag.Failure => _getLength(model, OverloadTag.Ok),
            _ => throw new ArgumentOutOfRangeException(nameof(tag)),
        };
        var len = tag switch
        {
            OverloadTag.Ok => _getLength(model, OverloadTag.Ok),
            OverloadTag.Failure => _getLength(model, OverloadTag.Failure),
            _ => throw new ArgumentOutOfRangeException(nameof(tag)),
        };
        return new(_underlyingMemory, start, len);
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(_underlyingMemory!);
    }
}
