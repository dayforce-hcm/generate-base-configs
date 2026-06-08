# GitHub Actions NuGet Publish Pipeline — Design Spec

**Date:** 2026-06-05
**Author:** Dan Bonab
**Status:** Approved

## Problem Statement

`GenerateBaseConfigs` is currently published as a NuGet package via Azure Pipelines using Ceridian's internal `cicd-templates`. The goal is to replace that with a GitHub Actions workflow that publishes to NuGet.org, enabling public consumption without Ceridian ADO access.

## Solution Overview

A single `dotnet.yml` GitHub Actions workflow replaces both Azure Pipelines files. It follows the PolySharp structural pattern: build/test on every push and external PR; pack on those same events; publish to NuGet.org only when a `rel/x.y.z` branch is pushed.

## Workflow File: `.github/workflows/dotnet.yml`

### Triggers

```yaml
on: [push, pull_request]
```

All events are handled by one workflow. Publish is gated by a job condition, not a separate trigger.

### Duplicate-run guard

For `build-and-test` and `pack` jobs, runs are skipped for same-repo PRs to avoid double-executing on a branch push that also opens a PR (same pattern as PolySharp):

```yaml
if: >-
  github.event_name == 'push' ||
  github.event.pull_request.user.login != github.repository_owner
```

### Jobs

| Job | `needs` | Condition | Purpose |
|-----|---------|-----------|---------|
| `build-and-test` | — | Duplicate-run guard | `dotnet build -c Release` + `dotnet test -c Release` |
| `pack` | `build-and-test` | Duplicate-run guard | `dotnet pack -c Release`, uploads `nuget_packages` artifact |
| `publish` | `pack` | `push` event + `rel/` branch | Downloads artifact, `dotnet nuget push` to NuGet.org |

### Publish condition

```yaml
if: >-
  github.event_name == 'push' &&
  startsWith(github.ref, 'refs/heads/rel/')
```

### NuGet push step

```yaml
- name: Push to NuGet.org
  run: dotnet nuget push artifacts/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}
```

Requires a `NUGET_API_KEY` GitHub secret in the repo settings.

## Versioning

### Storage

Version is stored in `Directory.Build.props` as `<GenerateBaseConfigsVersion>`. This is the same property name referenced in `tipGit`'s `Directory.Build.props` for package consumption.

### Suffix behaviour

| Context | `VersionSuffix` | Resulting version |
|---------|-----------------|-------------------|
| PR build | `pr` | `1.0.0-pr` |
| Push to any non-`rel/` branch | `alpha` | `1.0.0-alpha` |
| Push to `rel/x.y.z` branch | _(empty)_ | `1.0.0` |

MSBuild conditions in `Directory.Build.props` set these suffixes using `GITHUB_EVENT_NAME` and `GITHUB_REF_NAME` env vars (same approach as PolySharp).

### `Directory.Build.props` additions

```xml
<!-- Version -->
<GenerateBaseConfigsVersion>1.0.0</GenerateBaseConfigsVersion>
<VersionPrefix>$(GenerateBaseConfigsVersion)</VersionPrefix>
<VersionSuffix Condition="'$(GITHUB_EVENT_NAME)' == 'pull_request'">pr</VersionSuffix>
<VersionSuffix Condition="'$(GITHUB_EVENT_NAME)' == 'push' AND !$(GITHUB_REF_NAME.StartsWith('rel/'))">alpha</VersionSuffix>

<!-- Package output -->
<PackageOutputPath>$(MSBuildThisFileDirectory)artifacts/</PackageOutputPath>

<!-- Deterministic/reproducible build support -->
<ContinuousIntegrationBuild Condition="'$(GITHUB_RUN_ID)' != ''">true</ContinuousIntegrationBuild>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
```

## .csproj Changes

`src/GenerateBaseConfigs/GenerateBaseConfigs.csproj` needs packaging metadata and tool packaging enabled:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>GenerateBaseConfigs</ToolCommandName>
<PackageId>GenerateBaseConfigs</PackageId>
<Description>MSBuild pre-step for GenerateBindingRedirects: maintains app.base.config files for Dayforce AT projects.</Description>
<Authors>Ceridian</Authors>
<PackageTags>msbuild;nuget;dayforce;binding-redirects</PackageTags>
<RepositoryUrl>https://github.com/dayforce-hcm/generate-base-configs</RepositoryUrl>
<RepositoryType>git</RepositoryType>
```

The `build/GenerateBaseConfigs.targets` file must be included in the package under `build/`. For a `PackAsTool` package this requires explicit `<Content>` items if not auto-included.

## Files Deleted

- `azure-pipelines-ci.yml`
- `azure-pipelines-pr.yml`

The Azure Pipelines build definitions in Ceridian's ADO org must be manually disabled by someone with ADO access — that is out of scope for this code change.

## Release Workflow (day-to-day)

1. Feature branch: implement changes, bump `<GenerateBaseConfigsVersion>` in `Directory.Build.props`
2. Merge to master → workflow runs build + test + pack (produces `1.0.0-alpha`, not published)
3. Push a `rel/1.0.0` branch → workflow produces `1.0.0` and pushes to NuGet.org
4. Delete or keep the `rel/` branch (it's a signal, not a long-lived branch)

## Prerequisites

- `NUGET_API_KEY` GitHub secret set in repo settings before first `rel/` push
- NuGet.org package ID `GenerateBaseConfigs` claimed/reserved under the `dayforce-hcm` org account (or a personal API key with publish rights)

## Out of Scope

- SonarQube integration (was in Azure Pipelines CI; not replicated)
- Symbol publishing (was in Azure Pipelines CI; not replicated)
- Disabling Azure Pipelines build definitions in ADO (manual step)
