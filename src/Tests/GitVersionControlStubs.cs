using System;
using GenerateBaseConfigs;

namespace Tests;

/// <summary>
/// Stub that reports no files as git-tracked. Use in tests to avoid touching a real git repo.
/// </summary>
internal sealed class NoGitVersionControl : IGitVersionControl
{
    internal static readonly NoGitVersionControl Instance = new();

    public bool IsTracked(string filePath) => false;
    public void UntrackFile(string filePath) { }

    /// <summary>Scope that replaces AppConfigProcessor.GitVersionControl for the duration of the test.</summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly IGitVersionControl _previous;

        internal Scope(IGitVersionControl stub)
        {
            _previous = AppConfigProcessor.GitVersionControl;
            AppConfigProcessor.GitVersionControl = stub;
        }

        public void Dispose() => AppConfigProcessor.GitVersionControl = _previous;
    }
}

/// <summary>
/// Stub that reports every file as git-tracked and records untrack calls.
/// </summary>
internal sealed class TrackingGitVersionControl : IGitVersionControl
{
    internal readonly System.Collections.Generic.List<string> UntrackedFiles = [];

    public bool IsTracked(string filePath) => true;
    public void UntrackFile(string filePath) => UntrackedFiles.Add(filePath);
}
