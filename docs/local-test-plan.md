# Local test plan

Verifies that after migration, NuGet version bumps no longer produce `app.config` changes in git — only `app.base.config` is tracked in source control.

The test project used throughout is `SpecFlowTest.AA`, a representative AT project that already has `App.config` with binding redirects.

---

## Step 0 — Set local repo roots

Define these once at the start of your PowerShell session. All commands below use them.

```powershell
$GBC = "C:\Dayforce\GenerateBaseConfigs"     # this repo
$TIP = "C:\Dayforce\tipGit"                  # consumer repo
$GBR = "C:\Dayforce\GenerateBindingRedirects" # binding redirects tool
```

Adjust any path that differs on your machine.

---

## Part 1 — Exe behavior (Mode A → Mode B)

### 1. Build the exe

```powershell
dotnet build $GBC -c Release
```

### 2. Dry run Mode A on the test project

```powershell
& "$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe" `
  -f "$TIP\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --test --verbose
```

Expected output: logs what *would* be created — no files written yet.

### 3. Run Mode A for real

```powershell
& "$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe" `
  -f "$TIP\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --verbose
```

Expected output: `Mode A — creating app.base.config`

### 4. Verify git state in tipGit

```powershell
cd $TIP
git status -- "src/Analytics/AT/SpecFlowTest.AA/"
```

Expected:
- `app.base.config` → new file (untracked, ready to stage)
- `.gitignore` → new file (untracked, contains `app.config`)
- `App.config` → deleted from the git index (staged as removed, still on disk)

### 5. Verify Mode B fires on the next invocation

Run the tool again on the same project — now that `app.base.config` exists it should switch to Mode B:

```powershell
& "$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe" `
  -f "$TIP\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  --verbose
```

Expected output: `Mode B — restoring App.config from app.base.config`

After this, `git status` should still show no change to `App.config` — it is now gitignored.

---

## Part 2 — Full MSBuild pipeline (BeforeTargets hook)

Verifies that `RestoreBaseConfig` fires before `WriteBindingRedirects` in a real build. No NuGet pack/publish required — import the `.targets` file directly.

### 1. Temporarily wire the target into tipGit's AT Directory.Build.props

Open `$TIP\Build\AT\Directory.Build.props` and add these four lines **before** the `<Import Project="$(UpstreamDirectoryBuildProps)" />` line.
The XML below uses your `$GBC` value — substitute the literal path if your editor does not expand it.

```xml
<Import Project="$GBC\src\GenerateBaseConfigs\build\GenerateBaseConfigs.targets" />
<PropertyGroup>
  <GenerateBaseConfigsExe>$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>
</PropertyGroup>
```

Or patch the file from PowerShell without opening an editor:

```powershell
$propsFile = "$TIP\Build\AT\Directory.Build.props"
$insert = "  <Import Project=`"$GBC\src\GenerateBaseConfigs\build\GenerateBaseConfigs.targets`" />`r`n" +
          "  <PropertyGroup>`r`n" +
          "    <GenerateBaseConfigsExe>$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>`r`n" +
          "  </PropertyGroup>`r`n  "
(Get-Content $propsFile -Raw) `
  -replace '  <Import Project="\$\(UpstreamDirectoryBuildProps\)" />', "$insert<Import Project=`"`$(UpstreamDirectoryBuildProps)`" />" |
  Set-Content $propsFile
```

### 2. Build the test AT project and filter the log

```powershell
dotnet build "$TIP\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  -c Release -v normal "-p:Extra_GenerateBaseConfigsFlags=--verbose" 2>&1 |
  Select-String "RestoreBaseConfig|WriteBindingRedirects|Mode [AB]"
```

Expected in the build log (in this order):
1. `RestoreBaseConfig` — logs `Mode B — restoring App.config from app.base.config`
2. `WriteBindingRedirects` — runs immediately after

### 3. Simulate a NuGet version bump

Bump a package version in `$TIP\Directory.Packages.props`, restore, rebuild, then check git:

```powershell
cd $TIP
git status -- "src/Analytics/AT/SpecFlowTest.AA/App.config"
```

Expected: no output — `App.config` is gitignored and does not appear as modified even though `WriteBindingRedirects` rewrote it with updated redirects.

### 4. Revert the temporary wiring

```powershell
cd $TIP
git restore "Build/AT/Directory.Build.props"
```

---

## Part 3 — End-to-end with locally-built GenerateBindingRedirects

Verifies the full two-tool pipeline using both repos built from source — no NuGet packages required.

**Responsibilities:**
- `GenerateBaseConfigs` (`RestoreBaseConfig` target) — copies `app.base.config` → `app.config` so `WriteBindingRedirects` receives a clean, redirect-free base.
- `GenerateBindingRedirects` (`WriteBindingRedirects` target) — reads `project.assets.json`, resolves version conflicts, and injects fresh `<dependentAssembly>` entries into `app.config`.

### 1. Build both tools from source

```powershell
dotnet build $GBC -c Release
dotnet build $GBR -c Release
```

### 2. Wire both tools into tipGit's AT Directory.Build.props

```powershell
$propsFile = "$TIP\Build\AT\Directory.Build.props"
$insert = "  <Import Project=`"$GBC\src\GenerateBaseConfigs\build\GenerateBaseConfigs.targets`" />`r`n" +
          "  <PropertyGroup>`r`n" +
          "    <GenerateBaseConfigsExe>$GBC\src\GenerateBaseConfigs\bin\Release\net9.0\GenerateBaseConfigs.exe</GenerateBaseConfigsExe>`r`n" +
          "    <GenerateBindingRedirectsExe>$GBR\src\GenerateBindingRedirects\bin\Release\net9.0\GenerateBindingRedirects.exe</GenerateBindingRedirectsExe>`r`n" +
          "  </PropertyGroup>`r`n  "
(Get-Content $propsFile -Raw) `
  -replace '  <Import Project="\$\(UpstreamDirectoryBuildProps\)" />', "$insert<Import Project=`"`$(UpstreamDirectoryBuildProps)`" />" |
  Set-Content $propsFile
```

`GenerateBindingRedirectsExe` is guarded by `Condition="'$(GenerateBindingRedirectsExe)' == ''"` inside the `WriteBindingRedirects` target, so setting it here takes precedence over the NuGet package default.

### 3. Build and verify the full pipeline

```powershell
dotnet build "$TIP\src\Analytics\AT\SpecFlowTest.AA\SpecFlowTest.AA.csproj" `
  -c Release -v normal "-p:Extra_GenerateBaseConfigsFlags=--verbose" 2>&1 |
  Select-String "RestoreBaseConfig|WriteBindingRedirects|Mode [AB]"
```

Expected in the build log (in this order):
1. `RestoreBaseConfig` — logs `Mode B — restoring app.config from app.base.config`
2. `WriteBindingRedirects` — runs immediately after, injects fresh binding redirects

### 4. Verify app.config has binding redirects injected by GenerateBindingRedirects

```powershell
Select-String "dependentAssembly" `
  "$TIP\src\Analytics\AT\SpecFlowTest.AA\app.config" |
  Select-Object -First 3
```

Expected: `<dependentAssembly>` entries present — proof that `WriteBindingRedirects` ran and populated the file.

### 5. Verify git still does not track app.config

```powershell
cd $TIP
git status -- "src/Analytics/AT/SpecFlowTest.AA/App.config"
```

Expected: only the staged deletion from Mode A — `app.config` on disk is invisible to git because it is gitignored.

### 6. Revert the temporary wiring

```powershell
cd $TIP
git restore "Build/AT/Directory.Build.props"
```
