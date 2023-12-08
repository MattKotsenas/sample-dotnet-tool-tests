namespace NuGetTestUtils;

/// <summary>
/// Helper class that finds all directories that contain .nupkg files under a given root directory.
/// </summary>
public class NupkgFinder
{
    private readonly string _root;

    public NupkgFinder(string root)
    {
        _root = root;
    }

    public IReadOnlyCollection<string> GetDirectories()
    {
        string[] nugetPackages = Directory.GetFiles(_root, "*.nupkg", SearchOption.AllDirectories);
        string[] nugetDirectories = nugetPackages.Select(p => new FileInfo(p).Directory!.FullName).Distinct().ToArray();

        return nugetDirectories;
    }
}
