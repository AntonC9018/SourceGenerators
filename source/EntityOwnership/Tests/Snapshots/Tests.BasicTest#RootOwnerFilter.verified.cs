//HintName: RootOwnerFilter.cs
namespace EntityOwnership;

using System.Linq;

public sealed class RootOwnerFilter : global::EntityOwnership.IRootOwnerFilter
{
    private RootOwnerFilter() {}
    public static RootOwnerFilter Instance { get; } = new();

    public bool CanFilter<TEntity, TOwnerId>()
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var idType = typeof(TOwnerId);
        return EntityOwnershipHelper.SupportsRootOwnerFilter(entityType, idType);
    }

    public IQueryable<TEntity> Filter<TEntity, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class
    {
        return EntityOwnershipGenericMethods.RootOwnerFilterT<TEntity, TOwnerId>(query, ownerId);
    }
}
