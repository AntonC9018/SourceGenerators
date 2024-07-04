using System;
using System.Linq.Expressions;
using System.Reflection;

namespace CachedPropertyInfo.Shared;

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
}
