using System;
using System.Linq;
using System.Linq.Expressions;

namespace EntityOwnership;

#nullable enable

public interface ISomeOwnerFilter
{
    bool CanFilter<TEntity, TOwner, TOwnerId>()
        where TEntity : class;

    IQueryable<TEntity> Filter<TEntity, TOwner, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
        where TEntity : class;

    Expression<Func<TEntity, bool>>? GetFilter<TEntity, TOwner, TOwnerId>(TOwnerId ownerId)
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
    bool TrySetOwnerId<TEntity, TOwner, TOwnerId>(TEntity entity, TOwnerId ownerId)
        where TEntity : class;
}
