using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal sealed class TagInfo()
{
    public HashSet<string> Values { get; } = [];
    public HashSet<string>? OkValues { get; set; }
    public bool IsEnum { get; init; }
    public string? BaseSet { get; init; }
    public required string ShortName { get; init; }
}

internal readonly struct TagsKvpWrapper
{
    private readonly KeyValuePair<string, TagInfo> _kvp;
    public TagsKvpWrapper(KeyValuePair<string, TagInfo> kvp)
    {
        _kvp = kvp;
    }

    public string QualifiedName => _kvp.Key;
    public TagInfo Info => _kvp.Value;
    public HashSet<string> Values => Info.Values;
    public HashSet<string>? OkValues => Info.OkValues;
    public bool IsEnum => Info.IsEnum;
    public string? BaseSet => Info.BaseSet;
    public string ShortName => Info.ShortName;
}

internal readonly struct WriteTagsContext : IEnumerable<TagsKvpWrapper>, IDisposable
{
    private readonly Dictionary<string, TagInfo> _storage;
    private WriteTagsContext(Dictionary<string, TagInfo> storage)
    {
        _storage = storage;
    }

    public static WriteTagsContext Create(CommonInfoGatherContext p)
    {
        Dictionary<string, TagInfo> tags = new();
        AddForOverloads(isOk: true);
        AddForOverloads(isOk: false);

        return new(tags);

        void AddForOverloads(bool isOk)
        {
            var tag = isOk ? OverloadTag.Ok : OverloadTag.Failure;
            string genericTag = p.AllOverloadsContext.DefaultTags.Ref(tag);

            void AddDefault()
            {
                var key = p.Config.WellKnownTypes.FullyQualifiedName;
                if (!tags.TryGetValue(key, out var v))
                {
                    v = new()
                    {
                        ShortName = "WellKnown",
                        IsEnum = true,
                    };
                    tags.Add(key, v);
                }
                v.Values.Add(genericTag);

                if (isOk)
                {
                    v.OkValues ??= new();
                    v.OkValues.Add(genericTag);
                }
            }

            var model = p.Model.OverloadsSets.Get(tag);

            foreach (var m in model.Methods)
            {
                if (m.Tag is not { } tagType)
                {
                    AddDefault();
                    continue;
                }

                if (!tags.TryGetValue(tagType.Type.QualifiedName, out var v))
                {
                    v = new()
                    {
                        ShortName = tagType.Type.ShortName,
                        BaseSet = tagType.SupersetReference?.FullyQualifiedName,
                        IsEnum = tagType.IsEnum,
                    };
                    tags.Add(tagType.Type.QualifiedName, v);
                }

                void AddTo(HashSet<string> values)
                {
                    if (m.AcceptedTagValues.IsEmpty)
                    {
                        values.Clear();
                        return;
                    }

                    foreach (var x in m.AcceptedTagValues)
                    {
                        values.Add(x);
                    }
                }

                AddTo(v.Values);
                if (isOk)
                {
                    v.OkValues ??= new();
                    AddTo(v.OkValues);
                }
            }

            foreach (var r in model.PassAlongResults)
            {
                var tagName = r.ResultType.QualifiedName + "Tag";

                if (!tags.TryGetValue(tagName, out var v))
                {
                    v = new()
                    {
                        ShortName = r.ResultType.ShortName + "Tag",
                    };
                    tags.Add(tagName, v);
                }

                if (isOk)
                {
                    v.OkValues ??= [];
                }
            }
        }
    }

    public void Dispose()
    {
        // TODO: Pool all objects.
    }

    public IEnumerator<TagsKvpWrapper> GetEnumerator()
    {
        return _storage.Select(x => new TagsKvpWrapper(x)).GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}


internal static class WriteTagsHelper
{
    public static void WriteTagsType(this WriteTagsContext writeTags, CommonContext p)
    {
        var tagTypeInfo = p.ResultTypeInfo with
        {
            Name = p.ResultTypeInfo.Name + "Tag",
        };
        using var hierarchyCleanup = p.Writer.StartHierarchy(
            p.Model.ResultHierarchy,
            lastAccessibility: p.Model.ResultAccessibility,
            lastReplacement: tagTypeInfo);

        p.Writer.WriteLine($"public {p.Config.ResultBase} Value {{ get; }}");
        p.Writer.WriteLine("public readonly bool IsNone => Value.IsNone;");

        WriteConstructors(tagTypeInfo.Name);
        WriteTryCreateWithCheck(tagTypeInfo.Name);
        WriteResultSets();
        WriteDeclares();
        WriteAsTags();
        WriteIsOk();

        // Constructors
        void WriteConstructors(string tagTypeName)
        {
            p.Writer.WriteLine($"private {tagTypeName}({p.Config.ResultBase} tag) => Value = tag;");

            foreach (var t in writeTags)
            {
                p.Writer.WriteLine($"public {tagTypeName}({t.QualifiedName} tag)");
                {
                    using var b1 = p.Writer.WriteBlock();
                    if (t.Values.Count > 0)
                    {
                        p.Writer.Write($"global::{typeof(Debug).FullName!}.Assert(tag is ");
                        var list = p.Writer.List(separator: " or ");
                        foreach (var tagName in t.Values)
                        {
                            list.Write($"{t.QualifiedName}.{tagName}");
                        }
                        p.Writer.WriteLine(");");
                    }

                    if (t.IsEnum)
                    {
                        p.Writer.WriteLine($"Value = {p.Config.ResultBase}.Create<{t.QualifiedName}>(tag);");
                    }
                    else
                    {
                        p.Writer.WriteLine($"Value = tag.Value;");
                    }
                }
                p.Writer.WriteLine();
            }
        }

        void WriteTryCreateWithCheck(string tagTypeName)
        {
            p.Writer.WriteLine($"public static {tagTypeName} TryCreateWithCheck({p.Config.ResultBase} value)");
            {
                using var b = p.Writer.WriteBlock();
                {
                    p.Writer.WriteLine($"var ret = new {tagTypeName}(value);");
                    p.Writer.WriteLine("if (value.IsNone)");
                    using var b1 = p.Writer.WriteBlock();
                    p.Writer.WriteLine("return ret;");
                }
                foreach (var t in writeTags)
                {
                    using var b1 = p.Writer.WriteBlock();
                    p.Writer.WriteLine($"var r = ret.As<{t.QualifiedName}>();");

                    if (t.IsEnum)
                    {
                        p.Writer.WriteLine($"if (r != ({t.QualifiedName}) 0)");
                    }
                    else
                    {
                        p.Writer.WriteLine("if (!r.IsNone)");
                    }

                    using var b2 = p.Writer.WriteBlock();
                    p.Writer.WriteLine("return ret;");
                }
                p.Writer.WriteLine($"return new({p.Config.ResultBase}.None);");
            }
        }

        // Result sets
        void WriteResultSets()
        {
            p.Writer.WriteLine($"public static readonly global::{typeof(ImmutableArray<>).Namespace!}.ImmutableArray<{p.Config.ResultSet}> ResultSets = [");
            p.Writer.IncreaseIndent();
            foreach (var t in writeTags)
            {
                void WriteValue()
                {
                    if (!t.IsEnum)
                    {
                        Debug.Assert(t.Values.Count == 0);
                        // This could duplicate the result sets.
                        // We can't solve this easily.
                        // Having recursive dependencies is a nightmare in source generators.
                        // Another option is computing this at runtime (removing duplicates).
                        // This option is doable, the annoying thing is just that it's more work at runtime.
                        p.Writer.Write($".. {t.QualifiedName}.ResultSets");
                        return;
                    }
                    p.Writer.Write($"{p.Config.ResultBase}.ResultSetOf<{t.QualifiedName}>(");
                    if (t.Values.Count != 0)
                    {
                        p.Writer.Write("[");
                        var list = p.Writer.List(separator: ", ");
                        foreach (var x in t.Values)
                        {
                            list.Write($"{t.QualifiedName}.{x}");
                        }
                        p.Writer.Write("]");
                    }
                    p.Writer.Write(")");
                }
                WriteValue();
                p.Writer.WriteLine(",");
            }
            p.Writer.DecreaseIndent();
            p.Writer.WriteLine("];");
        }

        // Declares
        void WriteDeclares()
        {
            p.Writer.WriteLine("public static void Declare()");
            {
                using var b1 = p.Writer.WriteBlock();
                foreach (var t in writeTags)
                {
                    if (!t.IsEnum)
                    {
                        p.Writer.WriteLine($"{t.QualifiedName}.Declare();");
                        continue;
                    }
                    if (t.BaseSet is { } baseSet)
                    {
                        p.Writer.WriteLine($"{p.Config.ResultBase}.DeclareSubset<{t.QualifiedName}, {baseSet}>();");
                    }
                    else
                    {
                        p.Writer.WriteLine($"{p.Config.ResultBase}.Declare<{t.QualifiedName}>();");
                    }
                }
            }
        }

        // As<Tag>
        void WriteAsTags()
        {
            foreach (var t in writeTags)
            {
                // TODO: This will cause conflicts.
                var postfix = t.ShortName;

                p.Writer.WriteLine($"public readonly {t.QualifiedName} As{postfix}()");
                {
                    using var b = p.Writer.WriteBlock();
                    if (t.IsEnum)
                    {
                        p.Writer.WriteLine($"return Value.As<{t.QualifiedName}>();");
                    }
                    else
                    {
                        p.Writer.WriteLine($"return {t.QualifiedName}.TryCreateWithCheck(Value);");
                    }
                }
                p.Writer.WriteLine();
            }

            {
                p.Writer.WriteLine($"public readonly T As<T>() where T : struct");
                {
                    using var b = p.Writer.WriteBlock();
                    foreach (var t in writeTags)
                    {
                        p.Writer.WriteLine($"if (typeof(T) == typeof({t.QualifiedName}))");
                        using var b1 = p.Writer.WriteBlock();
                        p.Writer.WriteLine($"return (T) (object) As{t.ShortName}();");
                    }
                    p.Writer.WriteLine("throw new global::System.InvalidOperationException($\"Type {typeof(T).FullName!} is not allowed here\");");
                }
                p.Writer.WriteLine();
            }
        }

        // IsOk
        void WriteIsOk()
        {

            p.Writer.WriteLine($"public readonly bool IsOk");
            using var b = p.Writer.WriteBlock();
            p.Writer.WriteLine("get");
            using var b1 = p.Writer.WriteBlock();

            foreach (var tag in writeTags)
            {
                if (tag.OkValues is not { } okValues)
                {
                    continue;
                }

                using var b2 = p.Writer.WriteBlock();
                {
                    p.Writer.WriteLine($"var r = As<{tag.QualifiedName}>();");

                    if (tag.IsEnum)
                    {
                        p.Writer.WriteLine($"if (r != ({tag.QualifiedName}) 0)");
                        using var b3 = p.Writer.WriteBlock();

                        if (okValues.Count == 0)
                        {
                            p.Writer.WriteLine("return true;");
                        }

                        foreach (var v in okValues)
                        {
                            p.Writer.WriteLine($"if (r == {tag.QualifiedName}.{v})");
                            using var b4 = p.Writer.WriteBlock();
                            p.Writer.WriteLine("return true;");
                        }
                    }
                    else
                    {
                        p.Writer.WriteLine("if (!r.IsNone)");
                        using var b3 = p.Writer.WriteBlock();
                        p.Writer.WriteLine("return r.IsOk;");
                    }
                }
            }

            p.Writer.WriteLine("return false;");
        }
    }
}
