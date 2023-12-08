using Microsoft.Build.Utilities.ProjectCreation;
using NuGetTestUtils;
using System.IO.Abstractions;
using System.Reflection;
using Xunit.Abstractions;

namespace Echo.IntegrationTests;


public abstract class EchoTestsBase : NuGetIntegrationTestBase
{
    protected readonly Uri[] _packageFeeds;
    protected readonly IFileSystem _fs = new FileSystem();
    protected readonly ITestOutputHelper _output;
    protected readonly string _packageName = "Echo";

    // TODO: DRY out
    private static Uri[] GetNuGetPackageFeedsFromArtifactsPath(string artifactsPath)
    {
        // TODO: Can this be a single one?

        // Step 4: Convert the artifact path(s) to a set of Uris for use in the nuget.config
        IReadOnlyCollection<string> directories = new NupkgFinder(artifactsPath).GetDirectories();
        return directories.Select(d => new Uri(d)).ToArray();
    }

    protected EchoTestsBase(ITestOutputHelper output)
    {
        string artifactsPath = Step2RetrieveAssemblyMetadata(typeof(EchoTestsBase).Assembly);
        _packageFeeds =
            [
            ..GetNuGetPackageFeedsFromArtifactsPath(artifactsPath),
            new Uri("https://api.nuget.org/v3/index.json")
            ];
        _output = output;
    }

    private ProjectCreator CreateProject(string packageName, string version)
    {
        return ProjectCreator.Templates.SdkCsproj()
            .ItemPackageReference(packageName, version);
    }

    private static string GetPackagePath()
    {
        // Pull the artifacts package path from the <AssemblyMetadata> MSBuild property in the project file
        IEnumerable<AssemblyMetadataAttribute> attributes = typeof(EchoTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        string? packagePath = attributes.Single(attribute => attribute.Key == "ArtifactsPath").Value;

        return Path.GetFullPath(packagePath!);
    }

    protected static string GetLatestPackageVersionFromFeed(string feed, string packageName)
    {
        if (!Directory.Exists(feed))
        {
            throw new ArgumentException($"Directory for package feed '{feed}' does not exist. Ensure you build prior to running integration tests.", nameof(feed));
        }

        // Search for file that begin with our package name
        string[] files = Directory.GetFiles(feed, $"{packageName}*.nupkg", SearchOption.AllDirectories);

        // Find the most recently modified
        FileInfo file = files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).First();

        // Extract the version from the name
        string version = file.Name.Replace($"{packageName}.", string.Empty).Replace(file.Extension, string.Empty);

        return version;
    }
}

public class EchoTests : EchoTestsBase
{
    public EchoTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void TheOutputIsInTheLog()
    {
        // Step 5: Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
        // See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                // TODO: Fix .First()
                string version = GetLatestPackageVersionFromFeed(_packageFeeds.First().AbsolutePath, _packageName);

                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(_packageName, version)
                    .Save(Path.Combine(temp.FullName, "ClassLibraryA", "ClassLibraryA.csproj")); // Do the initial save here while we have the temp path. Future updates can call .Save() with no path to update the exiting project



                project
                    .TryBuild(restore: true, out bool result, out BuildOutput buildOutput);

                Assert.True(result);
                Assert.Contains("Hello, world!", buildOutput.GetConsoleLog());
            }
        }
    }
}

public class IncrementalBuiltEchoTests : EchoTestsBase
{
    public IncrementalBuiltEchoTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void TheTaskIsRunOnce()
    {
        // Step 5: Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
        // See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                // TODO: Fix .First()
                string version = GetLatestPackageVersionFromFeed(_packageFeeds.First().AbsolutePath, _packageName);

                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(_packageName, version)
                    .Save(Path.Combine(temp.FullName, "ClassLibraryA", "ClassLibraryA.csproj")); // Do the initial save here while we have the temp path. Future updates can call .Save() with no path to update the exiting project


                string incrementalBuildMessage = "Skipping target \"DoEcho\" because all output files are up-to-date with respect to the input files.";

                project
                    .TryBuild(restore: true, out bool initialBuildResult, out BuildOutput output);

                Assert.True(initialBuildResult);
                Assert.DoesNotContain(incrementalBuildMessage, output.GetConsoleLog());
                Assert.Contains("Hello, world!", output.GetConsoleLog());

                project.TryBuild(restore: false, out bool secondBuildResult, out BuildOutput output2);
                Assert.True(secondBuildResult);
                Assert.Contains(incrementalBuildMessage, output2.GetConsoleLog());
            }
        }
    }
}
