# Using MSBuildProjectCreator to test dotnet tools

This repo is a sample of using [MSBuildProjectCreator] to do integration testing of dotnet global tools.

The example tool is the `botsay` tool from the [.NET tool tutorial][dotnet-tool-tutorial] packaged locally as a NuGet
package. The tests create a new temporary package repository and isolated [NuGet package cache][nuget-package-cache]
to install and run the tool without polluting your package caches.

## Tour around the repo

Here's a quick description of the important parts of the repo.

### Step 1: Generate NuGet package on build (optional)

In `Microsoft.Botsay.csproj`, enable `GeneratePackageOnBuild` so your `.nupkg` is created on every build.

```diff
  <PropertyGroup>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>botsay</ToolCommandName>
    <RollForward>major</RollForward>
+   <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
```

### Step 2: Inject the artifacts path into the test assembly

In order to test the NuGet packages, the tests need to be able to find them. Relying on relative paths between the main
project's output directory and the test assembly can be fragile, especially across different test frameworks and
harnesses.

The way I like to solve this problem is by injecting the source of truth (MSBuild properties / items) into the test
assembly. In this repo I'm using .NET 8's new [simplified artifacts layout][dotnet-artifacts-layout], as it defines a
common output directory the solution, instead of creating output directories per project. However, any output layout
can work.

Start by creating an MSBuild property to some well-known output location in your solution. In this case I'm using the
`<ArtifactsPath>` property that's defined in `//Directory.Build.props`.

```diff
<Project>
  <PropertyGroup>
+   <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

Next, we instruct MSBuild to inject that property into the test assembly via `<AssemblyMetadata>`. In
`Microsoft.Botsay.IntegrationTests.csproj`, add the following ItemGroup:

```diff
+ <ItemGroup>
+   <AssemblyMetadata Include="ArtifactsPath" Value="$(ArtifactsPath)" />
+ </ItemGroup>
```

### Step 3: Retrieve the assembly metadata

Once the artifacts path is injected into the assembly, we need to retrieve it at runtime. The sample includes a
`AssemblyMetadataParser` class to encapsulate this responsibility. The bulk of the work is a single reflection call
like this:

```csharp
typeof(BotsayTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
```

If you're in a scenario where reflection isn't available, you can also use a source generator such as
[ThisAssembly][thisassembly-github].

### Step 4: Convert the artifact path(s) to a set of Uris for use in the nuget.config

In the sample I provided a `NupkgFinder` class to demonstrate walking the artifacts directory to find NuGet packages.
This strategy avoids hardcoding paths and project names into your tests, at the cost of slightly more complicated code.

If you have a very simple scenario, it's OK to hardcode paths directly to NuGet packages.

```csharp
Uri[] packageFeeds = new [] { new Uri(packagePath) };
```

### Step 5: Create a temporary workspace

Next we need to create a temporary directory to use as a workspace. In `BotsayTests.cs`, start by creating a temp dir.
In the sample I've used TestableIO's `CreateDisposableDirectory` extension, as it simplifies creating a temp directory
and automatically cleaning up via the `IDisposable pattern`.

```csharp
// Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
// See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
IFileSystem fs = new FileSystem();
using (fs.CreateDisposableDirectory(out IDirectoryInfo temp)) // Create a temporary directory that is automatically cleaned up
{
    // Use temp here as scratch space
}
```

### Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache

Inside our temp dir, use MSBuildProjectCreator's `PackageRepository`. This class does a few things for us:

1. It creates a `nuget.config` with `<packageSources>` entries for the artifacts paths found in Step 4
2. It sets the `globalPackagesFolder` (see the [NuGet config docs][nuget-config-docs]) to our temp, preventing package
operations from polluting the user's global cache
3. It hides and restores the `NUGET_PACKAGES` environment variable (if set) to prevent global cache pollution

```diff
using (fs.CreateDisposableDirectory(out IDirectoryInfo temp)) // Create a temporary directory that is automatically cleaned up
{
+   // Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
+   using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
+   {
+       // Add our temp directory to %PATH% so installed tools can be found and executed
+       Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + $"{Path.PathSeparator}{temp.FullName}");
+   }
}
```

Lastly, we append the temp dir to the `%PATH%` environment variable so that any installed tools can be found.

### Step 7: Run dotnet install and pass in our temp parameters

The final setup step is to run `dotnet tool install` and pass in the temp dir as the `--tool-path` and the
`nuget.config` file to `--configfile`. Note that if your NuGet packages are pre-release (e.g. include "-beta" in the
name) you'll want to also pass the `--prerelease` flag.

```diff
using (fs.CreateDisposableDirectory(out IDirectoryInfo temp)) // Create a temporary directory that is automatically cleaned up
{
    // Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
    using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
    {
        // Add our temp directory to %PATH% so installed tools can be found and executed
        Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + $"{Path.PathSeparator}{temp.FullName}");

+        // Step 7: Run `dotnet tool install`
+        string[] args = $"tool install microsoft.botsay --tool-path {temp.FullName} --configfile {repo.NuGetConfigPath}".Split(" ");

+        await Cli.Wrap("dotnet").WithArguments(args).ExecuteBufferedAsync();
    }
}
```

In the sample I use the excellent [CliWrap][cliwrap-github] library to make working with external processes easier. Of
course you can always use `Process.Start()` directly if you wish.

### Step 8: Run your test

With that setup in place, we can now run our test. Execute the `botsay` tool and assert the standard output is as
expected.

```csharp
// Step 8: Run the `botsay` tool and assert the expected output
string[] args = $"hello from the bot".Split(" ");

BufferedCommandResult result = await Cli.Wrap("botsay").WithArguments(args).ExecuteBufferedAsync();
Assert.Equal(expected, result.StandardOutput);
```

[MSBuildProjectCreator]: https://github.com/jeffkl/MSBuildProjectCreator
[dotnet-tool-tutorial]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create
[nuget-package-cache]: https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders
[dotnet-artifacts-layout]: https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output
[thisassembly-github]: https://github.com/devlooped/ThisAssembly
[nuget-config-docs]: https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file#config-section
[cliwrap-github]: https://github.com/Tyrrrz/CliWrap
