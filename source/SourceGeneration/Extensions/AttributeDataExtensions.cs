using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SourceGeneration.Extensions;

/// <summary>
/// Extension methods for the <see cref="AttributeData"/> type.
/// </summary>
internal static class AttributeDataExtensions
{
    /// <summary>
    /// Tries to get a constructor argument at a given index from the input <see cref="AttributeData"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of constructor argument to retrieve.</typeparam>
    /// <param name="attributeData">The target <see cref="AttributeData"/> instance to get the argument from.</param>
    /// <param name="index">The index of the argument to try to retrieve.</param>
    /// <param name="result">The resulting argument, if it was found.</param>
    /// <returns>Whether or not an argument of type <typeparamref name="T"/> at position <paramref name="index"/> was found.</returns>
    public static bool TryGetConstructorArgument<T>(this AttributeData attributeData, int index, out T? result)
    {
        if (attributeData.ConstructorArguments.Length >= index + 1 &&
            attributeData.ConstructorArguments[index].Value is T argument)
        {
            result = argument;

            return true;
        }

        result = default;

        return false;
    }

    /// <summary>
    /// Tries to get a given named argument value from an <see cref="AttributeData"/> instance, if present.
    /// </summary>
    /// <typeparam name="T">The type of argument to check.</typeparam>
    /// <param name="attributeData">The target <see cref="AttributeData"/> instance to check.</param>
    /// <param name="name">The name of the argument to check.</param>
    /// <param name="value">The resulting argument value, if present.</param>
    /// <returns>Whether or not <paramref name="attributeData"/> contains an argument named <paramref name="name"/> with a valid value.</returns>
    public static bool TryGetNamedArgument<T>(this AttributeData attributeData, string name, out T? value)
    {
        foreach (KeyValuePair<string, TypedConstant> properties in attributeData.NamedArguments)
        {
            if (properties.Key == name)
            {
                value = (T?)properties.Value.Value;

                return true;
            }
        }

        value = default;

        return false;
    }
}