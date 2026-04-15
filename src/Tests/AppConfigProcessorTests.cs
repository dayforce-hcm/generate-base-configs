using System;
using System.IO;
using System.Xml;
using GenerateBaseConfigs;
using Xunit;

namespace Tests;

public class AppConfigProcessorTests
{
    // ── XML unit tests (in-memory, no file system) ──────────────────────────

    [Fact]
    public void StripBinding_OnlyBindings_RemovesAssemblyBindingAndRuntime()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Foo" publicKeyToken="AABBCCDD" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-1.0.0.0" newVersion="1.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        var doc = Load(result!);
        Assert.Null(doc.SelectSingleNode("/configuration/runtime"));
        Assert.Null(doc.SelectSingleNode("/configuration/runtime/assemblyBinding"));
    }

    [Fact]
    public void StripBinding_RuntimeWithOtherContent_KeepsRuntimeRemovesAssemblyBinding()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <gcServer enabled="true" />
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Foo" publicKeyToken="AABBCCDD" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-1.0.0.0" newVersion="1.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        var doc = Load(result!);
        Assert.NotNull(doc.SelectSingleNode("/configuration/runtime"));
        Assert.NotNull(doc.SelectSingleNode("/configuration/runtime/gcServer"));
        Assert.Null(doc.SelectSingleNode("/configuration/runtime/assemblyBinding"));
    }

    [Fact]
    public void StripBinding_NoRuntime_ReturnsDocumentUnchanged()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <startup>
                <supportedRuntime version="v4.0" />
              </startup>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        var doc = Load(result!);
        Assert.NotNull(doc.SelectSingleNode("/configuration/startup"));
        Assert.Null(doc.SelectSingleNode("/configuration/runtime"));
    }

    [Fact]
    public void StripBinding_RuntimeWithoutAssemblyBinding_ReturnsDocumentUnchanged()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <gcServer enabled="true" />
              </runtime>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        var doc = Load(result!);
        Assert.NotNull(doc.SelectSingleNode("/configuration/runtime/gcServer"));
    }

    [Fact]
    public void StripBinding_NoConfigurationRoot_ReturnsNull()
    {
        const string xml = "<foo />";
        var result = Strip(xml);
        Assert.Null(result);
    }

    [Fact]
    public void StripBinding_PreservesXmlDeclaration()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Foo" publicKeyToken="AABBCCDD" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-1.0.0.0" newVersion="1.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        Assert.StartsWith("<?xml", result!.TrimStart());
    }

    [Fact]
    public void StripBinding_NoOrphanedBlankLines()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<configuration>\r\n  <appSettings>\r\n    <add key=\"k\" value=\"v\" />\r\n  </appSettings>\r\n  <runtime>\r\n    <assemblyBinding xmlns=\"urn:schemas-microsoft-com:asm.v1\">\r\n      <dependentAssembly>\r\n        <assemblyIdentity name=\"Foo\" publicKeyToken=\"AABB\" culture=\"neutral\" />\r\n        <bindingRedirect oldVersion=\"0.0.0.0-1.0.0.0\" newVersion=\"1.0.0.0\" />\r\n      </dependentAssembly>\r\n    </assemblyBinding>\r\n  </runtime>\r\n</configuration>";

        var result = Strip(xml);

        Assert.NotNull(result);
        // No consecutive blank lines
        Assert.DoesNotContain("\r\n\r\n\r\n", result!);
        Assert.DoesNotContain("\n\n\n", result!);
    }

    [Fact]
    public void StripBinding_WithCodeBase_RemovesEntireAssemblyBinding()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Foo" publicKeyToken="AABBCCDD" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-1.0.0.0" newVersion="1.0.0.0" />
                    <codeBase version="1.0.0.0" href="_BindingRedirects/Foo/1.0.0.0/Foo.dll" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """;

        var result = Strip(xml);

        Assert.NotNull(result);
        var doc = Load(result!);
        Assert.Null(doc.SelectSingleNode("//codeBase"));
        Assert.Null(doc.SelectSingleNode("//assemblyBinding"));
    }

    // ── Integration tests (temp directories, real file system) ──────────────

    [Fact]
    public void ModeA_CreatesBaseConfig()
    {
        using var tmp = new TempDir();
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);
        var baseConfig = tmp.Path("app.base.config");

        using var _ = new NoGitVersionControl.Scope(NoGitVersionControl.Instance);
        AppConfigProcessor.RunModeA(appConfig, baseConfig, dryRun: false, verbose: false);

        Assert.True(File.Exists(baseConfig));
        var doc = XmlLoad(baseConfig);
        Assert.Null(doc.SelectSingleNode("/configuration/runtime/assemblyBinding"));
        Assert.NotNull(doc.SelectSingleNode("/configuration/appSettings"));
    }

    [Fact]
    public void ModeA_CreatesGitIgnore()
    {
        using var tmp = new TempDir();
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);

        using var _ = new NoGitVersionControl.Scope(NoGitVersionControl.Instance);
        AppConfigProcessor.RunModeA(appConfig, tmp.Path("app.base.config"), dryRun: false, verbose: false);

        var gitIgnore = tmp.Path(".gitignore");
        Assert.True(File.Exists(gitIgnore));
        Assert.Contains("app.config", File.ReadAllText(gitIgnore), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModeA_DoesNotDuplicateGitIgnoreEntry()
    {
        using var tmp = new TempDir();
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);
        tmp.WriteFile(".gitignore", "app.config\r\n");

        using var _ = new NoGitVersionControl.Scope(NoGitVersionControl.Instance);
        AppConfigProcessor.RunModeA(appConfig, tmp.Path("app.base.config"), dryRun: false, verbose: false);

        var lines = File.ReadAllLines(tmp.Path(".gitignore"));
        Assert.Single(lines, l => l.Equals("app.config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ModeA_DryRunMakesNoWrites()
    {
        using var tmp = new TempDir();
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);

        using var _ = new NoGitVersionControl.Scope(NoGitVersionControl.Instance);
        AppConfigProcessor.RunModeA(appConfig, tmp.Path("app.base.config"), dryRun: true, verbose: false);

        Assert.False(File.Exists(tmp.Path("app.base.config")));
        Assert.False(File.Exists(tmp.Path(".gitignore")));
    }

    [Fact]
    public void ModeA_UntracksCalled_WhenFileIsTracked()
    {
        using var tmp = new TempDir();
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);
        var trackingGit = new TrackingGitVersionControl();

        using var _ = new NoGitVersionControl.Scope(trackingGit);
        AppConfigProcessor.RunModeA(appConfig, tmp.Path("app.base.config"), dryRun: false, verbose: false);

        Assert.Contains(appConfig, trackingGit.UntrackedFiles);
    }

    [Fact]
    public void ModeB_CopiesBaseConfigToAppConfig()
    {
        using var tmp = new TempDir();
        var baseConfig = tmp.WriteFile("app.base.config", SampleAppConfigNoBindings);
        var appConfig = tmp.Path("app.config");

        AppConfigProcessor.RunModeB(appConfig, baseConfig, dryRun: false, verbose: false);

        Assert.True(File.Exists(appConfig));
        Assert.Equal(File.ReadAllText(baseConfig), File.ReadAllText(appConfig));
    }

    [Fact]
    public void ModeB_OverwritesExistingAppConfig()
    {
        using var tmp = new TempDir();
        var baseConfig = tmp.WriteFile("app.base.config", SampleAppConfigNoBindings);
        var appConfig = tmp.WriteFile("app.config", SampleAppConfigWithBindings);

        AppConfigProcessor.RunModeB(appConfig, baseConfig, dryRun: false, verbose: false);

        Assert.Equal(File.ReadAllText(baseConfig), File.ReadAllText(appConfig));
    }

    [Fact]
    public void ModeB_DryRunMakesNoWrites()
    {
        using var tmp = new TempDir();
        var baseConfig = tmp.WriteFile("app.base.config", SampleAppConfigNoBindings);
        var appConfig = tmp.Path("app.config");

        AppConfigProcessor.RunModeB(appConfig, baseConfig, dryRun: true, verbose: false);

        Assert.False(File.Exists(appConfig));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? Strip(string xml) =>
        AppConfigProcessor.StripAssemblyBinding(LoadDoc(xml));

    private static XmlDocument LoadDoc(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlDocument Load(string xml) => LoadDoc(xml);

    private static XmlDocument XmlLoad(string path)
    {
        var doc = new XmlDocument();
        doc.Load(path);
        return doc;
    }

    private const string SampleAppConfigWithBindings = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <appSettings>
            <add key="Timeout" value="10" />
          </appSettings>
          <runtime>
            <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
              <dependentAssembly>
                <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30AD4FE6B2A6AEED" culture="neutral" />
                <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                <codeBase version="13.0.0.0" href="_BindingRedirects/Newtonsoft.Json/13.0.0.0/Newtonsoft.Json.dll" />
              </dependentAssembly>
            </assemblyBinding>
          </runtime>
        </configuration>
        """;

    private const string SampleAppConfigNoBindings = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <appSettings>
            <add key="Timeout" value="10" />
          </appSettings>
        </configuration>
        """;
}

/// <summary>Temporary directory that cleans up on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

    internal TempDir() => Directory.CreateDirectory(_dir);

    internal string Path(string fileName) => System.IO.Path.Combine(_dir, fileName);

    internal string WriteFile(string fileName, string content)
    {
        var path = Path(fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
