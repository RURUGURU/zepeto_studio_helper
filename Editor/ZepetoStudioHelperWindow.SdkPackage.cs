using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Detecting the installed zepeto.studio package and comparing versions.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // [QC][Invariant:sdk_detection]
        // Detection must not depend on how the SDK was installed. A registry package lives in Library/PackageCache,
        // an embedded or file: package lives in Packages/, and both resolve through the Packages/<name> virtual path.
        private static bool TryGetZepetoStudioPackage(out string version, out string source)
        {
            version = string.Empty;
            source = string.Empty;

            string embeddedManifest = Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.Combine("Packages", Path.Combine(RequiredPackage, "package.json")));
            if (File.Exists(embeddedManifest))
            {
                version = ReadPackageVersion(embeddedManifest);
                if (!string.IsNullOrEmpty(version))
                {
                    source = "embedded";
                    return true;
                }
            }

            string packageCacheRoot = Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.Combine("Library", "PackageCache"));
            if (Directory.Exists(packageCacheRoot))
            {
                string[] directories = Directory.GetDirectories(packageCacheRoot, RequiredPackage + "@*");
                string bestVersion = string.Empty;
                for (int i = 0; i < directories.Length; i++)
                {
                    string manifest = Path.Combine(directories[i], "package.json");
                    string candidate = File.Exists(manifest)
                        ? ReadPackageVersion(manifest)
                        : Path.GetFileName(directories[i]).Substring(RequiredPackage.Length + 1);
                    if (string.IsNullOrEmpty(candidate))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(bestVersion) || CompareVersions(candidate, bestVersion) > 0)
                    {
                        bestVersion = candidate;
                    }
                }

                if (!string.IsNullOrEmpty(bestVersion))
                {
                    version = bestVersion;
                    source = "registry";
                    return true;
                }
            }

            return false;
        }

        private static string ReadPackageVersion(string manifestPath)
        {
            try
            {
                string json = File.ReadAllText(manifestPath);
                int keyIndex = json.IndexOf("\"version\"", StringComparison.Ordinal);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex);
                int openQuote = colonIndex < 0 ? -1 : json.IndexOf('"', colonIndex);
                int closeQuote = openQuote < 0 ? -1 : json.IndexOf('"', openQuote + 1);
                if (openQuote < 0 || closeQuote <= openQuote)
                {
                    return string.Empty;
                }

                return json.Substring(openQuote + 1, closeQuote - openQuote - 1).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        // Numeric component compare so 3.2.16 correctly ranks above 3.2.9 (a string compare would not).
        private static int CompareVersions(string left, string right)
        {
            int[] leftParts = ParseVersionParts(left);
            int[] rightParts = ParseVersionParts(right);
            for (int i = 0; i < 3; i++)
            {
                if (leftParts[i] != rightParts[i])
                {
                    return leftParts[i] < rightParts[i] ? -1 : 1;
                }
            }

            return 0;
        }

        private static int[] ParseVersionParts(string version)
        {
            int[] parts = new int[3];
            if (string.IsNullOrEmpty(version))
            {
                return parts;
            }

            string core = version.Trim();
            int suffixIndex = core.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex > 0)
            {
                core = core.Substring(0, suffixIndex);
            }

            string[] segments = core.Split('.');
            for (int i = 0; i < parts.Length && i < segments.Length; i++)
            {
                int value;
                parts[i] = int.TryParse(segments[i], out value) ? value : 0;
            }

            return parts;
        }

        private static bool IsRequiredZepetoStudioPackageInstalled(out string foundVersion)
        {
            string source;
            if (!TryGetZepetoStudioPackage(out foundVersion, out source))
            {
                foundVersion = string.Empty;
                return false;
            }

            return CompareVersions(foundVersion, MinimumPackageVersion) >= 0;
        }
    }
}
