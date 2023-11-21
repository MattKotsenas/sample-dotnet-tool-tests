# Using MSBuildProjectCreator to test dotnet tools

This repo is a sample of using [MSBuildProjectCreator] to do integration testing of dotnet global tools.

The example tool is the `botsay` tool from the [.NET tool tutorial][dotnet-tool-tutorial] packaged locally as a NuGet
package. The tests create a new temporary package repository and isolated [NuGet package cache][nuget-package-cache]
to install and run the tool without polluting your package caches.

[MSBuildProjectCreator]: https://github.com/jeffkl/MSBuildProjectCreator
[dotnet-tool-tutorial]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create
[nuget-package-cache]: https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders
