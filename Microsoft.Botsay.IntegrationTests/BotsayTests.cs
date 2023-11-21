using CliWrap;
using CliWrap.Buffered;
using Microsoft.Build.Utilities.ProjectCreation;
using System.IO.Abstractions;
using Xunit.Abstractions;

namespace Microsoft.Botsay.IntegrationTests;

public class BotsayTests
{
    private readonly Uri[] _packageFeeds;
    private readonly IFileSystem _fs = new FileSystem();
    private readonly ITestOutputHelper _output;

    public BotsayTests(ITestOutputHelper output)
    {
        string artifactsPath = GetArtifactsPathFromAssemblyMetadata();
        _packageFeeds = GetNuGetPackageFeedsFromArtifactsPath(artifactsPath);
        _output = output;
    }

    private static string GetArtifactsPathFromAssemblyMetadata()
    {
        IReadOnlyDictionary<string, string?> metadata = new AssemblyMetadataParser().Parse();

        // The key 'ArtifactsPath' is defined as AssemblyMetadata in the .csproj file.
        const string key = "ArtifactsPath";
        if (!metadata.TryGetValue(key, out string? artifactsPath) || artifactsPath is null || !Directory.Exists(artifactsPath))
        {
            throw new InvalidDataException($"Assembly metadata attribute '{key}' not found or does not exist.");
        }

        return artifactsPath;
    }

    private static Uri[] GetNuGetPackageFeedsFromArtifactsPath(string artifactsPath)
    {
        IReadOnlyCollection<string> directories = new NupkgFinder(artifactsPath).GetDirectories();
        return directories.Select(d => new Uri(d)).ToArray();
    }

    private static async Task<BufferedCommandResult> Install(string temp, string nuget)
    {
        string[] args = $"tool install microsoft.botsay --tool-path {temp} --prerelease --configfile {nuget}".Split(" ");

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
        // Arrange

        // Use TestableIO to create a temp directory that's automatically deleted via IDisposable.
        // See https://github.com/TestableIO/System.IO.Abstractions.Extensions#automatic-cleanup-with-disposable-extensions
        using (_fs.CreateDisposableDirectory(out IDirectoryInfo temp))
        {
            _output.WriteLine($"Using temp directory '{temp.FullName}'.");

            // Create a nuget.config that points to our feeds and sets cache properties to avoid polluting the global cache
            using (PackageRepository repo = PackageRepository.Create(temp.FullName, _packageFeeds))
            {
                // Add our temp directory to %PATH% so installed tools can be found and executed
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + $"{Path.PathSeparator}{temp.FullName}");

                // Act
                await Install(temp.FullName, repo.NuGetConfigPath);
                BufferedCommandResult result = await Run(temp.FullName);

                // Assert
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
