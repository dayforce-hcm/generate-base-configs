using System;
using System.Collections.Generic;
using GenerateBaseConfigs;

namespace Tests;

internal abstract class DummyGitVersionControl<T> : IGitVersionControl
    where T : DummyGitVersionControl<T>, new()
{
    internal static readonly T Instance = new();

    internal readonly struct Scope : IDisposable
    {
        private readonly IGitVersionControl m_prevInstance;

        public Scope()
        {
            m_prevInstance = AppConfigProcessor.GitVersionControl;
            AppConfigProcessor.GitVersionControl = Instance;
        }

        public readonly void Dispose() => AppConfigProcessor.GitVersionControl = m_prevInstance;
    }

    public string? WorkspaceRoot { get; set; }
    public string? HEAD => null;
    public abstract bool IsTracked(string filePath);
    public abstract void UntrackFile(string filePath);
}

internal class NoGitVersionControl : DummyGitVersionControl<NoGitVersionControl>
{
    public override bool IsTracked(string filePath) => false;
    public override void UntrackFile(string filePath) { }
}

internal class ForceGitVersionControl : DummyGitVersionControl<ForceGitVersionControl>
{
    public override bool IsTracked(string filePath) => true;
    public override void UntrackFile(string filePath) { }
}

internal class TrackingGitVersionControl : IGitVersionControl
{
    internal readonly List<string> UntrackedFiles = [];

    public string? WorkspaceRoot { get; set; }
    public string? HEAD => null;
    public bool IsTracked(string filePath) => true;
    public void UntrackFile(string filePath) => UntrackedFiles.Add(filePath);
}
