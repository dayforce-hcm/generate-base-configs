using System.IO;
using System.Reflection;
using GenerateBaseConfigs;
using Xunit;

namespace Tests;

public class ProjectConfigLocatorTests
{
    // Resolve Input fixture paths relative to the Tests source directory.
    // Assembly is at bin/Release/net9.0/ — go up 4 levels to reach src/Tests/.
    private static string InputDir => Path.GetFullPath(
        Path.Combine(Assembly.GetExecutingAssembly().Location, @"..\..\..\..\Input"));

    private static string Csproj(string folder, string name) =>
        Path.Combine(InputDir, folder, name + ".csproj");

    [Fact]
    public void Legacy_ExplicitInclude_ReturnsBothPathsEqual()
    {
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("LegacyProject", "LegacyProject"));

        Assert.NotNull(result);
        Assert.NotNull(result!.ActualConfigFilePath);
        Assert.Equal(
            Path.GetFullPath(result.ExpectedConfigFilePath),
            Path.GetFullPath(result.ActualConfigFilePath!));
        Assert.False(result.SdkStyle);
    }

    [Fact]
    public void Legacy_LinkedInclude_ActualDiffersFromExpected()
    {
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("LegacyProjectLinked", "LegacyProjectLinked"));

        Assert.NotNull(result);
        Assert.NotNull(result!.ActualConfigFilePath);
        Assert.NotEqual(
            Path.GetFullPath(result.ExpectedConfigFilePath),
            Path.GetFullPath(result.ActualConfigFilePath!));
        // Expected is always projectDir\app.config
        Assert.EndsWith("LegacyProjectLinked\\app.config", result.ExpectedConfigFilePath, System.StringComparison.OrdinalIgnoreCase);
        // Actual resolves to SharedConfigs\app.config
        Assert.EndsWith("SharedConfigs\\app.config", result.ActualConfigFilePath!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_NoAppConfigInclude_ActualIsNull()
    {
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("LegacyProjectNoConfig", "LegacyProjectNoConfig"));

        Assert.NotNull(result);
        Assert.Null(result!.ActualConfigFilePath);
        // ExpectedConfigFilePath is always set
        Assert.EndsWith("app.config", result.ExpectedConfigFilePath, System.StringComparison.OrdinalIgnoreCase);
        Assert.False(result.SdkStyle);
    }

    [Fact]
    public void SdkStyle_AppConfigOnDisk_ReturnsPath()
    {
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("SdkProject", "SdkProject"));

        Assert.NotNull(result);
        Assert.True(result!.SdkStyle);
        Assert.NotNull(result.ActualConfigFilePath);
        Assert.True(File.Exists(result.ActualConfigFilePath));
    }

    [Fact]
    public void SdkStyle_NoAppConfigOnDisk_ActualIsNull()
    {
        // LegacyProjectNoConfig has no ProjectGuid → treated as SDK style
        // but no app.config on disk either
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("LegacyProjectNoConfig", "LegacyProjectNoConfig"));

        Assert.NotNull(result);
        // ActualConfigFilePath null because neither include nor disk file
        Assert.Null(result!.ActualConfigFilePath);
    }

    [Fact]
    public void WebProject_LegacyProjectTypeGuids_ReturnsNull()
    {
        var result = ProjectConfigLocator.FindConfigPaths(Csproj("WebProject", "WebProject"));
        Assert.Null(result);
    }
}
