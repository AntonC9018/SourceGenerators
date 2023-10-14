﻿//HintName: EntityOwnershipExtensions.cs
// <auto-generated/>
#pragma warning disable
#nullable enable
namespace EntityOwnership
{
    using System.Linq.Expressions;
    using System.Linq;
    using System;

    public static partial class EntityOwnershipOverloads
    {
        public static IQueryable<Child1> DirectOwnerFilter(this IQueryable<Child1> query, int ownerId)
        {
            return query.Where((Child1 c) => c.RootId == ownerId);
        }

        public static IQueryable<Root> RootOwnerFilter(this IQueryable<Root> query, int ownerId)
        {
            return query.Where((Root r) => r.Id == ownerId);
        }

        public static IQueryable<Child1> RootOwnerFilter(this IQueryable<Child1> query, int ownerId)
        {
            return DirectOwnerFilter(query, ownerId);
        }
    }

    public static partial class EntityOwnershipGenericMethods
    {
        public static IQueryable<T> DirectOwnerFilterT<T, TId>(this IQueryable<T> query, TId ownerId)
        {
            if (typeof(T) == typeof(Child1))
            {
                var q = (IQueryable<Child1>)query;
                var id = Coerce<TId, int>(ownerId);
                return (IQueryable<T>)EntityOwnershipOverloads.DirectOwnerFilter(q, id);
            }

            throw new System.InvalidOperationException();
        }

        public static IQueryable<T> RootOwnerFilterT<T, TId>(this IQueryable<T> query, TId ownerId)
        {
            if (typeof(T) == typeof(Child1))
            {
                var q = (IQueryable<Child1>)query;
                var id = Coerce<TId, int>(ownerId);
                return (IQueryable<T>)EntityOwnershipOverloads.RootOwnerFilter(q, id);
            }

            throw new System.InvalidOperationException();
        }

        public static Expression<Func<T, bool>>? GetSomeOwnerFilterT<T, TOwner, TId>(TId ownerId)
        {
            if (typeof(T) == typeof(Root))
            {
                if (typeof(TOwner) == typeof(Root))
                {
                    var id = Coerce<TId, int>(ownerId);
                    Expression<Func<Root, bool>> e = (Root r) => r.Id == id;
                    return (Expression<Func<T, bool>>? )(LambdaExpression? )e;
                }

                return null;
            }

            if (typeof(T) == typeof(Child1))
            {
                if (typeof(TOwner) == typeof(Child1))
                {
                    var id = Coerce<TId, string>(ownerId);
                    Expression<Func<Child1, bool>> e = (Child1 c) => c.Id == id;
                    return (Expression<Func<T, bool>>? )(LambdaExpression? )e;
                }

                if (typeof(TOwner) == typeof(Root))
                {
                    var id = Coerce<TId, int>(ownerId);
                    Expression<Func<Child1, bool>> e = (Child1 c) => c.RootId == id;
                    return (Expression<Func<T, bool>>? )(LambdaExpression? )e;
                }

                return null;
            }

            return null;
        }

        public static IQueryable<TEntity> SomeOwnerFilterT<TEntity, TOwner, TOwnerId>(IQueryable<TEntity> query, TOwnerId ownerId)
            where TEntity : class
        {
            var filter = GetSomeOwnerFilterT<TEntity, TOwner, TOwnerId>(ownerId);
            if (filter is null)
                throw new InvalidOperationException();
            return query.Where(filter);
        }

        private static U Coerce<T, U>(T value)
        {
            if (value is not U u)
                throw new global::EntityOwnership.WrongIdTypeException(expected: typeof(U), actual: typeof(T));
            return u;
        }

