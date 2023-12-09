using Microsoft.Build.Utilities.ProjectCreation;
using System.Reflection;

namespace NuGetTestUtils;

public abstract class NuGetIntegrationTestBase : MSBuildTestBase
{
    protected virtual string Step2RetrieveArtifactsPathFromAssemblyMetadata(Assembly assembly)
    {
        IReadOnlyDictionary<string, string?> metadata = new AssemblyMetadataParser(assembly).Parse();

        // The key 'ArtifactsPath' is defined as AssemblyMetadata in the test's .csproj file.
        const string key = "ArtifactsPath";
        if (!metadata.TryGetValue(key, out string? artifactsPath) || artifactsPath is null || !Directory.Exists(artifactsPath))
        {
            throw new InvalidDataException($"Assembly metadata attribute '{key}' not found or does not exist.");
        }

        return artifactsPath;
    }

    protected static Uri Step3ConvertArtifactsPathToNuGetFeedUri(string artifactsPath)
    {
        // Step 3: Convert the artifact path to a Uri for use in the nuget.config
        return NupkgFinder.Find(artifactsPath).AsFeedUri();
    }
}
