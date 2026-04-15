using System.IO;

namespace GenerateBaseConfigs;

internal static class Extensions
{
    /// <summary>
    /// Walks up the directory tree to find the git workspace root (the directory containing .git).
    /// Ported from Dayforce.CSharp.ProjectAssets.Extensions.GetGitWorkspaceRoot.
    /// </summary>
    internal static string? GetGitWorkspaceRoot(this string path)
    {
        string tmp;
        path = Path.GetFullPath(path);
        while (path.Length > 3 && !Directory.Exists(tmp = path + "\\.git") && !File.Exists(tmp))
            path = Path.GetDirectoryName(path)!;

        return path.Length <= 3 ? null : path + '\\';
    }
}
