using System.Reflection;

namespace NuGetTestUtils;

/// <summary>
/// Parses any &lt;AssemblyMetadata&gt; items defined by MSBuild. This allows MSBuild to inject values from the build process
/// into the binary so it can be used at runtime.
/// </summary>
/// <remarks>
/// See https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#assemblymetadata.
/// </remarks>
public class AssemblyMetadataParser
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
        // Step 3: Retrieve the assembly metadata attributes and collect into a dictionary
        return _targetAssembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
    }
}
