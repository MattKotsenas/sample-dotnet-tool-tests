using System.Reflection;

namespace Microsoft.Botsay.IntegrationTests;

/// <summary>
/// Parses any &lt;AssemblyMetadata&gt; items defined by MSBuild. This allows MSBuild to inject values from the build process
/// into the binary so it can be used at runtime.
/// </summary>
/// <remarks>
/// See https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#assemblymetadata.
/// </remarks>
internal class AssemblyMetadataParser
{
    private readonly Assembly _targetAssembly;

    public AssemblyMetadataParser() : this(typeof(AssemblyMetadataParser).Assembly)
    {
    }

    public AssemblyMetadataParser(Assembly targetAssemb)
    {
        _targetAssembly = targetAssemb;
    }

    public IReadOnlyDictionary<string, string?> Parse()
    {
        Dictionary<string, string?> results = new();

        // Pull the paths from the <AssemblyMetadata> MSBuild property in the project file
        IEnumerable<AssemblyMetadataAttribute> metadataAttributes = _targetAssembly.GetCustomAttributes<AssemblyMetadataAttribute>();

        foreach (AssemblyMetadataAttribute attribute in metadataAttributes)
        {
            results.Add(attribute.Key, attribute.Value);
        }

        return results;
    }
}
