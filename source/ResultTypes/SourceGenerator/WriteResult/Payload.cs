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
                return p.Writer.StartHierarchy(
                    payload.Hierarchy,
                    allowDeclaringFields: true);
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
                    lastReplacement: payloadTypeInfo,
                    allowDeclaringFields: true);
            }
        }

        void WriteAllPayloadFields()
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

        void WritePayloadFields(
            OverloadSetInfoAccessor info,
            HashSet<string> alreadyWrittenPayloads)
        {
            {
                var payloadNames = info.PayloadNames;
                var methods = info.Model.Methods;
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
            {
                var names = info.ResultPayloadNames;
                var results = info.Model.PassAlongResults;
                for (int i = 0; i < results.Length; i++)
                {
                    if (!alreadyWrittenPayloads.Add(names[i]))
                    {
                        continue;
                    }
                    var type = results[i].ResultType.QualifiedName;

                    p.Writer.WriteLine($"public {type}Payload {names[i]};");
                }

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
        var model = info.Model;
        {
            var methods = model.Methods;
            for (int index = 0; index < methods.Length; index++)
            {
                var x = methods[index];
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
        {
            var passAlongResults = model.PassAlongResults;
            for (int index = 0; index < passAlongResults.Length; index++)
            {
                info.ResultPayloadNames[index] = GetName();

                string GetName()
                {
                    var x = passAlongResults[index];
                    if (x.ExistingFieldNameInPayload is { } existingName)
                    {
                        return existingName;
                    }
                    if (qualifiedNameToFieldName.Find(x.ResultType.QualifiedName) is { } existingName1)
                    {
                        return existingName1;
                    }
                    return x.ResultType.ShortName;
                }
            }
        }
    }

    public static bool IsPayloadNeverAssigned(this AllOverloadsContext allOverloadsContext)
    {
        foreach (var ov in allOverloadsContext)
        {
            var model = ov.Model;
            if (model.PassAlongResults.Length != 0)
            {
                return false;
            }
        }
        foreach (var ov in allOverloadsContext)
        {
            var model = ov.Model;
            foreach (var m in model.Methods)
            {
                if (m.Payload is not null)
                {
                    return false;
                }
            }
        }
        return true;
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

