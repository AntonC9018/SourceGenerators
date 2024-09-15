using System;
using System.Collections.Generic;
using SourceGeneration.Models;

namespace ResultTypes.SourceGenerator;

internal static class PayloadHelper
{
    public static void WritePayload(this WritePayloadContext context, CommonContext p)
    {
        {
            using var c = StartPayload();
            WriteAllPayloadFields();
        }

        HierarchyCleanup StartPayload()
        {
            if (p.Model.SharedPayload is { } payload)
            {
                return p.Writer.StartHierarchy(payload.Hierarchy);
            }
            else
            {
                var payloadTypeInfo = p.ResultTypeInfo with
                {
                    Name = context.PayloadTypeName,
                };
                return p.Writer.StartHierarchy(
                    p.Model.ResultHierarchy,
                    lastAccessibility: p.Model.ResultAccessibility,
                    lastReplacement: payloadTypeInfo);
            }
        }

        void WriteAllPayloadFields()
        {
            bool allHaveSinglePayload = p.AllOverloadsContext.SinglePayloadIndex
                .Reduce(static (a, x) => a && x != -1, initialState: true);

            if (!allHaveSinglePayload)
            {
                // HashSet pool?
                var writtenPayloads = new HashSet<string>();

                if (p.Model.SharedPayload is { } sharedPayload)
                {
                    foreach (var field in sharedPayload.ExistingFields)
                    {
                        writtenPayloads.Add(field);
                    }
                }

                foreach (var overloadsInfo in p.AllOverloadsContext)
                {
                    WritePayloadFields(overloadsInfo, writtenPayloads);
                }
            }
        }

        void WritePayloadFields(
            OverloadSetInfoAccessor info,
            HashSet<string> alreadyWrittenPayloads)
        {
            var methods = info.Model.Methods;
            var payloadNames = info.PayloadNames;

            for (int i = 0; i < methods.Length; i++)
            {
                if (payloadNames[i] is not { } payloadName)
                {
                    continue;
                }
                if (!alreadyWrittenPayloads.Add(payloadName))
                {
                    continue;
                }
                var type = methods[i].Payload!.Type.QualifiedName;

                p.Writer.WriteLine($"public {type} {payloadName};");
            }
        }
    }

    public static string? GetDefaultPayloadFieldName(in Model.Method m)
    {
        if (m.Payload is not { })
        {
            return null;
        }
        return m.Payload.Type.ShortName;
    }

    public static void SetupPayloadFieldNames(
        OverloadSetInfoAccessor info,
        ResultTypeToFieldNameMap qualifiedNameToFieldName)
    {
        var model = info.Model.Methods;
        for (int index = 0; index < model.Length; index++)
        {
            var x = model[index];
            if (x.Payload is not { } payload)
            {
                continue;
            }

            if (payload.IsWholePayload)
            {
                continue;
            }

            if (qualifiedNameToFieldName.Find(payload.Type.QualifiedName) is { } existingName)
            {
                info.PayloadNames[index] = existingName;
            }
            else
            {
                info.PayloadNames[index] = GetDefaultPayloadFieldName(x);
            }
        }
    }
}

internal readonly struct ResultTypeToFieldNameMap : IDisposable
{
    private readonly Dictionary<string, string> _storage;

    private ResultTypeToFieldNameMap(Dictionary<string, string> s)
    {
        _storage = s;
    }

    public string? Find(string qualifiedName)
    {
        return _storage.TryGetValue(qualifiedName, out var value) ? value : null;
    }

    public static ResultTypeToFieldNameMap Create(AllOverloadsContext context)
    {
        var map = new Dictionary<string, string>();
        Fill();
        return new(map);

        void Fill()
        {
            foreach (var info in context)
            {
                foreach (var x in info.Model.PassAlongResults)
                {
                    if (x.ExistingFieldNameInPayload is { } existingField)
                    {
                        map.Add(x.ResultType.QualifiedName, existingField);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
    }
}

// TODO: Maybe put the field names here.
internal readonly struct WritePayloadContext : IDisposable
{
    public string PayloadTypeName { get; init; }

    public static WritePayloadContext Create(CommonInfoGatherContext p)
    {
        if (p.Model.SharedPayload is { } payload)
        {
            return new()
            {
                PayloadTypeName = payload.Hierarchy.FullyQualifiedMetadataName,
            };
        }

        return new()
        {
            PayloadTypeName = p.ResultTypeInfo.Name + "Payload",
        };
    }

    public void Dispose()
    {
    }
}