        private static readonly Expression<Func<Root, int>> Id__Root__Root = (Root r) => r.Id;
        private static readonly Expression<Func<Child1, string>> Id__Child1__Child1 = (Child1 c) => c.Id;
        private static readonly Expression<Func<Child1, int>> Id__Child1__Root = (Child1 c) => c.RootId;
        public static Expression<Func<T, TId>>? GetOwnerIdExpression<T, TOwner, TId>()
        {
            if (typeof(T) == typeof(Root))
            {
                if (typeof(TOwner) == typeof(Root))
                    return (Expression<Func<T, TId>>)(object)Id__Root__Root;
                return null;
            }

            if (typeof(T) == typeof(Child1))
            {
                if (typeof(TOwner) == typeof(Child1))
                    return (Expression<Func<T, TId>>)(object)Id__Child1__Child1;
                if (typeof(TOwner) == typeof(Root))
                    return (Expression<Func<T, TId>>)(object)Id__Child1__Root;
                return null;
            }

            return null;
        }

        public static bool TrySetOwnerId<T, TOwner, TId>(this T entity, TId ownerId)
            where T : notnull
        {
            if (typeof(T) == typeof(Child1))
            {
                if (typeof(TOwner) != typeof(Root))
                    return false;
                var id = Coerce<TId, int>(ownerId);
                var castedEntity = (Child1)(object)entity;
                castedEntity.RootId = id;
                return true;
            }

            return false;
        }
    }

    public static partial class EntityOwnershipHelper
    {
        public static System.Type? GetIdType(System.Type entityType)
        {
            if (entityType == typeof(Root))
                return typeof(int);
            if (entityType == typeof(Child1))
                return typeof(string);
            return null;
        }

        public static System.Type? GetDirectOwnerType(System.Type entityType)
        {
            if (entityType == typeof(Child1))
                return typeof(Root);
            return null;
        }

        public static System.Type? GetRootOwnerType(System.Type entityType)
        {
            if (entityType == typeof(Child1))
                return typeof(Root);
            return null;
        }

        public static bool SupportsDirectOwnerFilter(System.Type entityType)
        {
            if (entityType == typeof(Child1))
                return true;
            return false;
        }

        public static bool SupportsDirectOwnerFilter(Type entityType, Type idType)
        {
            var ownerType = GetDirectOwnerType(entityType);
            if (ownerType is null)
                return false;
            var ownerIdType = GetIdType(ownerType);
            return SupportsDirectOwnerFilter(entityType) && ownerIdType == idType;
        }

        public static bool SupportsRootOwnerFilter(System.Type entityType)
        {
            if (entityType == typeof(Root))
                return true;
            if (entityType == typeof(Child1))
                return true;
            return false;
        }

        public static bool SupportsRootOwnerFilter(Type entityType, Type idType)
        {
            var ownerType = GetRootOwnerType(entityType);
            if (ownerType is null)
                return false;
            var ownerIdType = GetIdType(ownerType);
            return SupportsRootOwnerFilter(entityType) && ownerIdType == idType;
        }

        public static bool SupportsSomeOwnerFilter(System.Type entityType, System.Type ownerType)
        {
            if (entityType == typeof(Root))
            {
                if (ownerType == typeof(Root))
                    return true;
                return false;
            }

            if (entityType == typeof(Child1))
            {
                if (ownerType == typeof(Child1))
                    return true;
                if (ownerType == typeof(Root))
                    return true;
                return false;
            }

            return false;
        }

        public static bool SupportsSomeOwnerFilter(Type entityType, Type ownerType, Type idType)
        {
            var ownerIdType = GetIdType(ownerType);
            return SupportsSomeOwnerFilter(entityType, ownerType) && ownerIdType == idType;
        }

        private static readonly System.Collections.ObjectModel.ReadOnlyCollection<Type> DependentTypes__Root = System.Array.AsReadOnly(new System.Type[] { typeof(Child1) });
        private static readonly System.Collections.ObjectModel.ReadOnlyCollection<Type> DependentTypes__Child1 = System.Array.AsReadOnly(new System.Type[] { });
        public static System.Collections.ObjectModel.ReadOnlyCollection<Type> GetDependentTypes<TOwnerType>()
        {
            if (typeof(TOwnerType) == typeof(Root))
                return DependentTypes__Root;
            if (typeof(TOwnerType) == typeof(Child1))
                return DependentTypes__Child1;
            throw new System.InvalidOperationException();
        }
    }
}
