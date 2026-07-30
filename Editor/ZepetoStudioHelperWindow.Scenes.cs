using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Discovering and opening the work scene that contains a LOADER.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // [QC][Invariant:no_hardcoded_scene]
        // The ZEPETO SDK packages do not ship a work scene, so the helper must discover whichever scene in the
        // project actually contains a LOADER instead of assuming a fixed Assets/Playground.unity path.
        private void RefreshWorkSceneCandidates()
        {
            List<string> guids = new List<string>();
            List<string> options = new List<string>();

            if (AssetDatabase.IsValidFolder("Assets"))
            {
                string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                Array.Sort(sceneGuids, CompareScenePathByName);
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (string.IsNullOrEmpty(path) || !SceneFileContainsLoader(path))
                    {
                        continue;
                    }

                    guids.Add(sceneGuids[i]);
                    options.Add(MakePopupSafeLabel(path));
                }
            }

            workSceneGuids = guids.ToArray();
            workSceneOptions = options.ToArray();
            selectedWorkSceneIndex = Mathf.Clamp(selectedWorkSceneIndex, 0, Mathf.Max(0, workSceneGuids.Length - 1));
        }

        private static int CompareScenePathByName(string leftGuid, string rightGuid)
        {
            string left = AssetDatabase.GUIDToAssetPath(leftGuid) ?? string.Empty;
            string right = AssetDatabase.GUIDToAssetPath(rightGuid) ?? string.Empty;
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SceneFileContainsLoader(string scenePath)
        {
            try
            {
                string absolutePath = ToAbsoluteProjectPath(scenePath);
                if (!File.Exists(absolutePath))
                {
                    return false;
                }

                // A binary/compressed scene cannot be scanned as text; treat it as a candidate rather than hiding it.
                using (StreamReader reader = new StreamReader(absolutePath))
                {
                    string line;
                    int inspectedLines = 0;
                    while ((line = reader.ReadLine()) != null && inspectedLines < 200000)
                    {
                        inspectedLines++;
                        if (line.IndexOf("m_Name: LOADER", StringComparison.Ordinal) >= 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void OpenSelectedWorkScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 scene을 바꾸지 않습니다. 먼저 Stop을 누르세요.";
                return;
            }

            RefreshWorkSceneCandidates();
            if (workSceneGuids.Length == 0)
            {
                statusMessage = "LOADER가 들어 있는 scene을 프로젝트에서 찾지 못했습니다. "
                    + "ZEPETO Studio에서 받은 의상 템플릿 프로젝트의 scene을 Assets 아래에 넣은 뒤 다시 눌러주세요.";
                return;
            }

            int index = Mathf.Clamp(selectedWorkSceneIndex, 0, workSceneGuids.Length - 1);
            string scenePath = AssetDatabase.GUIDToAssetPath(workSceneGuids[index]);
            if (string.IsNullOrEmpty(scenePath))
            {
                statusMessage = "선택한 scene 경로를 찾지 못했습니다. 목록을 새로고침하세요.";
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty
                && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                statusMessage = "Scene 변경을 취소했습니다. / Open scene canceled.";
                return;
            }

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single
            );

            loader = null;
            lastLoaderSearchTime = -1000d;
            RefreshAll();
            statusMessage = "작업 scene을 열었습니다: " + scenePath;
        }
    }
}
