# GenerateBaseConfigs

A build tool that eliminates `app.config` churn in pull requests caused by auto-generated assembly binding redirects.

## The problem

[`GenerateBindingRedirects`](../GenerateBindingRedirects) regenerates the `<assemblyBinding>` section of `app.config` files on every build. Because these files are committed to source control, any NuGet package version bump touches every `app.config` in the repo — producing PR diffs with hundreds of unrelated file changes that require approval from teams who own those configs.

## The solution

`GenerateBaseConfigs` introduces a companion file, `app.base.config`, that holds everything in `app.config` **except** the binding redirects. This file is committed to source control instead of `app.config`.

During each build, `GenerateBaseConfigs` runs **before** `GenerateBindingRedirects` and restores `app.config` from `app.base.config`. `GenerateBindingRedirects` then injects the current binding redirects into the restored file as usual. The resulting `app.config` is gitignored and never committed.

```
source control:   app.base.config   ← your settings, no binding redirects
                  .gitignore        ← contains "app.config"
build output:     app.config        ← restored from base + redirects injected
```

## How it works

The tool selects its behaviour automatically based on whether `app.base.config` exists:

| State | What the tool does |
|---|---|
| `app.base.config` **does not exist** (first run / migration) | Reads `app.config`, strips `<assemblyBinding>`, writes `app.base.config`, adds `app.config` to `.gitignore`, removes `app.config` from the git index |
| `app.base.config` **exists** (every subsequent build) | Copies `app.base.config` → `app.config` if `app.config` is absent or older than `app.base.config`; skips the copy otherwise to avoid touching the file timestamp unnecessarily |

## Build & test

```bash
dotnet build -c Release
dotnet test -c Release
```

---

## Integration guide

### Prerequisites

- The repo already uses [`GenerateBindingRedirects`](../GenerateBindingRedirects).
- The NuGet package for `GenerateBaseConfigs` is published to your internal feed.
- All projects you want to migrate are .NET Framework (not .NET Core / .NET 5+).

### Step 1 — Add the NuGet package reference

Add a `PackageReference` to every project that should use the tool. In Dayforce's `tipGit`, all acceptance test (AT) projects inherit from `Build/AT/Directory.Build.props`, so one addition covers all of them:

```xml
<!-- tipGit/Build/AT/Directory.Build.props -->
<Project>
  <PropertyGroup>
    <IsAcceptanceTest>True</IsAcceptanceTest>
    ...
  </PropertyGroup>

  <!-- Add this: -->
  <ItemGroup>
    <PackageReference Include="GenerateBaseConfigs" PrivateAssets="all" />
  </ItemGroup>

  <Import Project="$(UpstreamDirectoryBuildProps)" />
</Project>
```

If your repo uses central package management (`Directory.Packages.props`), pin the version there:

```xml
<!-- Directory.Build.props -->
<GenerateBaseConfigsVersion>1.0.*</GenerateBaseConfigsVersion>

<!-- Directory.Packages.props -->
<PackageVersion Include="GenerateBaseConfigs" Version="$(GenerateBaseConfigsVersion)" />
```

### Step 2 — Verify the MSBuild target ordering

The NuGet package includes `GenerateBaseConfigs.targets`, which defines:

```xml
<Target Name="RestoreBaseConfig" BeforeTargets="WriteBindingRedirects" ...>
```

`WriteBindingRedirects` is the target defined by `GenerateBindingRedirects`. No manual ordering is required — MSBuild will run `RestoreBaseConfig` automatically before `WriteBindingRedirects` whenever both packages are present.

You can confirm the ordering in the MSBuild Structured Log Viewer after a build.

### Step 3 — Run the one-time migration

Before the new build step can use Mode B (copy base → config), every project needs an `app.base.config` created from its existing `app.config`. Do this in one shot using batch mode:

```powershell
# 1. Dry run first — see what would be created without writing anything
GenerateBaseConfigs.exe --batch C:\Dayforce\tipGit\src --test --verbose

# 2. Run for real
GenerateBaseConfigs.exe --batch C:\Dayforce\tipGit\src --verbose
```

This will, for each `.csproj` found recursively under the given directory:
- Strip `<assemblyBinding>` from `app.config` and write `app.base.config`
- Create or update the project directory's `.gitignore` to ignore `app.config`
- Remove `app.config` from the git index (`git rm --cached`)

### Step 4 — Commit the migration

```powershell
# Stage the new base configs and updated .gitignore files
git add "**/*.base.config"
git add "**/.gitignore"

# Verify — app.config files should appear as deleted (removed from index)
git status

# Commit
git commit -m "migration: introduce app.base.config for AT projects; remove app.config from source control"
```

> **Note:** `app.config` files remain on disk after this commit — they are just no longer tracked by git. Developers who already have the repo cloned do not need to do anything; the next build will regenerate `app.config` automatically.

### Step 5 — Verify the build

Build one of the migrated projects and confirm:

1. `RestoreBaseConfig` runs and logs `Mode B — restoring app.config from app.base.config` (first build) or `Mode B — app.config is up-to-date, skipping copy` (subsequent builds)
2. `WriteBindingRedirects` runs immediately after and injects binding redirects into the restored `app.config`
3. The final `app.config` in the output directory contains both your settings and the current binding redirects
4. `git status` shows no changes to `app.config` after the build

---

## CLI reference

```
GenerateBaseConfigs.exe [options]

Options:
  -f, --projectFile=VALUE   [Required] Path to the .csproj file to process.
      --batch=VALUE         Batch migration: process all .csproj files found
                              recursively under the given directory.
  -v, --verbose             Enable verbose logging.
      --test                Dry run — log what would be done without writing
                              any files or modifying the git index.
  -h, --help                Show this help and exit.

Examples:
  # Normal per-project invocation (called by MSBuild)
  GenerateBaseConfigs.exe -f MyProject\MyProject.csproj

  # Dry-run batch migration over an entire source tree
  GenerateBaseConfigs.exe --batch C:\repos\myrepo\src --test --verbose

  # Real batch migration
  GenerateBaseConfigs.exe --batch C:\repos\myrepo\src --verbose
```

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Bad arguments |
| 3 | Runtime error |
