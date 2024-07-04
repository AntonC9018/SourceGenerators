using System;
using System.Linq.Expressions;
using System.Reflection;

namespace PropertyCacheHelper.Shared;

public readonly struct CachedPropertyInfo<TParent, TField>
{
    public CachedPropertyInfo(PropertyInfo propertyInfo)
    {
        PropertyInfo = propertyInfo;
    }

    public CachedPropertyInfo(Expression<Func<TParent, TField>> member)
    {
        var memberInfo = ((MemberExpression) member.Body).Member;
        PropertyInfo = (PropertyInfo) memberInfo;
    }

    public Type ParentType => typeof(TParent);
    public PropertyInfo PropertyInfo { get; }

    public static implicit operator CachedPropertyInfo(CachedPropertyInfo<TParent, TField> x)
    {
        return new CachedPropertyInfo(x.PropertyInfo, x.ParentType);
    }
}

public readonly struct CachedPropertyInfo
{
    public CachedPropertyInfo(PropertyInfo propertyInfo, Type parentType)
    {
        PropertyInfo = propertyInfo;
        ParentType = parentType;
    }

    public Type ParentType { get; }
    public PropertyInfo PropertyInfo { get; }
}
