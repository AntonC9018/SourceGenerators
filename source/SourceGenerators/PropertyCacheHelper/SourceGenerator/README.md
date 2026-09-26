Generates static fields that cache reflected `PropertyInfo` of the respective members.

Apply `[CachePropertyInfo]` to the type whose properties are to be cached.
You can adjust which properties to cache by applying `[CachePropertyInfo]` to them.
The source generator will create a `{YourTypeName}Props` static class containing the cached properties.
