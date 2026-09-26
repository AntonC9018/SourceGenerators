using System;
using System.Linq.Expressions;
using System.Reflection;

namespace PropertyCacheHelper.Shared;

public readonly struct CachedPropertyInfo<TParent, TField>
{
    public Type ParentType => typeof(TParent);
    public PropertyInfo PropertyInfo { get; }
    public string? JsonPropertyName { get; }

    public CachedPropertyInfo(PropertyInfo propertyInfo, string? jsonPropertyName = null)
    {
        PropertyInfo = propertyInfo;
        JsonPropertyName = jsonPropertyName;
    }

    public CachedPropertyInfo(Expression<Func<TParent, TField>> member, string? jsonPropertyName = null)
    {
        var memberInfo = ((MemberExpression) member.Body).Member;
        this = new((PropertyInfo) memberInfo, jsonPropertyName);
    }

    public static implicit operator CachedPropertyInfo(CachedPropertyInfo<TParent, TField> x)
    {
        return new CachedPropertyInfo(x.PropertyInfo, x.ParentType, x.JsonPropertyName);
    }
}

public readonly struct CachedPropertyInfo
{
    public CachedPropertyInfo(
        PropertyInfo propertyInfo,
        Type parentType,
        string? jsonPropertyName = null)
    {
        PropertyInfo = propertyInfo;
        ParentType = parentType;
        JsonPropertyName = jsonPropertyName;
    }

    public Type ParentType { get; }
    public PropertyInfo PropertyInfo { get; }
    public string? JsonPropertyName { get; }
}
