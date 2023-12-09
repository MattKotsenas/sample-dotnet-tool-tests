using Microsoft.Build.Utilities.ProjectCreation;
using NuGetTestUtils;
using System.IO.Abstractions;
using Xunit.Abstractions;

namespace Echo.IntegrationTests;

public abstract class EchoTestsBase : MSBuildTestBase
{
    protected ITestOutputHelper Output { get; private set; }
    protected string ArtifactsPath { get; private set; }
    protected Uri[] PackageFeeds { get; private set; }
    protected string PackageName { get; private set; } = "Echo";
    protected string PackageVersion { get; private set; }

    protected EchoTestsBase(ITestOutputHelper output)
    {
        Output = output;

        ArtifactsPath = RetrieveArtifactsPathFromAssemblyMetadata();
        PackageFeeds =
        [
            ConvertArtifactsPathToNuGetFeedUri(),
            new Uri("https://api.nuget.org/v3/index.json")
        ];
        PackageVersion = GetLatestPackageVersion();
    }

    private string RetrieveArtifactsPathFromAssemblyMetadata()
    {
        // Step 3: Retrieve the artifacts path from assembly metadata
        IReadOnlyDictionary<string, string?> metadata = new AssemblyMetadataParser(typeof(EchoTestsBase).Assembly).Parse();

        // The key 'ArtifactsPath' is defined as AssemblyMetadata in the test's .csproj file.
        const string key = "ArtifactsPath";
        if (!metadata.TryGetValue(key, out string? artifactsPath) || artifactsPath is null || !Directory.Exists(artifactsPath))
        {
            throw new InvalidDataException($"Assembly metadata attribute '{key}' not found or does not exist.");
        }

        return artifactsPath;
    }

    private Uri ConvertArtifactsPathToNuGetFeedUri()
    {
        // Step 4: Convert the artifact path to a Uri for use in the nuget.config
        return NupkgFinder.Find(ArtifactsPath).AsFeedUri();
    }

    private string GetLatestPackageVersion()
    {
        return NupkgFinder.Find(ArtifactsPath).LatestWithName(PackageName).Version;
    }
}

public class EchoTests : EchoTestsBase
{
    private readonly IFileSystem _fs = new FileSystem();

    public EchoTests(ITestOutputHelper output) :base (output)
    {
    }

    [Fact]
    public void TheOutputIsInTheLog()
    {
        // Step 5: Create a temporary workspace
        // Uses TestableIO extensions (see https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions)
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            Output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, PackageFeeds))
            {
                // Step 7: Create our "consuming" project that will install our NuGet package
                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(PackageName, PackageVersion)
                    .Save(Path.Combine(temp.FullName, "Sample", "Sample.csproj"));

                // Step 8: Try building the project and asserting the results
                project.TryBuild(restore: true, out bool result, out BuildOutput buildOutput);
                Assert.True(result);
                Assert.Contains("Hello, world!", buildOutput.GetConsoleLog());
            }
        }
    }
}

public class IncrementalBuiltEchoTests : EchoTestsBase
{
    private readonly IFileSystem _fs = new FileSystem();

    public IncrementalBuiltEchoTests(ITestOutputHelper output) : base (output)
    {
    }

    [Fact]
    public void TheTaskIsRunOnce()
    {
        const string incrementalBuildMessage = "Skipping target \"DoEcho\" because all output files are up-to-date with respect to the input files.";

        // Step 5: Create a temporary workspace
        // Uses TestableIO extensions (see https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions)
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            Output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, PackageFeeds))
            {
                // Step 7: Create our "consuming" project that will install our NuGet package
                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(PackageName, PackageVersion)
                    .Save(Path.Combine(temp.FullName, "Sample", "Sample.csproj"));

                // Step 8: Try building the project and asserting the results
                project.TryBuild(restore: true, out bool initialBuildResult, out BuildOutput output);
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
