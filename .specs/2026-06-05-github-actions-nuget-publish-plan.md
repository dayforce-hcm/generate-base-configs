# GitHub Actions NuGet Publish Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Azure Pipelines CI/PR with a single GitHub Actions workflow that builds, tests, packs, and publishes `GenerateBaseConfigs` to NuGet.org on `rel/x.y.z` branch pushes.

**Architecture:** One workflow file (`.github/workflows/dotnet.yml`) with three jobs — `build-and-test`, `pack`, and `publish`. Build/test/pack run on every push and external PR. Publish only fires when a `rel/` branch is pushed. Version is set manually in `Directory.Build.props` as `<GenerateBaseConfigsVersion>`; a `-alpha`/`-pr` suffix is appended automatically outside `rel/` branches.

**Tech Stack:** .NET 9, GitHub Actions, `dotnet pack`, `dotnet nuget push`, NuGet.org

---

## File Map

| File | Action | What changes |
|------|--------|--------------|
| `Directory.Build.props` | Modify | Add versioning props, `PackageOutputPath`, `ContinuousIntegrationBuild` |
| `src/GenerateBaseConfigs/GenerateBaseConfigs.csproj` | Modify | Add `PackAsTool`, `ToolCommandName`, `PackageId`, package metadata, `build/` content item |
| `src/GenerateBaseConfigs/build/GenerateBaseConfigs.targets` | Modify | Fix exe path for `PackAsTool` NuGet layout (`tools/net9.0/any/`) |
| `.github/workflows/dotnet.yml` | Create | Full CI/publish workflow |
| `azure-pipelines-ci.yml` | Delete | Replaced by GitHub Actions |
| `azure-pipelines-pr.yml` | Delete | Replaced by GitHub Actions |

---

### Task 1: Add versioning and package output to `Directory.Build.props`

**Files:**
- Modify: `Directory.Build.props`

The version suffix logic uses MSBuild regex conditions (same technique as PolySharp) because MSBuild doesn't have a native `.StartsWith()` method call on property strings.

- [ ] **Step 1: Open `Directory.Build.props`** and replace its entire content with:

```xml
<Project>
  <PropertyGroup>
    <Product>GenerateBaseConfigs</Product>
    <AssemblyTitle>$(Product)</AssemblyTitle>
    <Description>Creates and maintains app.base.config files for Dayforce acceptance test projects.</Description>
    <Team>Release Engineering</Team>
  </PropertyGroup>

  <!-- Version: bump GenerateBaseConfigsVersion before pushing a rel/x.y.z branch -->
  <PropertyGroup>
    <GenerateBaseConfigsVersion>1.0.0</GenerateBaseConfigsVersion>
    <IsReleaseBranch>false</IsReleaseBranch>
  </PropertyGroup>

  <PropertyGroup>
    <ReleaseBranchRegex>^rel/(\d+\.\d+\.\d+)$</ReleaseBranchRegex>
    <IsReleaseBranch Condition="'$(GITHUB_EVENT_NAME)' == 'push' AND '$(GITHUB_REF_NAME)' != ''">$([System.Text.RegularExpressions.Regex]::IsMatch($(GITHUB_REF_NAME), $(ReleaseBranchRegex)))</IsReleaseBranch>
  </PropertyGroup>

  <PropertyGroup>
    <VersionPrefix>$(GenerateBaseConfigsVersion)</VersionPrefix>
    <!-- alpha on all pushes; pr on pull_request; empty on rel/ branch push -->
    <VersionSuffix Condition="'$(GITHUB_EVENT_NAME)' == 'push' AND '$(IsReleaseBranch)' != 'true'">alpha</VersionSuffix>
    <VersionSuffix Condition="'$(GITHUB_EVENT_NAME)' == 'pull_request'">pr</VersionSuffix>
  </PropertyGroup>

  <!-- Place all .nupkg files in artifacts/ at repo root -->
  <PropertyGroup>
    <PackageOutputPath>$(MSBuildThisFileDirectory)artifacts/</PackageOutputPath>
  </PropertyGroup>

  <!-- Deterministic/reproducible builds in CI -->
  <PropertyGroup>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_RUN_ID)' != ''">true</ContinuousIntegrationBuild>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <RepositoryUrl>https://github.com/dayforce-hcm/generate-base-configs</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>

  <Import Project="$(MSBuildThisFileDirectory)\Directory.Build.Template.props" Condition="'$(TF_BUILD)' != ''"/>
</Project>
```

