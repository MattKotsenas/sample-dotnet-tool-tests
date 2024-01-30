# Automated integration testing for NuGet packages

Testing NuGet packages is typically a manual affair. Developers create a package, create a package source to point to
that package, and then manually install and uninstall the package into some projects to verify behavior. While this
can work for simple dependencies, NuGet packages can be very complicated ecosystems, bundling up not just DLLs but
additional content files, transformable source files, CLI tools, custom MSBuild tasks and targets, and more.
Additionally, the content and behavior can vary depending on the type and target framework version of the consuming
project.

After building and testing a few NuGet packages for .NET [CLI tools][dotnet-global-tools] and
[MSBuild Target packages][msbuild-target-packages], I decided to create this sample repo to demonstrate how to automate
integration testing scenarios for NuGet packages.

## A quick repo tour

The `botsay` folder is an example of testing a dotnet CLI tool. It builds the botsay tool from the
[.NET tool tutorial][dotnet-tool-tutorial] and adds integration tests to verify that the tool can be installed and
invoked.

The `msbuild` folder is an example of a package that adds custom MSBuild tasks. It's a simple
[custom MSBuild task][custom-msbuild-tasks] that hook's into the consuming project's build process and echos a hello
world message to the log.

Both examples use the `common` folder for setup, which uses [MSBuildProjectCreator] and [xUnit], though any test
framework should work. The environment is complete with a temporary package repository and isolated
[NuGet package cache][nuget-package-cache] to test installing and running the tool without polluting your package caches.

## Step-by-step process

Here's a step-by-step walkthrough of the setup and testing process. Start by reviewing steps 1 - 7, which are common
between all NuGet test types. These steps prepare the environment and ensure:
 * The most recent version of your package is used
 * That tests don't pollute your package caches
 * That tests clean up properly

Steps 8 - 9 are specific to each test scenario, and thus are separated for clarity.

Each step is also has a corresponding comment in the sample repo, so you can search for each step by number to jump
between this linear guide and the code in-context.

### Common setup

#### Step 1: Generate NuGet package on build (optional)

In the projects that create NuGet packages (e.g. `Microsoft.Botsay.csproj` and `Echo.csproj`), enable
`GeneratePackageOnBuild` so your `.nupkg` is created on every build. Otherwise you risk running tests on old packages,
which causes confusion.

```diff
  <PropertyGroup>
+   <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
```

#### Step 2: Take an "build order only" reference from the test project to the main project (optional)

If you're following my advice and generating a NuGet package on every build, you'll also want to enforce that any builds
and test runs ensure that the main project is up-to-date. To do that _without_ copying the main project binaries to your
test project (which may invalidate the test) do a "build order only" reference. This reference enforces proper build
ordering while preventing the output assemblies from being referenced or copied to the output directory.
See https://github.com/dotnet/msbuild/issues/4371#issuecomment-1195950719 for additional information.

```xml
<ProjectReference Include="../path/to/project.csproj">
  <Private>false</Private>
  <ExcludeAssets>all</ExcludeAssets>
  <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
</ProjectReference>
```

#### Step 3: Inject the artifacts path into the test assembly

In order to test the NuGet packages, the tests need to be able to find them. Relying on relative paths between the main
project's output directory and the test assembly can be fragile, especially across different test frameworks and
harnesses.

The way I like to solve this problem is by injecting the source of truth (MSBuild properties / items) into the test
assembly. In this repo I'm using .NET 8's new [simplified artifacts layout][dotnet-artifacts-layout], as it defines a
common output directory for all projects instead of creating output directories per project. However, any output layout
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
`Microsoft.Botsay.IntegrationTests.csproj` and `Echo.IntegrationTests.csproj`, add the following ItemGroup:

```diff
+ <ItemGroup>
+   <AssemblyMetadata Include="ArtifactsPath" Value="$(ArtifactsPath)" />
+ </ItemGroup>
```

#### Step 4: Retrieve the artifacts path from assembly metadata

Once the artifacts path is injected into the assembly, we need to retrieve it at runtime. The sample includes a
`AssemblyMetadataParser` class to encapsulate this responsibility. The bulk of the work is a single reflection call
like this:

```csharp
Assembly testAssembly; // Get a reference to the test assembly that contains the assembly metadata
IReadOnlyDictionary<string, string?> metadata = testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
```

From there you can then grab the artifacts directory like this

```csharp
metadata.TryGetValue("ArtifactsPath", out string? artifactsPath)
```

If you're in a scenario where reflection isn't available, you can also use a source generator such as
[ThisAssembly][thisassembly-github].

#### Step 5: Convert the artifacts path to a set of Uris for use in the nuget.config

In the sample I provided a `NupkgFinder` class to demonstrate walking the artifacts directory to find NuGet packages.
This strategy avoids hardcoding paths and project names into your tests, at the cost of slightly more complicated code.

If you have a very simple scenario, it's OK to hardcode paths directly to NuGet packages.

