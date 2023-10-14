//HintName: DirectOwnerFilter.cs
namespace EntityOwnership;

using System.Linq;

public sealed class DirectOwnerFilter : global::EntityOwnership.IDirectOwnerFilter
{
    private DirectOwnerFilter() {}
    public static DirectOwnerFilter Instance { get; } = new();

    public bool CanFilter<TEntity, TOwnerId>()
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var idType = typeof(TOwnerId);
        return EntityOwnershipHelper.SupportsDirectOwnerFilter(entityType, idType);
    }

    public IQueryable<TEntity> Filter<TEntity, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class
    {
        return EntityOwnershipGenericMethods.DirectOwnerFilterT<TEntity, TOwnerId>(query, ownerId);
    }
}
