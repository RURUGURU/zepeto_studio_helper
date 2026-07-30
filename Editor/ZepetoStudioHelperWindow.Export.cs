using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Running the official .zepeto export and reporting the output file.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void OpenExportMenu()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 export 메뉴를 열지 않습니다. 먼저 Stop을 눌러주세요.";
                return;
            }

            if (clothingPrefab != null)
            {
                SelectAndPing(clothingPrefab);
            }

            string officialPackagePath = GetOfficialZepetoPackagePath();
            string expectedPackagePath = GetExpectedZepetoPackagePath();
            // [AUDIT][Risk:Major][Scope:zepeto_export]
            // The official SDK menu writes <outfit>.zepeto beside the prefab. The helper only post-processes that
            // local output into a readable filename and reports the final path in the UI.
            if (!EditorApplication.ExecuteMenuItem(ExportMenuPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not execute menu item: " + ExportMenuPath);
                statusMessage = "ZEPETO Export 메뉴를 실행하지 못했습니다: " + ExportMenuPath;
                return;
            }

            AssetDatabase.Refresh();
            string finalPackagePath = MoveOfficialExportToFriendlyName(officialPackagePath, expectedPackagePath);
            AssetDatabase.Refresh();
            FindLoaderAndSerializedFields();

            if (!string.IsNullOrEmpty(finalPackagePath) && File.Exists(ToAbsoluteProjectPath(finalPackagePath)))
            {
                ResetHelperConsoleSummaryAfterSuccessfulExport();
                statusMessage = "ZEPETO export 파일을 만들었습니다: " + finalPackagePath;
                UnityEngine.Object packageAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPackagePath);
                if (packageAsset != null)
                {
                    SelectAndPing(packageAsset);
                }
            }
            else
            {
                statusMessage = "ZEPETO Export 메뉴를 실행했습니다. Console의 ZEPETO archive 결과를 확인하세요.";
            }

            Repaint();
        }

        private void RecheckExportResult()
        {
            AssetDatabase.Refresh();
            string officialPackagePath = GetOfficialZepetoPackagePath();
            string expectedPackagePath = GetExpectedZepetoPackagePath();
            string finalPackagePath = MoveOfficialExportToFriendlyName(officialPackagePath, expectedPackagePath);
            AssetDatabase.Refresh();

            if (!string.IsNullOrEmpty(finalPackagePath) && File.Exists(ToAbsoluteProjectPath(finalPackagePath)))
            {
                statusMessage = "ZEPETO export 파일을 확인했습니다: " + finalPackagePath;
                SelectAndPing(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPackagePath));
            }
            else
            {
                statusMessage = "아직 .zepeto 파일이 없습니다. Console의 ZEPETO archive 로그를 확인하고, "
                    + "export가 끝난 뒤 다시 눌러주세요.";
            }

            Repaint();
        }

        private string MoveOfficialExportToFriendlyName(string officialPackagePath, string friendlyPackagePath)
        {
            // [QC][Invariant:export_rename]
            // Rename only after the official output exists, and delete only the expected friendly target.
            // This prevents a failed SDK export from being reported as a successful helper export.
            if (string.IsNullOrEmpty(officialPackagePath))
            {
                return friendlyPackagePath;
            }

            if (string.IsNullOrEmpty(friendlyPackagePath)
                || string.Equals(officialPackagePath, friendlyPackagePath, StringComparison.OrdinalIgnoreCase))
            {
                return officialPackagePath;
            }

            if (!File.Exists(ToAbsoluteProjectPath(officialPackagePath)))
            {
                return friendlyPackagePath;
            }

            if (File.Exists(ToAbsoluteProjectPath(friendlyPackagePath)))
            {
                AssetDatabase.DeleteAsset(friendlyPackagePath);
            }

            string moveError = AssetDatabase.MoveAsset(officialPackagePath, friendlyPackagePath);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not rename export file: " + moveError);
                return officialPackagePath;
            }

            return friendlyPackagePath;
        }

        private void ResetHelperConsoleSummaryAfterSuccessfulExport()
        {
            sessionWarningCount = 0;
            sessionErrorCount = 0;
            lastConsoleMessage = string.Empty;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            lastSafetyRefreshTime = -1000d;
        }

        private string GetExpectedZepetoPackagePath()
        {
            // [QA][Acceptance:visible_output_path]
            // Step 7 reads this path before and after export so users can see exactly where the .zepeto file should be.
            string officialPath = GetOfficialZepetoPackagePath();
            if (string.IsNullOrEmpty(officialPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(officialPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            return folder + "/" + BuildFriendlyExportFileName();
        }

        private string GetOfficialZepetoPackagePath()
        {
            if (clothingPrefab == null)
            {
                return string.Empty;
            }

            string prefabPath = AssetDatabase.GetAssetPath(clothingPrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(prefabPath) + ".zepeto";
            return folder + "/" + fileName;
        }

        private string BuildFriendlyExportFileName()
        {
            string outfitName = clothingPrefab == null ? "outfit" : clothingPrefab.name;
            AnimationClip clip = GetAssignedAnimationClip();
            string motionName = clip == null ? "motion" : GetReadableMotionName(clip.name);
            // [QC][Invariant:filename]
            // Include both outfit and motion so exported files stay recognizable outside Unity's Project window.
            return MakeExportSafeFileName("ZEPETO_" + outfitName + "_" + motionName) + ".zepeto";
        }

        private static string GetReadableMotionName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "motion";
            }

            string name = rawName;
            int clipEditIndex = name.IndexOf("_clipedit", StringComparison.OrdinalIgnoreCase);
            if (clipEditIndex > 0)
            {
                name = name.Substring(0, clipEditIndex);
            }

            const string editableSuffix = "_editable";
            if (name.EndsWith(editableSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - editableSuffix.Length);
            }

            return string.IsNullOrEmpty(name) ? "motion" : name;
        }

        private static string MakeExportSafeFileName(string value)
        {
            string safeName = string.IsNullOrEmpty(value) ? "ZEPETO_export" : value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            safeName = safeName.Replace(' ', '_');
            while (safeName.IndexOf("__", StringComparison.Ordinal) >= 0)
            {
                safeName = safeName.Replace("__", "_");
            }

            return string.IsNullOrEmpty(safeName) ? "ZEPETO_export" : safeName;
        }

        private static string GetExportPackageStatusText(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return "의상 선택 필요";
            }

            string absolutePath = ToAbsoluteProjectPath(projectRelativePath);
            if (!File.Exists(absolutePath))
            {
                return projectRelativePath + " (아직 생성 전)";
            }

            FileInfo fileInfo = new FileInfo(absolutePath);
            return projectRelativePath
                + " (저장됨, "
                + FormatBytes(fileInfo.Length)
                + ", "
                + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                + ")";
        }

        private static bool ExportPackageExists(string projectRelativePath)
        {
            return !string.IsNullOrEmpty(projectRelativePath)
                && File.Exists(ToAbsoluteProjectPath(projectRelativePath));
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelativePath));
        }
    }
}