- [ ] **Step 2: Verify build still works**

```bash
dotnet build -c Release
```

Expected: Build succeeds. No version errors.

- [ ] **Step 3: Verify version suffix applies correctly in local pack**

```bash
dotnet pack src/GenerateBaseConfigs/GenerateBaseConfigs.csproj -c Release
```

Expected: `artifacts/GenerateBaseConfigs.1.0.0.nupkg` is produced (no suffix locally, since `GITHUB_EVENT_NAME` is unset — that's correct).

- [ ] **Step 4: Commit**

```bash
git add Directory.Build.props
git commit -m "build: add versioning and package output path to Directory.Build.props"
```

---

### Task 2: Add package metadata and tool layout to the `.csproj`

**Files:**
- Modify: `src/GenerateBaseConfigs/GenerateBaseConfigs.csproj`

`PackAsTool=true` places the compiled exe under `tools/net9.0/any/` in the NuGet package. The `build/GenerateBaseConfigs.targets` file must be explicitly included via a `<Content>` item — it is not auto-included for tool packages.

- [ ] **Step 1: Replace `src/GenerateBaseConfigs/GenerateBaseConfigs.csproj` content with:**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>GenerateBaseConfigs</AssemblyName>
    <Nullable>enable</Nullable>

    <!-- NuGet tool package -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>GenerateBaseConfigs</ToolCommandName>
    <PackageId>GenerateBaseConfigs</PackageId>
    <Authors>Ceridian</Authors>
    <PackageTags>msbuild;nuget;dayforce;binding-redirects</PackageTags>
  </PropertyGroup>

  <!-- Include the MSBuild targets file in the package under build/ -->
  <ItemGroup>
    <Content Include="build\GenerateBaseConfigs.targets" Pack="true" PackagePath="build\" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="LibGit2Sharp" Version="0.31.0" />
    <PackageReference Include="Mono.Options" Version="6.12.0.148" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Build and pack**

```bash
dotnet build -c Release
dotnet pack src/GenerateBaseConfigs/GenerateBaseConfigs.csproj -c Release
```

Expected: `artifacts/GenerateBaseConfigs.1.0.0.nupkg` produced. No build errors.

- [ ] **Step 3: Inspect the `.nupkg` to confirm layout**

A `.nupkg` is a zip file. Extract it and verify the directory structure:

```bash
# Create a temp dir and extract
mkdir -p /tmp/nupkg-inspect
cp artifacts/GenerateBaseConfigs.1.0.0.nupkg /tmp/nupkg-inspect/GenerateBaseConfigs.1.0.0.zip
cd /tmp/nupkg-inspect && unzip -l GenerateBaseConfigs.1.0.0.zip
```

Expected output must contain both of:
- `tools/net9.0/any/GenerateBaseConfigs.exe`  (or `tools/net9.0/any/GenerateBaseConfigs.dll` on Linux)
- `build/GenerateBaseConfigs.targets`

If `tools/net9.0/any/` is not present, `PackAsTool=true` did not apply correctly — double-check the `<PackAsTool>` property is inside a `<PropertyGroup>` not an `<ItemGroup>`.

- [ ] **Step 4: Commit**

```bash
git add src/GenerateBaseConfigs/GenerateBaseConfigs.csproj
git commit -m "build: add PackAsTool, metadata, and build/ content item to csproj"
```

---

### Task 3: Fix the exe path in `GenerateBaseConfigs.targets`

**Files:**
- Modify: `src/GenerateBaseConfigs/build/GenerateBaseConfigs.targets`

The current targets file uses `$(MSBuildThisFileDirectory)..\tools\GenerateBaseConfigs.exe`. With `PackAsTool=true`, the exe lands at `tools/net9.0/any/GenerateBaseConfigs.exe` (not `tools/GenerateBaseConfigs.exe` directly). The path must be updated to match the actual NuGet package layout.

- [ ] **Step 1: Update `src/GenerateBaseConfigs/build/GenerateBaseConfigs.targets`**

Replace the `GenerateBaseConfigsExe` default path from:
```xml
<GenerateBaseConfigsExe Condition="'$(GenerateBaseConfigsExe)' == ''">$(MSBuildThisFileDirectory)..\tools\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
```
to:
```xml
<GenerateBaseConfigsExe Condition="'$(GenerateBaseConfigsExe)' == ''">$(MSBuildThisFileDirectory)..\tools\net9.0\any\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
```

Full file after change:
```xml
<?xml version="1.0" encoding="utf-8" ?>

<Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">

  <Target Name="RestoreBaseConfig"
          BeforeTargets="WriteBindingRedirects"
          Condition="'$(DesignTimeBuild)' != true">

    <PropertyGroup>
      <GenerateBaseConfigsExe Condition="'$(GenerateBaseConfigsExe)' == ''">$(MSBuildThisFileDirectory)..\tools\net9.0\any\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
    </PropertyGroup>

    <Error Condition="!Exists('$(GenerateBaseConfigsExe)')"
           Text="GenerateBaseConfigs.exe not found at $(GenerateBaseConfigsExe). Ensure the GenerateBaseConfigs NuGet package is installed." />

    <Exec Command="$(GenerateBaseConfigsExe) --projectFile $(MSBuildProjectFullPath) $(Extra_GenerateBaseConfigsFlags)" />

  </Target>

</Project>
```

- [ ] **Step 2: Re-pack and re-inspect to confirm the targets file is updated in the package**

```bash
dotnet pack src/GenerateBaseConfigs/GenerateBaseConfigs.csproj -c Release
mkdir -p /tmp/nupkg-inspect2
cp artifacts/GenerateBaseConfigs.1.0.0.nupkg /tmp/nupkg-inspect2/GenerateBaseConfigs.1.0.0.zip
cd /tmp/nupkg-inspect2 && unzip GenerateBaseConfigs.1.0.0.zip build/GenerateBaseConfigs.targets -d .
cat build/GenerateBaseConfigs.targets
```

Expected: The extracted targets file contains `tools\net9.0\any\GenerateBaseConfigs.exe` in the path.

- [ ] **Step 3: Run tests to confirm nothing broke**

```bash
dotnet test -c Release
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/GenerateBaseConfigs/build/GenerateBaseConfigs.targets
git commit -m "build: fix exe path in targets file for PackAsTool NuGet layout"
```

---

### Task 4: Create `.github/workflows/dotnet.yml`

**Files:**
- Create: `.github/workflows/dotnet.yml`

Three jobs: `build-and-test` → `pack` → `publish`. The duplicate-run guard on `build-and-test` and `pack` prevents double execution when a branch push also has an open PR. The `publish` job only runs on `rel/` branch pushes.

- [ ] **Step 1: Create `.github/workflows/dotnet.yml`**

```bash
mkdir -p .github/workflows
```

Create `.github/workflows/dotnet.yml` with this content:

```yaml
name: .NET

# Runs on every push and on PRs from forks.
# Same-repo PRs are skipped on build/pack (the push event already covers them).
# Publish only fires on rel/x.y.z branch pushes.
on: [push, pull_request]

jobs:

  build-and-test:
    if: >-
      github.event_name == 'push' ||
      github.event.pull_request.user.login != github.repository_owner
    runs-on: windows-latest
    steps:
    - name: Git checkout
      uses: actions/checkout@v4
    - name: Build
      run: dotnet build -c Release
    - name: Test
      run: dotnet test -c Release

  pack:
    if: >-
      github.event_name == 'push' ||
      github.event.pull_request.user.login != github.repository_owner
    needs: [build-and-test]
    runs-on: windows-latest
    steps:
    - name: Git checkout
      uses: actions/checkout@v4
    - name: Pack
      run: dotnet pack src/GenerateBaseConfigs/GenerateBaseConfigs.csproj -c Release
    - name: Upload package artifact
      uses: actions/upload-artifact@v4
      with:
        name: nuget_packages
        path: artifacts/*.nupkg
        if-no-files-found: error

  publish:
    if: >-
      github.event_name == 'push' &&
      startsWith(github.ref, 'refs/heads/rel/')
    needs: [pack]
    runs-on: windows-latest
    steps:
    - name: Download package artifact
      uses: actions/download-artifact@v4
      with:
        name: nuget_packages
        path: artifacts
    - name: Push to NuGet.org
      run: dotnet nuget push artifacts/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}
```

- [ ] **Step 2: Verify YAML is valid**

```bash
# GitHub Actions has no local linter, but we can catch obvious YAML errors:
python -c "import yaml, sys; yaml.safe_load(open('.github/workflows/dotnet.yml'))" 2>&1 || echo "YAML parse error"
```

Expected: No output (or "YAML parse error" if Python is unavailable — in that case, paste the file into https://yaml-online-parser.appspot.com to validate manually).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/dotnet.yml
git commit -m "ci: add GitHub Actions workflow for build, test, pack, and NuGet publish"
```

---

### Task 5: Delete Azure Pipelines files

**Files:**
- Delete: `azure-pipelines-ci.yml`
- Delete: `azure-pipelines-pr.yml`

- [ ] **Step 1: Delete both files**

```bash
git rm azure-pipelines-ci.yml azure-pipelines-pr.yml
```

- [ ] **Step 2: Commit**

```bash
git commit -m "ci: remove Azure Pipelines files — replaced by GitHub Actions"
```

---

### Task 6: End-to-end smoke test

Verify the full pipeline works before pushing to GitHub.

- [ ] **Step 1: Clean build from scratch**

```bash
dotnet clean
dotnet build -c Release
```

Expected: Build succeeds, no warnings about missing files or bad versions.

- [ ] **Step 2: Tests pass**

```bash
dotnet test -c Release
```

Expected: All tests green.

- [ ] **Step 3: Pack and inspect**

```bash
dotnet pack src/GenerateBaseConfigs/GenerateBaseConfigs.csproj -c Release
```

Expected: `artifacts/GenerateBaseConfigs.1.0.0.nupkg` exists.

Inspect the package:
```bash
mkdir -p /tmp/final-inspect
cp artifacts/GenerateBaseConfigs.1.0.0.nupkg /tmp/final-inspect/pkg.zip
cd /tmp/final-inspect && unzip -l pkg.zip
```

Checklist — all of these must be present:
- `build/GenerateBaseConfigs.targets`
- `tools/net9.0/any/GenerateBaseConfigs.exe` (or `.dll`)
- `GenerateBaseConfigs.nuspec` (auto-generated)

- [ ] **Step 4: Push the feature branch to GitHub and observe workflow**

```bash
git push origin ci/add-azure-pipelines
```

Go to https://github.com/dayforce-hcm/generate-base-configs/actions and confirm:
- The `build-and-test` and `pack` jobs run and go green
- The `publish` job does **not** appear (because this is not a `rel/` branch)

- [ ] **Step 5: Create a PR and confirm the workflow runs for it**

Open a PR from `ci/add-azure-pipelines` → `master`. Confirm `build-and-test` and `pack` run on the PR. `publish` must not run.

---

## Prerequisites Checklist (before first `rel/` push)

- [ ] `NUGET_API_KEY` secret added to the GitHub repo (Settings → Secrets and variables → Actions → New repository secret)
- [ ] NuGet.org package ID `GenerateBaseConfigs` is available or owned by the account associated with the API key
- [ ] Azure Pipelines build definitions in Ceridian ADO disabled manually (out of scope for this code change)

---

## Day-to-Day Release Workflow

1. Implement changes on a feature branch; bump `<GenerateBaseConfigsVersion>` in `Directory.Build.props`
2. Merge to `master` → workflow runs build + test + pack (produces `1.0.0-alpha`, not published)
3. Push a `rel/1.0.0` branch → workflow produces `1.0.0` and pushes to NuGet.org
4. `rel/` branch can be deleted after publish
