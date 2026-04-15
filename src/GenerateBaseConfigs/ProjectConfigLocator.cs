using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;

namespace GenerateBaseConfigs;

/// <summary>
/// Locates app.config for a given .csproj file.
/// Ports the relevant subset of ProjectContext.Create from Dayforce.CSharp.ProjectAssets.
/// </summary>
internal static class ProjectConfigLocator
{
    // Web application ProjectTypeGuid — same constant as ProjectContext
    private const string WEB_APPLICATION_GUID = "{349c5851-65df-11da-9384-00065b846f21}";

    /// <summary>
    /// Returns config paths for the given project, or null if it is a web application (uses web.config).
    /// </summary>
    internal static ConfigPaths? FindConfigPaths(string projectFilePath)
    {
        var (nav, nsmgr) = GetProjectXPathNavigator(projectFilePath);
        var projectDir = Path.GetDirectoryName(projectFilePath)!;

        if (IsLegacyWebApplication(nav, nsmgr) || IsSdkWebApplication(nav, nsmgr))
            return null;

        // Expected path is always projectDir\app.config — mirrors ProjectContext.cs:67
        var expectedConfigFilePath = Path.GetFullPath(Path.Combine(projectDir, "app.config"));
        string? actualConfigFilePath = null;
        var sdkStyle = false;

        var attr = LocateAppConfigInProjectXml(nav, nsmgr);
        if (attr != null)
        {
            actualConfigFilePath = Path.GetFullPath(Path.Combine(projectDir, attr.Value));
        }
        else if (!(bool)nav.Evaluate("boolean(/p:Project/p:PropertyGroup/p:ProjectGuid)", nsmgr))
        {
            // SDK-style project — no ProjectGuid element, matches ProjectContext.cs:77
            sdkStyle = true;
            var candidate = Path.Combine(projectDir, "app.config");
            actualConfigFilePath = File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        return new ConfigPaths(expectedConfigFilePath, actualConfigFilePath, sdkStyle);
    }

    /// <summary>
    /// Locates the @Include attribute of a &lt;None Include="app.config"&gt; element (case-insensitive).
    /// Exact port of ProjectContext.LocateAppConfigInProjectXml at ProjectContext.cs:132.
    /// </summary>
    internal static XPathNavigator? LocateAppConfigInProjectXml(XPathNavigator nav, XmlNamespaceManager nsmgr) =>
        nav.Select(
            "/p:Project/p:ItemGroup/p:None[contains(translate(@Include,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'app.config')]/@Include",
            nsmgr)
        .Cast<XPathNavigator>()
        .FirstOrDefault(o =>
            o.Value.Equals("app.config", StringComparison.OrdinalIgnoreCase) ||
            o.Value.EndsWith("\\app.config", StringComparison.OrdinalIgnoreCase));

    private static bool IsLegacyWebApplication(XPathNavigator nav, XmlNamespaceManager nsmgr)
    {
        var projectTypeGuids = nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:ProjectTypeGuids/text()", nsmgr)?.Value;
        return projectTypeGuids?.Contains(WEB_APPLICATION_GUID, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSdkWebApplication(XPathNavigator nav, XmlNamespaceManager nsmgr)
    {
        var sdk = nav.SelectSingleNode("/p:Project/@p:Sdk", nsmgr)?.Value;
        return sdk?.StartsWith("MSBuild.SDK.SystemWeb", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Creates an XPathNavigator for the project file and sets up the namespace manager.
    /// Exact port of ProjectContext.GetProjectXPathNavigator at ProjectContext.cs:161.
    /// </summary>
    internal static (XPathNavigator nav, XmlNamespaceManager nsmgr) GetProjectXPathNavigator(string projectFilePath)
    {
        var doc = new XPathDocument(projectFilePath);
        var nav = doc.CreateNavigator()!;
        var nsmgr = new XmlNamespaceManager(nav.NameTable);
        nav.MoveToFollowing(XPathNodeType.Element);
        var ns = nav.GetNamespacesInScope(XmlNamespaceScope.Local).FirstOrDefault();
        nsmgr.AddNamespace("p", ns.Value ?? "");
        return (nav, nsmgr);
    }
}

/// <summary>
/// Result of locating app.config for a project.
/// Mirrors the ExpectedConfigFilePath / ActualConfigFilePath / SDKStyle properties of ProjectContext.
/// </summary>
internal record ConfigPaths(
    string ExpectedConfigFilePath,
    string? ActualConfigFilePath,
    bool SdkStyle);
