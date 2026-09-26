using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceGeneration.Extensions;
using SourceGeneration.Helpers;

namespace EntityOwnership.SourceGenerator;

internal record OwnershipEntityTypeInfo
{
    public record struct IdInfo
    {
        public required TypeSyntaxReference Type { get; init; }
        public required string PropertyName { get; init; }
    }
    public record struct OwnerInfo
    {
        public required string TypeMetadataName { get; init; }
        public required IdInfo? Id { get; init; }
        public required string? NavigationPropertyName { get; init; }
    }
    public record struct IdAndTypeInfo
    {
        public required string TypeMetadataName { get; init; }
        public required IdInfo? Id { get; init; }
    }

    public required OwnerInfo? OwnerType { get; init; }
    public required IdAndTypeInfo Type { get; init; }
}

public static class OwnershipModelHelper
{
    internal static OwnershipEntityTypeInfo? GetEntityTypeInfo(this GeneratorSyntaxContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        var classDeclaration = (ClassDeclarationSyntax) context.Node;
        var classSymbol = (INamedTypeSymbol) context.SemanticModel.GetDeclaredSymbol(classDeclaration)!;

        var iownedInterface = compilation.GetTypeByMetadataName(typeof(IOwnedBy<>).FullName);
        var iownedImplementation = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.ConstructedFrom.Equals(iownedInterface, SymbolEqualityComparer.Default));

        var iownerInterface = compilation.GetTypeByMetadataName(typeof(IOwner).FullName);
        var iownerImplementation = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.Equals(iownerInterface, SymbolEqualityComparer.Default));

        if (iownedImplementation is null && iownerImplementation is null)
        {
            return null;
        }

        OwnershipEntityTypeInfo.IdAndTypeInfo typeInfo;
        {
            const string idPropertyName = "Id";
            var idProperty = classSymbol
                .GetMembersEvenIfUnimplemented(idPropertyName)
                .Concat(classSymbol
                    .GetMembersEvenIfUnimplemented(classSymbol.Name + "Id"))
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            idProperty ??= classSymbol
                .GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.GetAttributes()
                    // TODO: This attribute should be moved next to the other metadata things of this sg.
                    .Any(a => a.AttributeClass?.Name.Contains("CustomId") ?? false));

            typeInfo = new()
            {
                TypeMetadataName = classSymbol.GetFullyQualifiedMetadataName(),
                Id = idProperty is null ? null : new()
                {
                    PropertyName = idProperty.Name,
                    Type = TypeSyntaxReference.From(idProperty.Type),
                },
            };
        }

        OwnershipEntityTypeInfo.OwnerInfo? ownerTypeInfo = null;
        if (iownedImplementation is { } impl)
        {
            var ownerType = impl.TypeArguments[0];

            var navigationPropertyName = ownerType.Name;
            var navigationProperty = classSymbol
                .GetMembersEvenIfUnimplemented(navigationPropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(i => i.Type.Equals(ownerType, SymbolEqualityComparer.Default));

            var idPropertyName = $"{navigationPropertyName}Id";
            var idProperty = classSymbol
                .GetMembersEvenIfUnimplemented(idPropertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

            ownerTypeInfo = new()
            {
                TypeMetadataName = ownerType.GetFullyQualifiedMetadataName(),
                Id = idProperty is null ? null : new()
                {
                    Type = TypeSyntaxReference.From(idProperty.Type),
                    PropertyName = idProperty.Name,
                },
                NavigationPropertyName = navigationProperty?.Name,
            };
        }

        var info = new OwnershipEntityTypeInfo
        {
            OwnerType = ownerTypeInfo,
            Type = typeInfo,
        };

        return info;
    }
}
