using CliWrap;
using CliWrap.Buffered;
using Microsoft.Build.Utilities.ProjectCreation;
using NuGetTestUtils;
using System.IO.Abstractions;
using System.Reflection;
using Xunit.Abstractions;

namespace Microsoft.Botsay.IntegrationTests;

public class BotsayTests
{
    private readonly Uri _packageFeeds;
    private readonly IFileSystem _fs = new FileSystem();
    private readonly ITestOutputHelper _output;

    public BotsayTests(ITestOutputHelper output)
    {
        string artifactsPath = RetrieveArtifactsPathFromAssemblyMetadata(typeof(BotsayTests).Assembly);
        _packageFeeds = ConvertArtifactsPathToNuGetFeedUri(artifactsPath);
        _output = output;
    }

    private string RetrieveArtifactsPathFromAssemblyMetadata(Assembly assembly)
    {
        // Step 3: Retrieve the artifacts path from assembly metadata
        IReadOnlyDictionary<string, string?> metadata = new AssemblyMetadataParser(assembly).Parse();

        // The key 'ArtifactsPath' is defined as AssemblyMetadata in the test's .csproj file.
        const string key = "ArtifactsPath";
        if (!metadata.TryGetValue(key, out string? artifactsPath) || artifactsPath is null || !Directory.Exists(artifactsPath))
        {
            throw new InvalidDataException($"Assembly metadata attribute '{key}' not found or does not exist.");
        }

        return artifactsPath;
    }

    private static Uri ConvertArtifactsPathToNuGetFeedUri(string artifactsPath)
    {
        // Step 4: Convert the artifact path to a Uri for use in the nuget.config
        return NupkgFinder.Find(artifactsPath).AsFeedUri();
    }

    private static async Task<BufferedCommandResult> Install(string temp, string nugetConfig)
    {
        // Step 7: Run `dotnet tool install` and specify `--tool-path` and `--configfile`
        string[] args = $"tool install microsoft.botsay --tool-path {temp} --prerelease --configfile {nugetConfig}".Split(" ");

        return await Cli.Wrap("dotnet")
        .WithArguments(args)
        .ExecuteBufferedAsync();
    }

    private async Task<BufferedCommandResult> Run(string temp)
    {
        string[] args = $"hello from the bot".Split(" ");

        return await Cli.Wrap("botsay")
            .WithArguments(args)
            .ExecuteBufferedAsync();
    }

    [Fact]
    public async Task CanInstallAndRun()
    {
        // Step 5: Create a temporary workspace
        // Uses TestableIO extensions (see https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions)
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Step 6: Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                // Add our temp directory to %PATH% so installed tools can be found and executed
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + $"{Path.PathSeparator}{temp.FullName}");
                await Install(temp.FullName, repo.NuGetConfigPath);

                // Step 8: Run the `botsay` tool and assert the expected output
                BufferedCommandResult result = await Run(temp.FullName);
                string expected = "\n" +
@"        hello from the bot
        __________________
                        \
                        \
                            ....
                            ....'
                            ....
                            ..........
                        .............'..'..
                    ................'..'.....
                .......'..........'..'..'....
                ........'..........'..'..'.....
                .'....'..'..........'..'.......'.
                .'..................'...   ......
                .  ......'.........         .....
                .    _            __        ......
                ..    #            ##        ......
            ....       .                 .......
            ......  .......          ............
                ................  ......................
                ........................'................
            ......................'..'......    .......
            .........................'..'.....       .......
        ........    ..'.............'..'....      ..........
    ..'..'...      ...............'.......      ..........
    ...'......     ...... ..........  ......         .......
    ...........   .......              ........        ......
    .......        '...'.'.              '.'.'.'         ....
    .......       .....'..               ..'.....
    ..       ..........               ..'........
            ............               ..............
            .............               '..............
            ...........'..              .'.'............
        ...............              .'.'.............
        .............'..               ..'..'...........
        ...............                 .'..............
        .........                        ..............
            .....
    " + Environment.NewLine;

                Assert.Equal(expected, result.StandardOutput);
            }
        }
    }
}
