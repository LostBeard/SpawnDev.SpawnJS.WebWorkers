using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SpawnDev.SpawnJS.WebWorkers.Build.Tasks
{
    /// <summary>
    /// Produces the two SpawnDev.SpawnJS.WebWorkers bundled JS entrypoints for the consuming .Net WASM
    /// app - main.classic.js (non-module) and main.module.js (module) - each with the event-holder folded
    /// in, so the app can be loaded via a plain &lt;script&gt; (CDN), importScripts(), or as a
    /// Worker/SharedWorker/ServiceWorker entrypoint.
    ///
    /// The consuming app is built with WasmBundlerFriendlyBootConfig=true (set by this package's props), so
    /// its OWN dotnet.js graph uses static imports (Rollup-followable). This task therefore bundles the app's
    /// OWN output - no separate inner build/publish - and the two JS files it emits reference the app's OWN
    /// _framework assets by their real (fingerprinted) names. Nothing is duplicated: exactly two JS files are
    /// added, reusing the existing _framework as-is. main.js is untouched and uses the same _framework.
    ///
    /// Mechanics:
    ///   1. Find the app's runtime entry _framework/dotnet(.&lt;fp&gt;).js (fingerprint varies per build).
    ///   2. Stage the event-holder + a loader (Rollup entry) into a temp dir; rewrite the loader's dotnet
    ///      import to an ABSOLUTE path to that dotnet.&lt;fp&gt;.js (so Rollup follows the app's real graph
    ///      without copying _framework or mutating the real wwwroot).
    ///   3. Run the shipped self-contained Rollup bundle (node rollup.bundled.mjs) to emit the two JS files.
    ///   4. Return them via BundleFiles.
    /// Requires Node.js on PATH. Disable with &lt;SpawnJSWebWorkersClassicBundle&gt;false&lt;/...&gt;.
    /// </summary>
    public class SpawnJSWebWorkersBundle : Task
    {
        /// <summary>
        /// A pre-assembled app output wwwroot that contains _framework (used for a publish, whose wwwroot is
        /// complete on disk). Ignored when BundleFromAssets is supplied (the build case assembles its own).
        /// </summary>
        public string SourceWwwroot { get; set; } = "";

        /// <summary>
        /// Build case: the resolved static web assets (@(StaticWebAsset)) to assemble into a temporary complete
        /// wwwroot before bundling. A `dotnet build` does not assemble _framework on disk, so each asset is
        /// copied by its RelativePath (fingerprinted names the bundler-friendly dotnet.js imports) into a temp
        /// wwwroot under StagingDir. When non-empty this takes precedence over SourceWwwroot.
        /// </summary>
        public ITaskItem[] BundleFromAssets { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Directory to write the two produced JS files into.</summary>
        [Required] public string OutputDir { get; set; } = "";

        /// <summary>The package buildcontent dir (has loader.js, rollup.bundled.mjs, event-holder).</summary>
        [Required] public string PackageContentDir { get; set; } = "";

        /// <summary>A private staging dir (under obj/) for the rewritten loader + event-holder Rollup entry.</summary>
        [Required] public string StagingDir { get; set; } = "";

        /// <summary>
        /// Extra files that the dotnet.js graph imports from the wwwroot root but that a plain build does not
        /// assemble on disk (RCL *.lib.module.js JS initializers). Copied into SourceWwwroot root before Rollup.
        /// Empty for a publish (the publish wwwroot is already complete).
        /// </summary>
        public ITaskItem[] StageFiles { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>node executable (default: node on PATH).</summary>
        public string NodeExe { get; set; } = "node";

        /// <summary>The two produced bundle JS files (main.classic.js + main.module.js), full paths.</summary>
        [Output] public ITaskItem[] BundleFiles { get; set; } = Array.Empty<ITaskItem>();

        public override bool Execute()
        {
            try
            {
                PackageContentDir = Path.GetFullPath(PackageContentDir);
                var loaderSrc = Path.Combine(PackageContentDir, "loader.js");
                var rollupBundle = Path.Combine(PackageContentDir, "rollup.bundled.mjs");
                var eventHolderSrc = Path.Combine(PackageContentDir, "spawndev.spawnjs.webworkers.event-holder.js");
                if (!File.Exists(loaderSrc) || !File.Exists(rollupBundle) || !File.Exists(eventHolderSrc))
                {
                    Log.LogError($"SpawnJS.WebWorkers bundle: missing tooling in '{PackageContentDir}' (loader.js / rollup.bundled.mjs / event-holder).");
                    return false;
                }

                var stagingDir = Path.GetFullPath(StagingDir);
                Directory.CreateDirectory(stagingDir);

                var outDir = Path.GetFullPath(OutputDir).TrimEnd('\\', '/');
                var classicOut = Path.Combine(outDir, "main.classic.js");
                var moduleOut = Path.Combine(outDir, "main.module.js");

                // Skip-when-unchanged cache: the expensive work (assemble + Rollup) is redone only when an input
                // that affects the bundle changed. The key mixes the tooling file stats with a signature of the
                // bundled inputs; for the build case that signature is each asset's RelativePath + Fingerprint
                // (the fingerprint IS a content hash, so any content change flips the key), for publish it is the
                // _framework file names + sizes. The stamp lives in obj (StagingDir), never in the shipped output.
                var cacheKey = ComputeCacheKey(loaderSrc, eventHolderSrc, rollupBundle);
                var stampFile = Path.Combine(stagingDir, ".spawnjs-bundle.cache");
                if (File.Exists(classicOut) && File.Exists(moduleOut) && File.Exists(stampFile)
                    && File.ReadAllText(stampFile).Trim() == cacheKey)
                {
                    BundleFiles = new ITaskItem[] { new TaskItem(classicOut), new TaskItem(moduleOut) };
                    Log.LogMessage(MessageImportance.High, "SpawnJS.WebWorkers bundle: up to date (cache hit) - skipping assemble + rollup.");
                    return true;
                }

                string wwwroot;
                if (BundleFromAssets.Length > 0)
                {
                    // BUILD case: assemble a complete wwwroot from the resolved static web assets. Each asset's
                    // physical file (Identity) is copied to <assembled>/<RelativePath>; the RelativePath carries
                    // the fingerprinted names that the bundler-friendly dotnet.js graph imports, so the assembled
                    // layout matches those imports exactly.
                    wwwroot = Path.Combine(stagingDir, "wwwroot");
                    if (Directory.Exists(wwwroot)) Directory.Delete(wwwroot, true);
                    Directory.CreateDirectory(wwwroot);
                    foreach (var asset in BundleFromAssets)
                    {
                        var rel = asset.GetMetadata("RelativePath");
                        if (string.IsNullOrEmpty(rel)) continue;
                        // The bundler-friendly dotnet.js graph only imports _framework/* and the RCL
                        // *.lib.module.js from the wwwroot root. Skip everything else (scoped css, other static
                        // files) and skip pre-compressed variants (.gz/.br / Content-Encoding assets).
                        if (asset.GetMetadata("AssetTraitName") == "Content-Encoding") continue;
                        var relLower = rel.Replace('\\', '/').ToLowerInvariant();
                        bool needed = relLower.StartsWith("_framework/") || relLower.EndsWith(".lib.module.js");
                        if (!needed) continue;
                        // RelativePath holds a fingerprint placeholder - name#[.{fingerprint}]!.ext or
                        // name#[.{fingerprint=abc}]?.ext. Substitute the asset's Fingerprint so the assembled
                        // on-disk name matches the import (and the dev server's served endpoint route).
                        var fp = asset.GetMetadata("Fingerprint");
                        var resolved = System.Text.RegularExpressions.Regex.Replace(
                            rel, @"#\[\.\{fingerprint(=[^}\]]*)?\}\][!?]?", string.IsNullOrEmpty(fp) ? "" : "." + fp);
                        // Physical bytes: the logical Identity path (bin/wwwroot) is not assembled yet at this
                        // stage; the real file is the source item (obj/webcil, runtime pack, or nuget cache).
                        var full = asset.GetMetadata("FullPath");
                        var orig = asset.GetMetadata("OriginalItemSpec");
                        var src = File.Exists(full) ? full : (File.Exists(orig) ? orig : null);
                        if (src == null) continue;
                        var dest = Path.Combine(wwwroot, resolved.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(src, dest, true);
                    }
                }
                else
                {
                    wwwroot = Path.GetFullPath(SourceWwwroot);
                }

                var framework = Path.Combine(wwwroot, "_framework");
                if (!Directory.Exists(framework))
                {
                    Log.LogError($"SpawnJS.WebWorkers bundle: _framework not found under '{wwwroot}'.");
                    return false;
                }

                // 1) Find the runtime entry dotnet(.<fp>).js (exclude dotnet.runtime.* and dotnet.native.*).
                var dotnetJs = Directory.GetFiles(framework, "dotnet.*.js")
                    .Select(Path.GetFileName)
                    .Where(n => !n.StartsWith("dotnet.runtime.", StringComparison.OrdinalIgnoreCase)
                             && !n.StartsWith("dotnet.native.", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n.Length) // "dotnet.js" (unfingerprinted) sorts before "dotnet.<fp>.js"
                    .FirstOrDefault();
                if (dotnetJs == null && File.Exists(Path.Combine(framework, "dotnet.js"))) dotnetJs = "dotnet.js";
                if (dotnetJs == null)
                {
                    Log.LogError($"SpawnJS.WebWorkers bundle: could not find a followable _framework/dotnet(.<fp>).js in '{framework}'. " +
                                 "Is WasmBundlerFriendlyBootConfig=true for this build?");
                    return false;
                }

                // 2a) Copy any missing wwwroot-root imports (RCL *.lib.module.js) into the real wwwroot root
                //     so the graph resolves (a plain build does not assemble these on disk; a publish already has them).
                foreach (var item in StageFiles)
                {
                    var src = item.GetMetadata("FullPath");
                    if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
                    var dest = Path.Combine(wwwroot, Path.GetFileName(src));
                    if (!File.Exists(dest)) File.Copy(src, dest, false);
                }

                // 2b) Stage the Rollup entry: event-holder beside a loader whose dotnet import is rewritten to
                //     an ABSOLUTE path to the app's real dotnet.<fp>.js (avoids copying _framework / touching wwwroot).
                File.Copy(eventHolderSrc, Path.Combine(stagingDir, "spawndev.spawnjs.webworkers.event-holder.js"), true);
                var absDotnet = Path.Combine(framework, dotnetJs).Replace('\\', '/');
                var loaderText = File.ReadAllText(loaderSrc)
                    .Replace("'./_framework/dotnet.js'", "'" + absDotnet + "'")
                    .Replace("\"./_framework/dotnet.js\"", "\"" + absDotnet + "\"");
                var stagedLoader = Path.Combine(stagingDir, "loader.js");
                File.WriteAllText(stagedLoader, loaderText);

                // 3) Run the self-contained Rollup bundle (node only). Emits ONLY main.module.js + main.classic.js.
                Directory.CreateDirectory(outDir);
                Log.LogMessage(MessageImportance.High, $"SpawnJS.WebWorkers bundle: rollup against {dotnetJs} -> main.classic.js + main.module.js...");
                var rollupArgs = $"\"{rollupBundle}\" \"{stagedLoader}\" \"{outDir}\"";
                if (!Run(NodeExe, rollupArgs, out var rollupLog))
                {
                    Log.LogError("SpawnJS.WebWorkers bundle: Node/Rollup failed. Node.js must be on PATH " +
                                 "(disable with <SpawnJSWebWorkersClassicBundle>false</SpawnJSWebWorkersClassicBundle>):\n" + rollupLog);
                    return false;
                }

                // 4) Return the two produced JS files.
                var results = new List<ITaskItem>();
                foreach (var name in new[] { "main.classic.js", "main.module.js" })
                {
                    var p = Path.Combine(outDir, name);
                    if (!File.Exists(p))
                    {
                        Log.LogError($"SpawnJS.WebWorkers bundle: expected output not produced: {p}");
                        return false;
                    }
                    results.Add(new TaskItem(p));
                }
                BundleFiles = results.ToArray();
                File.WriteAllText(stampFile, cacheKey); // record inputs so an unchanged rebuild is a cache hit
                Log.LogMessage(MessageImportance.High,
                    $"SpawnJS.WebWorkers bundle: wrote main.classic.js + main.module.js to {outDir} (reusing existing _framework, 0 assets copied)");
                return true;
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, true);
                return false;
            }
        }

        /// <summary>
        /// Builds a stable cache key from the inputs that determine the bundle: the tooling files (loader,
        /// event-holder, Rollup bundle) by size+mtime, plus a signature of the bundled inputs - each asset's
        /// RelativePath + Fingerprint for a build (the fingerprint is a content hash), or the _framework file
        /// names + sizes for a publish. Any relevant change flips the key.
        /// </summary>
        private string ComputeCacheKey(string loaderSrc, string eventHolderSrc, string rollupBundle)
        {
            var sb = new StringBuilder();
            void Stat(string label, string path)
            {
                var fi = new FileInfo(path);
                sb.Append(label).Append(':').Append(fi.Exists ? fi.Length : -1).Append(':')
                  .Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0).Append('\n');
            }
            Stat("loader", loaderSrc);
            Stat("eventholder", eventHolderSrc);
            Stat("rollup", rollupBundle);

            if (BundleFromAssets.Length > 0)
            {
                var sigs = new List<string>();
                foreach (var a in BundleFromAssets)
                {
                    if (a.GetMetadata("AssetTraitName") == "Content-Encoding") continue;
                    var rel = a.GetMetadata("RelativePath");
                    if (string.IsNullOrEmpty(rel)) continue;
                    var relLower = rel.Replace('\\', '/').ToLowerInvariant();
                    if (!(relLower.StartsWith("_framework/") || relLower.EndsWith(".lib.module.js"))) continue;
                    sigs.Add(rel + "|" + a.GetMetadata("Fingerprint"));
                }
                sigs.Sort(StringComparer.Ordinal);
                foreach (var s in sigs) sb.Append(s).Append('\n');
            }
            else if (!string.IsNullOrEmpty(SourceWwwroot))
            {
                var fw = Path.Combine(Path.GetFullPath(SourceWwwroot), "_framework");
                if (Directory.Exists(fw))
                {
                    var sigs = new List<string>();
                    foreach (var f in Directory.GetFiles(fw))
                        sigs.Add(Path.GetFileName(f) + "|" + new FileInfo(f).Length);
                    sigs.Sort(StringComparer.Ordinal);
                    foreach (var s in sigs) sb.Append(s).Append('\n');
                }
            }

            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
        }

        private bool Run(string exe, string args, out string combinedOutput)
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(OutputDir.TrimEnd('\\', '/')),
                };
                using var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                combinedOutput = sb.ToString();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                combinedOutput = sb.ToString() + Environment.NewLine + ex.Message;
                return false;
            }
        }
    }
}
