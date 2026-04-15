using System.IO;
using LibGit2Sharp;

namespace GenerateBaseConfigs;

internal interface IGitVersionControl
{
    bool IsTracked(string filePath);
    void UntrackFile(string filePath);
}

internal class GitVersionControl : IGitVersionControl
{
    internal static IGitVersionControl Instance = new GitVersionControl();

    /// <summary>
    /// Returns true if the file is tracked by git (in the index or HEAD).
    /// Ported verbatim from GenerateBindingRedirects/IGitVersionControl.cs:17.
    /// </summary>
    public bool IsTracked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var wsPath = filePath.GetGitWorkspaceRoot();
        if (wsPath == null)
            return false;

        // Get the right file case — important for git
        filePath = Directory.GetFiles(Path.GetDirectoryName(filePath)!, Path.GetFileName(filePath))[0];
        filePath = filePath[wsPath.Length..].Replace('\\', '/');

        using var repo = new Repository(wsPath);
        return repo.Index[filePath] != null || repo.Lookup("HEAD:" + filePath, ObjectType.Blob) != null;
    }

    /// <summary>
    /// Removes the file from the git index (equivalent to git rm --cached).
    /// </summary>
    public void UntrackFile(string filePath)
    {
        var wsPath = filePath.GetGitWorkspaceRoot();
        if (wsPath == null)
            return;

        var relativePath = filePath[wsPath.Length..].Replace('\\', '/');
        using var repo = new Repository(wsPath);
        repo.Index.Remove(relativePath);
        repo.Index.Write();
    }
}
