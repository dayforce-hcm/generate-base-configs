# Local test plan

Verifies that after migration, NuGet version bumps no longer produce `app.config` changes in git — only `app.base.config` is tracked in source control.

The test project used throughout is `SpecFlowTest.AA`, a representative AT project that already has `App.config` with binding redirects.

---

## Part 1 — Exe behavior (Mode A → Mode B)

### 1. Build the exe

```powershell
cd C:\Dayforce\GenerateBaseConfigs
dotnet build -c Release
```

### 2. Dry run Mode A on the test project

```powershell
.\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe `
  -f "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --test --verbose
```

Expected output: logs what *would* be created — no files written yet.

### 3. Run Mode A for real

```powershell
.\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe `
  -f "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --verbose
```

Expected output: `Mode A — creating app.base.config`

### 4. Verify git state in tipGit

```powershell
cd C:\Dayforce\tipGit
git status -- "src/Analytics/AT/SpecFlowTest.AA/"
```

Expected:
- `app.base.config` → new file (staged)
- `.gitignore` → new or modified (staged, contains `app.config`)
- `App.config` → deleted from the git index (staged as removed, still on disk)

### 5. Verify Mode B fires on the next invocation

Run the tool again on the same project — now that `app.base.config` exists it should switch to Mode B:

```powershell
cd C:\Dayforce\GenerateBaseConfigs
.\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe `
  -f "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --verbose
```

Expected output: `Mode B — restoring App.config from app.base.config`

After this, `git status` should still show no change to `App.config` — it is now gitignored.

---

## Part 2 — Full MSBuild pipeline (BeforeTargets hook)

Verifies that `RestoreBaseConfig` fires before `WriteBindingRedirects` in a real build. No NuGet pack/publish required — import the `.targets` file directly.

### 1. Temporarily wire the target into tipGit's AT Directory.Build.props

Open `C:\Dayforce\tipGit\Build\AT\Directory.Build.props` and add these two lines **before** the `<Import Project="$(UpstreamDirectoryBuildProps)" />` line:

```xml
<Import Project="C:\Dayforce\GenerateBaseConfigs\src\GenerateBaseConfigs\build\GenerateBaseConfigs.targets" />
<PropertyGroup>
  <GenerateBaseConfigsExe>C:\Dayforce\GenerateBaseConfigs\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
</PropertyGroup>
```

### 2. Build the test AT project and filter the log

```powershell
dotnet build "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  -c Release -v normal 2>&1 |
  Select-String "GenerateBaseConfigs|WriteBindingRedirects|RestoreBaseConfig"
```

Expected in the build log (in this order):
1. `RestoreBaseConfig` — logs `Mode B — restoring App.config from app.base.config`
2. `WriteBindingRedirects` — runs immediately after and injects fresh binding redirects

### 3. Simulate a NuGet version bump

Bump a package version in `C:\Dayforce\tipGit\Directory.Packages.props`, restore, rebuild, then check git:

```powershell
cd C:\Dayforce\tipGit
git status -- "src/Analytics/AT/SpecFlowTest.AA/App.config"
```

Expected: no output — `App.config` is gitignored and does not appear as modified even though `WriteBindingRedirects` rewrote it with updated redirects.

### 4. Revert the temporary wiring

Remove the four lines added to `Directory.Build.props` in step 1. The real integration will come through the NuGet package once it is published.

---

## Part 3 — End-to-end with locally-built GenerateBindingRedirects

Verifies the full two-tool pipeline using both repos built from source — no NuGet packages required.

**Responsibilities:**
- `GenerateBaseConfigs` (`RestoreBaseConfig` target) — copies `app.base.config` → `app.config` so `WriteBindingRedirects` receives a clean, redirect-free base.
- `GenerateBindingRedirects` (`WriteBindingRedirects` target) — reads `project.assets.json`, resolves version conflicts, and injects fresh `<dependentAssembly>` entries into `app.config`.

### 1. Build both tools from source

```powershell
dotnet build C:\Dayforce\GenerateBaseConfigs -c Release
dotnet build C:\Dayforce\GenerateBindingRedirects -c Release
```

### 2. Wire both tools into tipGit's AT Directory.Build.props

Open `C:\Dayforce\tipGit\Build\AT\Directory.Build.props` and add these four lines **before** the `<Import Project="$(UpstreamDirectoryBuildProps)" />` line:

```xml
<Import Project="C:\Dayforce\GenerateBaseConfigs\src\GenerateBaseConfigs\build\GenerateBaseConfigs.targets" />
<PropertyGroup>
  <GenerateBaseConfigsExe>C:\Dayforce\GenerateBaseConfigs\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
  <GenerateBindingRedirectsExe>C:\Dayforce\GenerateBindingRedirects\src\GenerateBindingRedirects\bin\Release\net9.0\GenerateBindingRedirects.exe</GenerateBindingRedirectsExe>
</PropertyGroup>
```

`GenerateBindingRedirectsExe` is set with `Condition="'' == ''"` inside the `WriteBindingRedirects` target, so setting it here takes precedence over the NuGet package default.

### 3. Build and verify the full pipeline

```powershell
dotnet build "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  -c Release -v normal "-p:Extra_GenerateBaseConfigsFlags=--verbose" 2>&1 |
  Select-String "RestoreBaseConfig|WriteBindingRedirects|Mode [AB]"
```

Expected in the build log (in this order):
1. `RestoreBaseConfig` — logs `Mode B — restoring app.config from app.base.config`
2. `WriteBindingRedirects` — runs immediately after, injects fresh binding redirects

### 4. Verify app.config has binding redirects injected by GenerateBindingRedirects

```powershell
Select-String "dependentAssembly" `
  "C:\Dayforce\tipGit\src\Analytics\AT\SpecFlowTest.AA\app.config" |
  Select-Object -First 3
```

Expected: `<dependentAssembly>` entries present — proof that `WriteBindingRedirects` ran and populated the file.

### 5. Verify git still does not track app.config

```powershell
cd C:\Dayforce\tipGit
git status -- "src/Analytics/AT/SpecFlowTest.AA/App.config"
```

Expected: only the staged deletion from Mode A — `app.config` on disk is invisible to git because it is gitignored.

### 6. Revert the temporary wiring

Remove the four lines added to `Directory.Build.props` in step 2.
