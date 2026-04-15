using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Options;

namespace GenerateBaseConfigs;

public static class Program
{
    public static int Main(string[] args)
    {
        string? projectFilePath = null;
        string? batchRootDir = null;
        bool help = false;
        bool verbose = false;
        bool dryRun = false;
        var extraArgs = new List<string>();

        var options = new OptionSet
        {
            { "h|help|?",       "Show help and exit.",                          _ => help = true },
            { "f|projectFile=", "[Required] Path to the .csproj file.",         v => projectFilePath = v },
            { "v|verbose",      "Enable verbose logging.",                      _ => verbose = true },
            { "test",           "Dry run — log what would be done, no writes.", _ => dryRun = true },
            { "batch=",         "Batch migration: process all .csproj files under this directory.", v => batchRootDir = v },
            { "<>",             extraArgs.Add }
        };

        try
        {
            options.Parse(args);
        }
        catch (OptionException ex)
        {
            Console.Error.WriteLine($"GenerateBaseConfigs: ERROR: {ex.Message}");
            return 2;
        }

        if (help)
        {
            Console.WriteLine("Usage: GenerateBaseConfigs [options]");
            Console.WriteLine();
            options.WriteOptionDescriptions(Console.Out);
            return 0;
        }

        if (extraArgs.Count > 0)
        {
            Console.Error.WriteLine($"GenerateBaseConfigs: ERROR: Unexpected arguments: {string.Join(" ", extraArgs)}");
            return 2;
        }

        try
        {
            if (batchRootDir != null)
                return RunBatch(batchRootDir, dryRun, verbose);

            if (projectFilePath == null)
            {
                Console.Error.WriteLine("GenerateBaseConfigs: ERROR: --projectFile is required.");
                return 2;
            }

            return Run(projectFilePath, dryRun, verbose);
        }
        catch (ApplicationException ex)
        {
            Console.Error.WriteLine($"GenerateBaseConfigs: ERROR: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GenerateBaseConfigs: ERROR: Unexpected error: {ex}");
            return 3;
        }
    }

    internal static int Run(string projectFilePath, bool dryRun, bool verbose)
    {
        if (!File.Exists(projectFilePath))
            throw new ApplicationException($"Project file not found: {projectFilePath}");

        var paths = ProjectConfigLocator.FindConfigPaths(projectFilePath);
        if (paths == null)
        {
            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: {projectFilePath} is a web project — skipping.");
            return 0;
        }

        // Use actual path when it exists on disk; fall back to expected
        var appConfigPath = paths.ActualConfigFilePath ?? paths.ExpectedConfigFilePath;

        if (!File.Exists(appConfigPath))
        {
            if (verbose)
                Console.WriteLine($"GenerateBaseConfigs: No app.config found for {Path.GetFileNameWithoutExtension(projectFilePath)} — skipping.");
            return 0;
        }

        var baseConfigDir = Path.GetDirectoryName(paths.ExpectedConfigFilePath)!;
        var baseConfigPath = Path.Combine(baseConfigDir, "app.base.config");

        if (File.Exists(baseConfigPath))
            AppConfigProcessor.RunModeB(appConfigPath, baseConfigPath, dryRun, verbose);
        else
            AppConfigProcessor.RunModeA(appConfigPath, baseConfigPath, dryRun, verbose);

        return 0;
    }

    internal static int RunBatch(string rootDirectory, bool dryRun, bool verbose)
    {
        if (!Directory.Exists(rootDirectory))
            throw new ApplicationException($"Batch directory not found: {rootDirectory}");

        Console.WriteLine($"GenerateBaseConfigs: Batch mode — scanning {rootDirectory}");

        var csprojFiles = Directory.EnumerateFiles(rootDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int created = 0, skipped = 0, failed = 0;

        foreach (var csproj in csprojFiles)
        {
            try
            {
                var paths = ProjectConfigLocator.FindConfigPaths(csproj);
                if (paths == null) { skipped++; continue; }

                var appConfigPath = paths.ActualConfigFilePath ?? paths.ExpectedConfigFilePath;
                if (!File.Exists(appConfigPath)) { skipped++; continue; }

                var baseConfigPath = Path.Combine(Path.GetDirectoryName(paths.ExpectedConfigFilePath)!, "app.base.config");
                if (File.Exists(baseConfigPath)) { skipped++; continue; }

                AppConfigProcessor.RunModeA(appConfigPath, baseConfigPath, dryRun, verbose);
                created++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GenerateBaseConfigs: FAILED {csproj}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"GenerateBaseConfigs: Batch complete — {created} created, {skipped} skipped, {failed} failed.");
        return failed > 0 ? 3 : 0;
    }
}
