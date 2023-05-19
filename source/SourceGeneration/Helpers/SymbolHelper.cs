using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SourceGeneration.Helpers;

public static class Helper
{
    public static IEnumerable<ITypeSymbol> GetSelfAndSubtypes(this ITypeSymbol type)
    {
        yield return type;
        while (type.BaseType is { } baseType)
        {
            yield return baseType;
            type = baseType;
        }
    }

    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol type)
    {
        return type.GetSelfAndSubtypes()
            .SelectMany(t => t.GetMembers());
    }
}