```csharp
Uri[] packageFeeds = new [] { new Uri(packagePath) };
```

#### Step 6: Create a temporary workspace

Next we need to create a temporary directory to use as a workspace. In your test class / method, start by creating a
temp dir. In the samples I've used TestableIO's `CreateDisposableDirectory` extension, as it simplifies creating a temp
directory and automatically cleaning up via the `IDisposable pattern`.

```csharp
// Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
// See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
FileSystem fs = new();
using (fs.CreateDisposableDirectory(out IDirectoryInfo temp)) // Create a temporary directory that is automatically cleaned up
{
    // Use temp here as scratch space
}
```

#### Step 7: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache

Inside our temp dir, use MSBuildProjectCreator's `PackageRepository`. This class does a few things for us:

1. It creates a `nuget.config` with `<packageSources>` entries for the artifacts paths found in Step 4
2. It sets the `globalPackagesFolder` (see the [NuGet config docs][nuget-config-docs]) to our temp, preventing package
operations from polluting the user's global cache
3. It hides and restores the `NUGET_PACKAGES` environment variable (if set) to prevent global cache pollution

```diff
using (fs.CreateDisposableDirectory(out IDirectoryInfo temp)) // Create a temporary directory that is automatically cleaned up
{
-   // Use temp here as scratch space
+   // Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
+   using (PackageRepository repo = PackageRepository.Create(temp.FullName, packageFeeds))
+   {
+
+   }
}
```

### CLI tool tests

#### Step 8: Run dotnet install and pass in our temp parameters

The final step is the install the tool itself. First, we append the temp dir to the `%PATH%` environment variable so that
the installed tool can be found. Then we run `dotnet tool install` and pass in the temp dir as the `--tool-path` and the
`nuget.config` file to `--configfile`. Note that if your NuGet packages are pre-release (e.g. include "-beta" in the
name) you'll want to also pass the `--prerelease` flag.

```diff
// Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
using (PackageRepository repo = PackageRepository.Create(temp.FullName, packageFeeds))
{
+   // Add our temp directory to %PATH% so installed tools can be found and executed
+   Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + $"{Path.PathSeparator}{temp.FullName}");
+
+    string[] args = $"tool install microsoft.botsay --tool-path {temp.FullName} --configfile {repo.NuGetConfigPath}".Split(" ");
+    await Cli.Wrap("dotnet").WithArguments(args).ExecuteBufferedAsync();
}
```

In the sample I use the excellent [CliWrap][cliwrap-github] library to make working with external processes easier. Of
course you can always use `Process.Start()` directly if you wish.

#### Step 9: Run the `botsay` tool and assert the expected output

With that setup in place, we can now run our test. Execute the `botsay` tool and assert the standard output is as
expected.

```csharp
string[] args = $"hello from the bot".Split(" ");

var result = await Cli.Wrap("botsay").WithArguments(args).ExecuteBufferedAsync();
Assert.Equal(expected, result.StandardOutput);
```

### MSBuild tests

#### Step 8: Create the "consuming" project that will install the NuGet package

With the environment setup complete, we can now create a "consuming" project that will install our NuGet package. We
again leverage MSBuildProjectCreator, this time using the `ProjectCreator` templates to create our consuming project.
Often you'll need several such projects in order to properly test the right combination of framework versions, property
values, and other settings.

```diff
using (PackageRepository repo = PackageRepository.Create(temp.FullName, PackageFeeds))
{
+   var project = ProjectCreator.Templates.SdkCsproj()
+       .ItemPackageReference(PackageName, PackageVersion)
+       .Save(Path.Combine(temp.FullName, "Sample", "Sample.csproj"));
}
```

#### Step 9: Try building the project and asserting the results

At long last, we can try building our project and asserting that the build succeeded / failed as expected, along with
any log output. For example, in the msbuild sample I include a test to verify that the injected target properly
handles incremental build.

```csharp
// Step 9: Try building the project and asserting the results
project.TryBuild(restore: true, out bool result, out BuildOutput buildOutput);
Assert.True(result);
Assert.Contains(expectedOutput, buildOutput.GetConsoleLog());
```

[dotnet-global-tools]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools
[msbuild-target-packages]: https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets
[custom-msbuild-tasks]: https://learn.microsoft.com/en-us/visualstudio/msbuild/tutorial-custom-task-code-generation?view=vs-2022
[xUnit]: https://xunit.net/
[MSBuildProjectCreator]: https://github.com/jeffkl/MSBuildProjectCreator
[dotnet-tool-tutorial]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create
[nuget-package-cache]: https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders
[dotnet-tool-invoke-names]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools#invoke-a-global-tool
[dotnet-artifacts-layout]: https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output
[thisassembly-github]: https://github.com/devlooped/ThisAssembly
[nuget-config-docs]: https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file#config-section
[cliwrap-github]: https://github.com/Tyrrrz/CliWrap
