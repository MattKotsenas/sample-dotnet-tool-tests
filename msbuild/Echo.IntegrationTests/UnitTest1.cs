using Microsoft.Build.Utilities.ProjectCreation;
using NuGetTestUtils;
using System.IO.Abstractions;
using Xunit.Abstractions;

namespace Echo.IntegrationTests;


public abstract class EchoTestsBase : NuGetIntegrationTestBase
{
    // TODO: Fix naming
    protected readonly Uri[] _packageFeeds;
    protected readonly IFileSystem _fs = new FileSystem();
    protected readonly ITestOutputHelper _output;
    protected readonly string _packageName = "Echo";
    protected readonly string _artifactsPath;

    protected EchoTestsBase(ITestOutputHelper output)
    {
        _artifactsPath = Step2RetrieveArtifactsPathFromAssemblyMetadata(typeof(EchoTestsBase).Assembly);
        _packageFeeds =
        [
            Step3ConvertArtifactsPathToNuGetFeedUri(_artifactsPath),
            new Uri("https://api.nuget.org/v3/index.json")
        ];
        _output = output;
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
        // Step 4: Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
        // See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 5: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                string version = NupkgFinder.Find(_artifactsPath).LatestWithName(_packageName).Version;

                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(_packageName, version)
                    .Save(Path.Combine(temp.FullName, "Sample", "Sample.csproj"));

                project.TryBuild(restore: true, out bool result, out BuildOutput buildOutput);

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
        // Step 4: Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
        // See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 5: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                string version = NupkgFinder.Find(_artifactsPath).LatestWithName(_packageName).Version;

                var project = ProjectCreator.Templates.SdkCsproj()
                    .ItemPackageReference(_packageName, version)
                    .Save(Path.Combine(temp.FullName, "Sample", "Sample.csproj"));

                string incrementalBuildMessage = "Skipping target \"DoEcho\" because all output files are up-to-date with respect to the input files.";

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
