using System.Linq;

namespace EntityOwnership;

public interface ISomeOwnerFilter
{
    bool CanFilter<TEntity, TOwner, TOwnerId>()
        where TEntity : class;

    IQueryable<TEntity> Filter<TEntity, TOwner, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class;
}

public interface IUnspecifiedOwnerFilter
{
    bool CanFilter<TEntity, TOwnerId>()
        where TEntity : class;

    IQueryable<TEntity> Filter<TEntity, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class;
}

public interface IRootOwnerFilter : IUnspecifiedOwnerFilter
{
}

public interface IDirectOwnerFilter : IUnspecifiedOwnerFilter
{
}
