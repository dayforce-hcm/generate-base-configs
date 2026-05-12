using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace GenerateBaseConfigs;

internal static class AppConfigProcessor
{
    private static readonly XmlWriterSettings s_xmlWriterSettings = new()
    {
        Encoding = new UTF8Encoding(false)
    };

    // Replaceable for testing
    internal static IGitVersionControl GitVersionControl = new GitVersionControl();

    /// <summary>
    /// Mode A: app.base.config does not exist.
    /// Strips assemblyBinding from app.config and saves as app.base.config.
    /// Also gitignores app.config and untracks it from git.
    /// </summary>
    internal static bool RunModeA(string appConfigPath, string baseConfigPath, bool dryRun, bool verbose)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(appConfigPath);

        var strippedXml = StripAssemblyBinding(doc);
        if (strippedXml == null)
        {
            Console.Error.WriteLine($"GenerateBaseConfigs: WARNING: {appConfigPath} has no <configuration> root — skipping.");
            return false;
        }

        if (verbose)
            Console.WriteLine($"GenerateBaseConfigs: Mode A — creating {baseConfigPath}");

        if (!dryRun)
        {
            using var writer = XmlWriter.Create(baseConfigPath, s_xmlWriterSettings);
            // Write via XmlDocument round-trip to keep consistent encoding handling
            var outDoc = new XmlDocument { PreserveWhitespace = true };
            outDoc.LoadXml(strippedXml);
            outDoc.Save(writer);
        }

        UpdateGitIgnore(appConfigPath, dryRun, verbose);
        UntrackFromGit(appConfigPath, dryRun, verbose);
        return true;
    }

    /// <summary>
    /// Mode B: app.base.config exists.
    /// Copies app.base.config to app.config only when app.config does not exist (e.g. fresh clone).
    /// Skips the copy on subsequent builds so the file timestamp is not touched unnecessarily —
    /// an unnecessary write would force recompilation of the project and all its dependents.
    /// GenerateBindingRedirects updates the existing app.config in-place when redirects change.
    /// </summary>
    internal static void RunModeB(string appConfigPath, string baseConfigPath, bool dryRun, bool verbose)
    {
        if (File.Exists(appConfigPath))
        {
            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: Mode B — {appConfigPath} already exists, skipping copy");
            return;
        }

        if (verbose)
            Console.WriteLine($"GenerateBaseConfigs: Mode B — restoring {appConfigPath} from {baseConfigPath}");

        if (!dryRun)
            File.Copy(baseConfigPath, appConfigPath, overwrite: false);
    }

    /// <summary>
    /// Strips the &lt;assemblyBinding&gt; element (and its parent &lt;runtime&gt; if it becomes empty)
    /// from the given XmlDocument. Returns the serialized result, or null if the document has no
    /// &lt;configuration&gt; root (malformed).
    /// </summary>
    internal static string? StripAssemblyBinding(XmlDocument doc)
    {
        var cfg = doc.SelectSingleNode("/configuration");
        if (cfg == null)
            return null;

        var runtimeNode = cfg.ChildNodes.Cast<XmlNode>()
            .OfType<XmlElement>()
            .FirstOrDefault(n => n.LocalName == "runtime");

        if (runtimeNode == null)
            return Serialize(doc);

        var assemblyBindingNode = runtimeNode.ChildNodes.Cast<XmlNode>()
            .OfType<XmlElement>()
            .FirstOrDefault(n => n.LocalName == "assemblyBinding");

        if (assemblyBindingNode == null)
            return Serialize(doc);

        RemoveNodeWithAdjacentWhitespace(assemblyBindingNode);

        // If runtime has no more element children, remove it too
        if (!runtimeNode.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Any())
            RemoveNodeWithAdjacentWhitespace(runtimeNode);

        return Serialize(doc);
    }

    private static void RemoveNodeWithAdjacentWhitespace(XmlNode node)
    {
        var parent = node.ParentNode!;

        // Remove only the preceding whitespace sibling (the newline/indent that leads into
        // the removed node). The following whitespace sibling belongs to the next element
        // and must be kept so that element retains its own line.
        var prev = node.PreviousSibling;
        if (prev is XmlText or XmlWhitespace && string.IsNullOrWhiteSpace(prev.Value))
            parent.RemoveChild(prev);

        parent.RemoveChild(node);
    }

    private static string Serialize(XmlDocument doc)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, s_xmlWriterSettings))
            doc.Save(writer);
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }

    /// <summary>
    /// Ensures the project directory's .gitignore contains "app.config".
    /// Ported from BindingRedirectsWriter.UpdateOrAssertGitIgnore.
    /// </summary>
    private static void UpdateGitIgnore(string appConfigPath, bool dryRun, bool verbose)
    {
        var gitIgnorePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(appConfigPath)!, ".gitignore"));

        if (File.Exists(gitIgnorePath))
        {
            var lines = File.ReadAllLines(gitIgnorePath);
            if (lines.Contains("app.config", StringComparer.OrdinalIgnoreCase))
                return; // Already present — idempotent

            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: Updating {gitIgnorePath}");

            if (!dryRun)
                File.AppendAllText(gitIgnorePath, "app.config\r\n");
        }
        else
        {
            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: Creating {gitIgnorePath}");

            if (!dryRun)
                File.WriteAllText(gitIgnorePath, "app.config\r\n");
        }
    }

    /// <summary>
    /// Removes app.config from the git index (git rm --cached).
    /// The IsTracked check handles the "not in a git repo" case (returns false).
    /// </summary>
    private static void UntrackFromGit(string appConfigPath, bool dryRun, bool verbose)
    {
        if (!GitVersionControl.IsTracked(appConfigPath))
        {
            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: {appConfigPath} is not tracked by git — nothing to untrack.");
            return;
        }

        if (verbose)
            Console.WriteLine($"GenerateBaseConfigs: Untracking {appConfigPath} from git index.");

        if (!dryRun)
            GitVersionControl.UntrackFile(appConfigPath);
    }
}
