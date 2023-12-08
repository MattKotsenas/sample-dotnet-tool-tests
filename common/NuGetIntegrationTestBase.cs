using Microsoft.Build.Utilities.ProjectCreation;
using System.Reflection;

namespace NuGetTestUtils;

public abstract class NuGetIntegrationTestBase : MSBuildTestBase
{
    // TODO Step 3, 4, 5

    protected virtual string Step2RetrieveAssemblyMetadata(Assembly assembly)
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
}
