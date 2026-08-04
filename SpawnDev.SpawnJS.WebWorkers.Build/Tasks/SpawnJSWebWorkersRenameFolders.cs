using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpawnDev.SpawnJS.WebWorkers.Build.Tasks
{
    /// <summary>
    /// Publish-only, opt-in: rename the underscore-prefixed output folders (_framework, and optionally
    /// _content) so a published .Net WASM app can run where paths starting with '_' are illegal - most
    /// notably a browser extension (root files/folders cannot start with '_'; a leading '.' is likewise
    /// unsafe). Renaming makes the classic bundle (main.classic.js) usable as an extension background
    /// ServiceWorker / content-script runtime.
    ///
    /// It renames the physical folder AND rewrites every textual reference to it in the published
    /// wwwroot (js/mjs/html/json/css/webmanifest/map) - including this package's own main.classic.js /
    /// main.module.js, whose asset URLs point at "_framework/&lt;name&gt;". Binary assets (.wasm/.dat/…)
    /// are left untouched; they contain no path strings.
    ///
    /// This is inherently sharp: an app that hard-codes "_framework"/"_content" somewhere the rewrite
    /// cannot see (e.g. a fetch built from a variable) will break. Hence opt-in and publish-only.
    /// </summary>
    public class SpawnJSWebWorkersRenameFolders : Task
    {
        /// <summary>The published wwwroot to operate on ($(PublishDir)wwwroot).</summary>
        [Required] public string WwwrootDir { get; set; } = "";

        /// <summary>New name for the "_framework" folder (empty = leave as-is). Must not start with '_' or '.'.</summary>
        public string FrameworkFolderName { get; set; } = "";

        /// <summary>New name for the "_content" folder (empty = leave as-is). Must not start with '_' or '.'.</summary>
        public string ContentFolderName { get; set; } = "";

        public override bool Execute()
        {
            try
            {
                var wwwroot = Path.GetFullPath(WwwrootDir);
                if (!Directory.Exists(wwwroot))
                {
                    Log.LogError($"SpawnJS.WebWorkers rename: wwwroot not found: {wwwroot}");
                    return false;
                }

                var renames = new List<(string Old, string New)>();
                if (!string.IsNullOrWhiteSpace(FrameworkFolderName)) renames.Add(("_framework", FrameworkFolderName.Trim()));
                if (!string.IsNullOrWhiteSpace(ContentFolderName)) renames.Add(("_content", ContentFolderName.Trim()));
                if (renames.Count == 0) return true;

                foreach (var (old, nw) in renames)
                {
                    if (nw.StartsWith("_") || nw.StartsWith("."))
                    {
                        Log.LogError($"SpawnJS.WebWorkers rename: target name '{nw}' still starts with '_' or '.', which defeats the purpose. " +
                                     "Choose a name that does not start with '_' or '.'.");
                        return false;
                    }
                    if (nw.IndexOfAny(new[] { '/', '\\' }) >= 0)
                    {
                        Log.LogError($"SpawnJS.WebWorkers rename: target name '{nw}' must be a single folder name (no '/' or '\\').");
                        return false;
                    }
                }

                // 1) Rewrite textual references in the published wwwroot. Replace the token WITH its bounding
                //    separators to avoid matching an unrelated identifier substring: "_framework/" and "/_framework"
                //    and quoted "_framework" cover the URL/path forms the runtime and the bundle emit.
                var textExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".js", ".mjs", ".html", ".htm", ".json", ".css", ".webmanifest", ".map" };
                int filesChanged = 0;
                foreach (var file in Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories))
                {
                    if (!textExt.Contains(Path.GetExtension(file))) continue;
                    var text = File.ReadAllText(file);
                    var updated = text;
                    foreach (var (old, nw) in renames)
                    {
                        updated = updated
                            .Replace(old + "/", nw + "/")     // _framework/asset
                            .Replace("/" + old, "/" + nw)     // ./_framework , /_framework
                            .Replace("\"" + old + "\"", "\"" + nw + "\"")  // bare "_framework"
                            .Replace("'" + old + "'", "'" + nw + "'");
                    }
                    if (updated != text) { File.WriteAllText(file, updated); filesChanged++; }
                }

                // 2) Rename the physical folders.
                foreach (var (old, nw) in renames)
                {
                    var oldDir = Path.Combine(wwwroot, old);
                    var newDir = Path.Combine(wwwroot, nw);
                    if (!Directory.Exists(oldDir)) continue;
                    if (Directory.Exists(newDir)) Directory.Delete(newDir, true);
                    Directory.Move(oldDir, newDir);
                }

                Log.LogMessage(MessageImportance.High,
                    $"SpawnJS.WebWorkers: renamed {string.Join(", ", renames.Select(r => r.Old + " -> " + r.New))} " +
                    $"in {wwwroot} ({filesChanged} file(s) rewritten).");
                return true;
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, true);
                return false;
            }
        }
    }
}
