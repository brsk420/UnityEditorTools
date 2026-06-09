using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace _Brsk420.EditorTools
{
    /// <summary>
    /// Adds an "Open in Photoshop" context menu item for texture assets in the Project window.
    /// The item appears in the right-click dropdown next to "Reveal in Finder".
    /// </summary>
    internal static class OpenInPhotoshop
    {
        // Placed under "Assets/" so it shows up in the Project window right-click context menu.
        // Priority 19 places it close to "Reveal in Finder" (which sits around priority 18-20).
        private const string MenuPath = "Assets/Open in Photoshop";

        private const int MenuPriority = 19;

        // Extensions that we treat as openable image/texture assets.
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".psd",
            ".psb",
            ".tga",
            ".tif",
            ".tiff",
            ".bmp",
            ".gif",
            ".exr",
            ".hdr"
        };

        [MenuItem(MenuPath, false, MenuPriority)]
        private static void OpenSelectedInPhotoshop()
        {
            foreach (var obj in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                if (!IsSupportedTexture(assetPath))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(assetPath);
                OpenFileInPhotoshop(fullPath);
            }
        }

        // Validation: the menu item is only enabled when at least one selected asset is a supported texture.
        [MenuItem(MenuPath, true, MenuPriority)]
        private static bool ValidateOpenSelectedInPhotoshop()
        {
            foreach (var obj in Selection.objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath) && IsSupportedTexture(assetPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSupportedTexture(string assetPath)
        {
            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return SupportedExtensions.Contains(extension);
        }

        private static void OpenFileInPhotoshop(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[OpenInPhotoshop] File not found: {fullPath}");
                return;
            }

            try
            {
#if UNITY_EDITOR_OSX
                // On macOS, launch the installed Photoshop app via `open -a`.
                // Try the exact installed path first, then fall back to the generic app name.
                string photoshopApp = FindPhotoshopOnMac();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = $"-a \"{photoshopApp}\" \"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(startInfo);

#elif UNITY_EDITOR_WIN
                // On Windows, try to locate Photoshop and open the file with it.
                string photoshopExe = FindPhotoshopOnWindows();
                if (string.IsNullOrEmpty(photoshopExe))
                {
                    Debug.LogError("[OpenInPhotoshop] Could not locate Photoshop.exe. " +
                                   "Make sure Adobe Photoshop is installed.");
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = photoshopExe,
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(startInfo);
#else
                Debug.LogWarning("[OpenInPhotoshop] Unsupported editor platform.");
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[OpenInPhotoshop] Failed to open '{fullPath}' in Photoshop: {e.Message}");
            }
        }

#if UNITY_EDITOR_OSX
        private static string FindPhotoshopOnMac()
        {
            // Search /Applications for the newest installed Photoshop, e.g. "Adobe Photoshop 2026".
            const string applicationsDir = "/Applications";
            if (Directory.Exists(applicationsDir))
            {
                string bestApp = null;

                foreach (var dir in Directory.GetDirectories(applicationsDir, "Adobe Photoshop*"))
                {
                    // Inside each "Adobe Photoshop XXXX" folder there is an "<name>.app" bundle.
                    foreach (var app in Directory.GetDirectories(dir, "*.app"))
                    {
                        // Prefer the lexicographically greatest path (usually the newest year).
                        if (bestApp == null || string.CompareOrdinal(app, bestApp) > 0)
                        {
                            bestApp = app;
                        }
                    }

                    // Also handle the case where the .app sits directly in /Applications.
                    string directApp = dir + ".app";
                    if (Directory.Exists(directApp) &&
                        (bestApp == null || string.CompareOrdinal(directApp, bestApp) > 0))
                    {
                        bestApp = directApp;
                    }
                }

                if (!string.IsNullOrEmpty(bestApp))
                {
                    return bestApp;
                }
            }

            // Fallback: let macOS resolve the app by name.
            return "Adobe Photoshop";
        }
#endif

#if UNITY_EDITOR_WIN
        private static string FindPhotoshopOnWindows()

        {
            // Search common install locations for the newest available Photoshop.exe.
            var searchRoots = new[]
            {
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86)
            };

            string bestMatch = null;

            foreach (var root in searchRoots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                string adobeDir = Path.Combine(root, "Adobe");
                if (!Directory.Exists(adobeDir))
                {
                    continue;
                }

                foreach (var dir in Directory.GetDirectories(adobeDir, "Adobe Photoshop*"))
                {
                    string exe = Path.Combine(dir, "Photoshop.exe");
                    if (File.Exists(exe))
                    {
                        // Prefer the lexicographically greatest folder name (usually the newest year).
                        if (bestMatch == null || string.CompareOrdinal(dir, Path.GetDirectoryName(bestMatch)) > 0)
                        {
                            bestMatch = exe;
                        }
                    }
                }
            }

            return bestMatch;
        }
#endif
    }
}
