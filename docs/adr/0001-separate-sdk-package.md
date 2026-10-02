# Publish the Project SDK separately from the helper library

External Roslyn projects use `Anton.SourceGeneration.Sdk` as their Project SDK and may reference `Anton.SourceGeneration` for reusable Roslyn code. Separate package IDs keep build conventions distinct from the library's compile assets, and changing published package IDs later would be disruptive.
