# Select the SDK role from the project name

External products follow the same sibling project structure as this repository.
The project name suffix selects the source generator, analyzer, code fix,
package, or test role. The product name is the text before the first dot and is
used to find sibling projects. The shared project is required for every SDK
project; role-specific test and package projects require their matching sibling
projects.

One versioned `Anton.SourceGeneration.Sdk` package serves all roles. The SDK
provides the default target framework, helper and Roslyn package references,
project references, and package layout. Project files may override the target
framework. An unsupported name or missing required sibling fails the build
with a direct error.

This convention keeps consumer project files small. It also means a product
whose name contains dots needs a different layout or a later SDK extension.
