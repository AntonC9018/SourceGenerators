//HintName: SomeOwnerFilter.cs
namespace EntityOwnership;

using System.Linq;
using System.Linq.Expressions;

public sealed class SomeOwnerFilter : global::EntityOwnership.ISomeOwnerFilter
{
    private SomeOwnerFilter() {}
    public static SomeOwnerFilter Instance { get; } = new();

    public bool CanFilter<TEntity, TOwner, TOwnerId>()
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var ownerType = typeof(TOwner);
        var idType = typeof(TOwnerId);
        return EntityOwnershipHelper.SupportsSomeOwnerFilter(entityType, ownerType, idType);
    }

    public IQueryable<TEntity> Filter<TEntity, TOwner, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class
    {
        return EntityOwnershipGenericMethods.SomeOwnerFilterT<TEntity, TOwner, TOwnerId>(query, ownerId);
    }

    public Expression<System.Func<TEntity, bool>>? GetFilter<TEntity, TOwner, TOwnerId>(TOwnerId ownerId)
        where TEntity : class
    {
        return EntityOwnershipGenericMethods.GetSomeOwnerFilterT<TEntity, TOwner, TOwnerId>(ownerId);
    }
}
