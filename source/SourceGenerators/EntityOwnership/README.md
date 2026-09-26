Note that currently if the input entity types come from different projects, it won't generate code correctly.
Currently, it creates the graph model based on the entity types it can find in the project (source generation always happens per project). 
It is not possible to efficiently reference types from all projects when generating the outputs ([see e.g. this](https://github.com/martinothamar/Mediator/issues/62#issuecomment-1598357169)).

The source generator looks at all syntax nodes annotated with `IOwner` and `IOwned<>` (in the current project).
Then it constructs a graph out of all these. 
It detects cycles and saves metadata.
Currently, reporting the diagnostics (the owner types not being annotated, cycles being detected)
has not been implemented (it requires creating a separate analyzer,
and running the graph model creation logic there as well),
for now it simply refuses to generate the associated code altogether.

I've been thinking quite a lot about how to make the generated graph contain entities from all the projects:

- Simply caring about the immediate owner is not enough. For example, let's say we have types:
    ```mermaid
    flowchart LR
        subgraph ProjectA
        Project-->Company
        end
        Task-->Project
        subgraph ProjectB
        Task
        end
    ```
    If I go that route, and now change e.g. the Company's id type, the generated code from ProjectB
    will not be updated, because the Task model used for code generation doesn't know about Company.

- Recording the whole hierarchy for each node will work, but it will lead to a lot of duplication.
    In this case, the task model would look something like this:
    `Task(Type, Id, Hierarchy[Project(Type, Id), Company(Type, Id)])` (currently, it only records the immediate owner information).
    It's fine as long as the hierarchies are shallow, but the amount of information has quadratic complexity
    with the depth of the hierarchy.
    Also, it will also require a bunch of rewriting of the graph creation code and will require the model to change also.

- Ditching the syntax provider system and going for manual scanning would forego all benefits of incremental generation
    (and would also require a lot of rewriting).

Currently, we're only using the source generator in the context of a single project, but we may want
to use it in the context of multiple projects in the future.
When that time comes, I hope we'll have a better solution for this problem.


## TODO

Currently it generates an if-else chain on the generics parameters.
As I have recently learned, it doesn't get optimized whatsoever, because generics don't make
it generate duplicate definitions to jit separately.
This means each call to e.g. GetFilter is O(N) in the number of entity types.
This is pretty bad and might really become a problem eventually.
We probably should generate a static dictionary and look up instead, but I'm not sure of the best strategy.
