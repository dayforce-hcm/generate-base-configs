# GenerateBaseConfigs

## What this repo does

Pre-step for `GenerateBindingRedirects` in the Dayforce AT (acceptance test) build pipeline.

**Problem it solves:** `GenerateBindingRedirects` regenerates `<assemblyBinding>` sections in `app.config` files on every build. Those files are tracked in source control, so every NuGet version bump creates massive PR diffs across ~112 AT project app.configs — triggering approvals from teams unrelated to the actual change.

**Solution:**
- `app.base.config` = `app.config` minus binding redirects → committed to source control
- `app.config` = gitignored, regenerated each build from `app.base.config` + injected redirects
- The MSBuild target `RestoreBaseConfig` runs `BeforeTargets="WriteBindingRedirects"`

## Build & test

```bash
dotnet build -c Release
dotnet test -c Release
```

## Two modes

The tool selects its mode automatically based on whether `app.base.config` exists beside the project's `app.config`:

| `app.base.config` exists? | Mode | Action |
|--------------------------|------|--------|
| No | **A — Migration** | Strip `<assemblyBinding>` from `app.config` → save as `app.base.config`; create/update `.gitignore`; `git rm --cached app.config` |
| Yes | **B — Build restore** | Copy `app.base.config` → `app.config` so `GenerateBindingRedirects` gets a clean base |

## CLI

```
GenerateBaseConfigs.exe --projectFile <path-to.csproj>   # normal per-project use (MSBuild)
GenerateBaseConfigs.exe --batch <root-dir>               # one-time migration of all projects under a directory
GenerateBaseConfigs.exe --test --verbose -f <csproj>     # dry run
```

## Project structure

```
src/
  GenerateBaseConfigs/
    Program.cs                  # CLI (Mono.Options), Run(), RunBatch()
    AppConfigProcessor.cs       # XML stripping (StripAssemblyBinding), Mode A/B, .gitignore logic
    ProjectConfigLocator.cs     # Finds app.config path from .csproj — exact port of ProjectContext logic from GenerateBindingRedirects
    IGitVersionControl.cs       # Interface + GitVersionControl impl (IsTracked, UntrackFile via LibGit2Sharp)
    Extensions.cs               # GetGitWorkspaceRoot() extension method
    build/
      GenerateBaseConfigs.targets   # MSBuild: RestoreBaseConfig target (BeforeTargets=WriteBindingRedirects)
  Tests/
    AppConfigProcessorTests.cs  # XML unit tests + file I/O integration tests
    ProjectConfigLocatorTests.cs
    GitVersionControlStubs.cs   # NoGitVersionControl, TrackingGitVersionControl
    Input/                      # Fixture .csproj and app.config files
```

## Key design decisions

- **`ProjectConfigLocator`** is an exact port of the `ProjectContext` app.config discovery logic from `Dayforce.CSharp.ProjectAssets` (no dependency on that library — only the ~40 relevant lines are re-implemented). Returns `ConfigPaths(ExpectedConfigFilePath, ActualConfigFilePath, SdkStyle)`, mirroring the same two-path concept.
- **`AppConfigProcessor.GitVersionControl`** is a replaceable static field — swap in `NoGitVersionControl` in tests to avoid hitting a real git repo.
- **XML manipulation** uses `XmlDocument { PreserveWhitespace = true }` to preserve original formatting. Whitespace-only sibling nodes are removed alongside the stripped element to avoid orphaned blank lines.

## Integration in tipGit

Add to `tipGit/Build/AT/Directory.Build.props` (which already sets `IsAcceptanceTest = True` for all AT projects):

```xml
<ItemGroup>
  <PackageReference Include="GenerateBaseConfigs" PrivateAssets="all" />
</ItemGroup>
```

Add version to `tipGit/Directory.Build.props`:
```xml
<GenerateBaseConfigsVersion>1.0.*</GenerateBaseConfigsVersion>
```

And to `tipGit/Directory.Packages.props`:
```xml
<PackageVersion Include="GenerateBaseConfigs" Version="$(GenerateBaseConfigsVersion)" />
```

## One-time migration (batch mode)

```powershell
# Dry run first
GenerateBaseConfigs.exe --batch C:\Dayforce\tipGit\src --test --verbose

# Real run
GenerateBaseConfigs.exe --batch C:\Dayforce\tipGit\src --verbose

# Stage new files
git add "**/*.base.config" "**/.gitignore"
git commit -m "migration: introduce app.base.config for AT projects; remove app.config from source control"
```

## Reference repos

- `C:\Dayforce\GenerateBindingRedirects` — the downstream tool this pre-step feeds into; `WriteBindingRedirects` is the MSBuild target name we hook before
- `C:\Dayforce\tipGit` — consumer repo; AT projects are under `src/**/AT/`; `DFAcceptanceTest.sln` at repo root builds all of them
