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
                        var resolved = ResolveFingerprintedPath(rel, asset.GetMetadata("Fingerprint"));
                        // Physical bytes: the logical Identity path (bin/wwwroot) is not assembled yet at this
                        // stage; the real file is the source item (obj/webcil, runtime pack, or nuget cache).
                        var src = PickAssetSource(asset);
                        if (src == null)
                        {
                            // Never skip a needed asset silently: an absent one shows up later as a Rollup
                            // UNRESOLVED_IMPORT (or a "not produced by this build" error) that points at the
                            // importer instead of at the asset we could not find.
                            Log.LogWarning($"SpawnJS.WebWorkers bundle: no file on disk for static web asset '{rel}' " +
                                           $"(FullPath '{asset.GetMetadata("FullPath")}', OriginalItemSpec " +
                                           $"'{asset.GetMetadata("OriginalItemSpec")}'); it was NOT staged for the bundle.");
                            continue;
                        }
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

                // 2c) Fail with a readable message if the app's own boot graph points at files this build did not
                //     produce, instead of letting Rollup report it as an unresolved import.
                VerifyStagedImports(framework, dotnetJs);
                if (Log.HasLoggedErrors) return false;

                // 3) Run the self-contained Rollup bundle (node only). Emits ONLY main.module.js + main.classic.js.
                Directory.CreateDirectory(outDir);
                Log.LogMessage(MessageImportance.High, $"SpawnJS.WebWorkers bundle: rollup against {dotnetJs} -> main.classic.js + main.module.js...");
                // The wwwroot is passed so the bundler can keep each bundled module's ORIGINAL base URL:
                // the two entrypoints sit at the app root, but the modules they inline came from _framework/
                // (and _content/...), and the .Net runtime resolves both import.meta.url and JSHost.ImportAsync
                // module urls relative to where the importing module lived.
                var rollupArgs = $"\"{rollupBundle}\" \"{stagedLoader}\" \"{outDir}\" \"{wwwroot.TrimEnd('\\', '/')}\"";
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

                // 4b) Self-heal stale references the bundler-friendly boot module embedded into the output. On an
                //     incremental build the SDK can regenerate dotnet.<fp>.js with a NEW fingerprint but leave a
                //     PREVIOUS build's resource list inside it (a static-web-assets metadata-vs-content lag, seen with
                //     RCL dependencies). Rollup inlines that verbatim, so the bundle carries, for a recompiled asset:
                //       (a) new URL("_framework/App.<oldfp>.wasm")  - a URL to a file that no longer exists, AND
                //       (b) a { "name": "App.<oldfp>.wasm", "hash": "sha256-<old>" } boot-manifest entry.
                //     A worker booting from the bundle then fetches the CURRENT file but validates it against the OLD
                //     hash -> "Failed to find a valid digest in the 'integrity' attribute ... blocked" (exactly the
                //     error a hard reload throws; a stale build silently loads until then). The assembled _framework
                //     here is ground truth (names from each asset's fresh Fingerprint), so rewrite both forms: any
                //     absent _framework URL -> the file that matches it apart from the fingerprint segment, and any
                //     boot-manifest entry whose name is stale -> the current name AND the current file's real hash.
                var stagedNames = new HashSet<string>(
                    Directory.GetFiles(framework).Select(f => Path.GetFileName(f)!), StringComparer.Ordinal);
                var hashCache = new Dictionary<string, string>(StringComparer.Ordinal);
                string CurrentHash(string fileName)
                {
                    if (!hashCache.TryGetValue(fileName, out var h))
                    {
                        using var sha = System.Security.Cryptography.SHA256.Create();
                        using var fs = File.OpenRead(Path.Combine(framework, fileName));
                        h = "sha256-" + Convert.ToBase64String(sha.ComputeHash(fs));
                        hashCache[fileName] = h;
                    }
                    return h;
                }
                int totalRemapped = 0;
                foreach (var item in results)
                {
                    var path = item.ItemSpec;
                    var text = File.ReadAllText(path);
                    var remapped = 0;
                    // (a) URL references: new URL("_framework/<file>", ...)
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text, @"_framework/([^""'`\s,)\\]+)", m =>
                        {
                            var target = RemapStaleFrameworkRef(m.Groups[1].Value, stagedNames);
                            if (target == null) return m.Value;
                            remapped++;
                            return "_framework/" + target;
                        });
                    // (b) boot-manifest entries: "name": "<file>", "hash": "sha256-<...>"  (name+hash rewritten together)
                    text = System.Text.RegularExpressions.Regex.Replace(
                        text, @"(""name""\s*:\s*"")([^""]+)(""\s*,\s*""hash""\s*:\s*"")sha256-[A-Za-z0-9+/=]+("")", m =>
                        {
                            var target = RemapStaleFrameworkRef(m.Groups[2].Value, stagedNames);
                            if (target == null) return m.Value; // name already current -> trust its hash
                            remapped++;
                            return m.Groups[1].Value + target + m.Groups[3].Value + CurrentHash(target) + m.Groups[4].Value;
                        });
                    if (remapped > 0)
                    {
                        File.WriteAllText(path, text);
                        totalRemapped += remapped;
                        Log.LogMessage(MessageImportance.High,
                            $"SpawnJS.WebWorkers bundle: healed {remapped} stale reference(s) in {Path.GetFileName(path)} (SDK boot-config fingerprint lag).");
                    }
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

        /// <summary>
        /// If <paramref name="refName"/> (a filename referenced under <c>_framework/</c> in the rollup output) is
        /// NOT one of the assembled files, returns the assembled file that differs from it only in the fingerprint
        /// segment - same segment count, same name parts and extension, only the second-to-last segment differs -
        /// i.e. the current-build file the stale boot module should have named. Returns null when the reference is
        /// already valid, has a different shape (e.g. the unfingerprinted variant), or is ambiguous, so only a
        /// genuinely-broken reference with exactly one fingerprint-drift match is ever rewritten.
        /// </summary>
        /// <summary>
        /// Resolve a static web asset's <c>RelativePath</c> to the name it is actually served (and imported)
        /// under, by substituting its fingerprint placeholder.
        /// </summary>
        /// <remarks>
        /// A placeholder is either <c>name#[.{fingerprint}]!.ext</c> - fill in from the asset's own
        /// <c>Fingerprint</c> metadata - or <c>name#[.{fingerprint=abc}]!.ext</c>, which STATES the value and
        /// must win. The two are not interchangeable: the SDK writes the explicit form whenever the placeholder
        /// names a different asset's fingerprint than the item's own. The compressed variants make that visible -
        /// <c>...#[.{fingerprint=pw7pg93i7q}]!.lib.module.js.gz</c> carries <c>Fingerprint=mso25y89w2</c> (the
        /// fingerprint of the .gz itself), and is served as <c>....pw7pg93i7q.lib.module.js.gz</c>. Taking the
        /// item's own Fingerprint there stages the file under a name nothing imports, which surfaces much later
        /// as an unresolved import of the app's own boot graph.
        /// </remarks>
        internal static string ResolveFingerprintedPath(string relativePath, string fingerprint)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                relativePath, @"#\[\.\{fingerprint(?:=(?<value>[^}\]]*))?\}\][!?]?", m =>
                {
                    var value = m.Groups["value"].Success ? m.Groups["value"].Value : fingerprint;
                    return string.IsNullOrEmpty(value) ? "" : "." + value;
                });
        }

        /// <summary>
        /// Choose the physical file to stage for a static web asset.
        /// </summary>
        /// <remarks>
        /// An asset has two candidate paths: <c>Identity</c>/<c>FullPath</c> is its LOGICAL location, which for a
        /// generated asset is a COPY in the build output, and <c>OriginalItemSpec</c> is the producer that copy
        /// came from (typically under obj/). Preferring the copy is not safe: MSBuild does not always refresh it.
        /// Upgrading a package that ships a <c>*.lib.module.js</c> JS initializer changes that initializer's
        /// fingerprint and the SDK regenerates the bundler-friendly <c>obj/dotnet.js</c>, but the
        /// <c>bin/&lt;cfg&gt;/&lt;tfm&gt;/wwwroot/_framework/dotnet.js</c> copy is left behind still statically importing the
        /// OLD fingerprinted name - and since fingerprints are fixed width the stale copy has the SAME LENGTH, so
        /// no size-based check notices. Staging those bytes makes Rollup fail with a bare
        /// "Could not resolve ./../&lt;name&gt;.&lt;oldfp&gt;.lib.module.js", and the very same stale copy is what a dev
        /// server would serve - under a route and integrity computed from the FRESH content, so the app is broken
        /// whether or not it is bundled. Observed 2026-08-18 (Gemineachy, SpawnJS 2.0.5 -> 2.1.7).
        ///
        /// The asset's own <c>Integrity</c> is computed from the producer, so it settles which candidate is
        /// authoritative: take the first candidate whose SHA-256 matches. Assets whose two paths are the same
        /// file (everything from a package) are unaffected.
        /// </remarks>
        private string? PickAssetSource(ITaskItem asset)
        {
            var candidates = new List<string>(2);
            void Add(string? p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try { p = Path.GetFullPath(p!); } catch { return; }   // OriginalItemSpec is often project-relative
                if (File.Exists(p) && !candidates.Contains(p, StringComparer.OrdinalIgnoreCase)) candidates.Add(p);
            }
            Add(asset.GetMetadata("FullPath"));
            Add(asset.GetMetadata("OriginalItemSpec"));
            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            var integrity = asset.GetMetadata("Integrity");
            if (string.IsNullOrEmpty(integrity)) return candidates[0];
            foreach (var c in candidates)
            {
                if (!string.Equals(Sha256Base64(c), integrity, StringComparison.Ordinal)) continue;
                if (c != candidates[0])
                    Log.LogMessage(MessageImportance.High,
                        $"SpawnJS.WebWorkers bundle: '{asset.GetMetadata("RelativePath")}' - the build-output copy " +
                        $"'{candidates[0]}' does not match the asset's recorded content; staging the producer '{c}' instead.");
                return c;
            }
            // Neither matched. Do not fail the build over it - stage the usual choice and say so, loudly enough
            // that a resulting Rollup resolve error is traceable to this rather than looking like a tooling bug.
            Log.LogWarning($"SpawnJS.WebWorkers bundle: no candidate for '{asset.GetMetadata("RelativePath")}' matches its " +
                           $"recorded Integrity ({integrity}); staging '{candidates[0]}'. If the bundle fails to resolve an " +
                           "import, this asset's build output is stale - a rebuild (or deleting it) will regenerate it.");
            return candidates[0];
        }

        private static string Sha256Base64(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToBase64String(sha.ComputeHash(fs));
        }

        /// <summary>
        /// Check that every relative static import in the assembled <c>dotnet(.&lt;fp&gt;).js</c> resolves to a file
        /// that was actually assembled, and report the missing ones. Rollup would fail anyway, but only with an
        /// UNRESOLVED_IMPORT stack trace that says nothing about WHY the file is absent; naming the importer and
        /// the missing target turns a tooling-looking failure back into the build problem it is.
        /// </summary>
        private void VerifyStagedImports(string framework, string dotnetJs)
        {
            var entry = Path.Combine(framework, dotnetJs);
            if (!File.Exists(entry)) return;
            var text = File.ReadAllText(entry);
            var missing = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                text, @"(?:from|import)\s*\(?\s*[""'](?<spec>\.[^""']+)[""']"))
            {
                var spec = m.Groups["spec"].Value.Split('?', '#')[0];
                string resolved;
                try { resolved = Path.GetFullPath(Path.Combine(framework, spec.Replace('/', Path.DirectorySeparatorChar))); }
                catch { continue; }
                if (!File.Exists(resolved) && !missing.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                    missing.Add(resolved);
            }
            if (missing.Count == 0) return;
            // Name what IS present under the same folder with the same shape, so a fingerprint mismatch (the
            // staged name differing from the imported one) is visible in the error rather than guessed at.
            var detail = new StringBuilder();
            foreach (var m in missing)
            {
                detail.Append("\n  ").Append(m);
                var dir = Path.GetDirectoryName(m);
                var near = Directory.Exists(dir) ? NearestNames(dir!, Path.GetFileName(m)) : new List<string>();
                if (near.Count > 0)
                    detail.Append("\n      staged alongside it: ").Append(string.Join(", ", near));
            }
            Log.LogError($"SpawnJS.WebWorkers bundle: _framework/{dotnetJs} statically imports {missing.Count} file(s) that " +
                         "were not produced by this build:" + detail +
                         "\nThat file is the app's OWN boot graph, so this is a stale build output, not a bundler problem - " +
                         "most often a build-output copy left over from a previous package version. Rebuild the project " +
                         "(or delete the offending file) to regenerate it.");
        }

        /// <summary>
        /// Staged file names in <paramref name="dir"/> that match <paramref name="fileName"/> apart from a
        /// fingerprint segment - i.e. the same name with one dotted segment added, removed or changed.
        /// </summary>
        private static List<string> NearestNames(string dir, string fileName)
        {
            var wanted = fileName.Split('.');
            var result = new List<string>();
            foreach (var candidate in Directory.GetFiles(dir).Select(Path.GetFileName))
            {
                var parts = candidate!.Split('.');
                if (Math.Abs(parts.Length - wanted.Length) > 1) continue;
                // Same first segment and same extension, differing only in the middle -> a fingerprint drift.
                if (!string.Equals(parts[0], wanted[0], StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(parts[parts.Length - 1], wanted[wanted.Length - 1], StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(candidate);
                if (result.Count == 4) break;
            }
            return result;
        }

        private static string? RemapStaleFrameworkRef(string refName, HashSet<string> stagedNames)
        {
            if (stagedNames.Contains(refName)) return null;   // already valid - most references
            var parts = refName.Split('.');
            if (parts.Length < 3) return null;                // need at least name.<fingerprint>.ext
            string? match = null;
            foreach (var staged in stagedNames)
            {
                var sp = staged.Split('.');
                if (sp.Length != parts.Length) continue;      // different shape (unfingerprinted variant, etc.)
                bool same = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i == parts.Length - 2) continue;      // the fingerprint segment is allowed to differ
                    if (!string.Equals(parts[i], sp[i], StringComparison.Ordinal)) { same = false; break; }
                }
                if (!same) continue;
                if (match != null) return null;               // more than one candidate - do not guess
                match = staged;
            }
            return match;
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
