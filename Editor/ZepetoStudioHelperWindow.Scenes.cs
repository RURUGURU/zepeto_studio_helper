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
        // The work-scene selection is a GUID, not a list position. workSceneGuids is rebuilt and re-sorted by
        // every RefreshWorkSceneCandidates call, so an index captured when the popup was clicked started pointing
        // at a different scene as soon as one was added, renamed or deleted - and "씬 열기" then opened that other
        // scene without anything on screen changing. selectedWorkSceneIndex (window shell partial) survives as the
        // popup's display position only, re-derived from this GUID after every rebuild.
        private string selectedWorkSceneGuid = string.Empty;

        // The three StageComplete keys in the window shell partial are session-scoped but not scene-scoped, so
        // opening a different scene that also has a LOADER showed steps as "완료됨" that were completed for the
        // previous scene. This records which scene the booked progress belongs to.
        private const string StageProgressSceneGuidSessionKey = "Easy.ZepetoHelper.StageComplete.SceneGuid";

        // [QC][Invariant:no_hardcoded_scene]
        // The ZEPETO SDK packages do not ship a work scene, so the helper must discover whichever scene in the
        // project actually contains a LOADER instead of assuming a fixed Assets/Playground.unity path.
        private void RefreshWorkSceneCandidates()
        {
            EnsureActiveSceneWatch();

            // The popup index still refers to the arrays that are about to be replaced, so resolve it to a GUID
            // before rebuilding them.
            CommitWorkSceneSelectionToGuid();

            List<string> guids = new List<string>();
            List<string> options = new List<string>();

            if (AssetDatabase.IsValidFolder("Assets"))
            {
                string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                Array.Sort(sceneGuids, CompareScenePathByName);
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    bool loaderUnverified;
                    if (string.IsNullOrEmpty(path) || !SceneFileContainsLoader(path, out loaderUnverified))
                    {
                        continue;
                    }

                    guids.Add(sceneGuids[i]);

                    // A scene we could not read as text is listed, but labelled: hiding it was worse, and
                    // claiming it holds a LOADER when nothing checked would be a lie.
                    options.Add(loaderUnverified
                        ? MakePopupSafeLabel(path) + "  (LOADER 확인 못 함)"
                        : MakePopupSafeLabel(path));
                }
            }

            workSceneGuids = guids.ToArray();
            workSceneOptions = options.ToArray();
            NormalizeWorkSceneSelection();
            SyncWorkflowStageProgressToActiveScene();
        }

        /// <summary>
        /// Display index -> selected GUID, resolved against the arrays the index was chosen against. Must run
        /// before those arrays are replaced.
        /// </summary>
        private void CommitWorkSceneSelectionToGuid()
        {
            if (workSceneGuids.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(selectedWorkSceneIndex, 0, workSceneGuids.Length - 1);
            selectedWorkSceneGuid = workSceneGuids[index] ?? string.Empty;
        }

        /// <summary>
        /// Selected GUID -> display index. Falls back to the first candidate when the selected scene is no longer
        /// in the list, which is what clamping the raw index used to do.
        /// </summary>
        private void NormalizeWorkSceneSelection()
        {
            if (workSceneGuids.Length == 0)
            {
                selectedWorkSceneIndex = 0;
                return;
            }

            int index = IndexOfWorkSceneGuid(selectedWorkSceneGuid);
            if (index < 0)
            {
                index = 0;
                selectedWorkSceneGuid = workSceneGuids[0] ?? string.Empty;
            }

            selectedWorkSceneIndex = index;
        }

        private int IndexOfWorkSceneGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return -1;
            }

            for (int i = 0; i < workSceneGuids.Length; i++)
            {
                if (string.Equals(workSceneGuids[i], guid, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompareScenePathByName(string leftGuid, string rightGuid)
        {
            string left = AssetDatabase.GUIDToAssetPath(leftGuid) ?? string.Empty;
            string right = AssetDatabase.GUIDToAssetPath(rightGuid) ?? string.Empty;
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Text-scans the .unity file for the LOADER object: cheap, and it needs no scene load.
        ///
        /// It only works on a YAML (text) scene though. A scene saved in binary or compressed form cannot be
        /// scanned this way, and dropping those silently left the user staring at "LOADER가 들어 있는 scene을
        /// 찾지 못했습니다" with a perfectly good work scene in the project. Such a scene is returned as a
        /// candidate with contentUnverified set, so the caller lists it flagged rather than hiding it.
        ///
        /// Deliberate limitation: only the first 200,000 lines are read. A text scene whose LOADER appears after
        /// that is reported as not containing one.
        /// </summary>
        private static bool SceneFileContainsLoader(string scenePath, out bool contentUnverified)
        {
            contentUnverified = false;

            string absolutePath = string.Empty;
            try
            {
                absolutePath = ToAbsoluteProjectPath(scenePath);
                if (!File.Exists(absolutePath))
                {
                    return false;
                }

                using (StreamReader reader = new StreamReader(absolutePath))
                {
                    string line = reader.ReadLine();

                    // Every text scene opens with the YAML header. Anything else is binary/compressed, which
                    // reading as text cannot verify either way.
                    if (line == null || line.IndexOf("%YAML", StringComparison.Ordinal) < 0)
                    {
                        contentUnverified = true;
                        return true;
                    }

                    int inspectedLines = 1;
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
                // A file that exists but cannot be read (locked, or a stream error part way through) is the same
                // situation as a binary scene: unverifiable, so offer it flagged. A path that could not even be
                // resolved is not a candidate at all.
                contentUnverified = File.Exists(absolutePath);
                return contentUnverified;
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

            // By GUID. The refresh above rebuilt and re-sorted the list, so the popup's index is a display
            // position by now and could easily name a different scene than the one the user picked.
            string scenePath = AssetDatabase.GUIDToAssetPath(selectedWorkSceneGuid);
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

        /// <summary>
        /// Notices scene changes that do not go through OpenSelectedWorkScene - double-clicking a scene in the
        /// Project window, or File &gt; Open Scene.
        ///
        /// Subscribed from RefreshWorkSceneCandidates rather than OnEnable because the window shell partial owns
        /// OnEnable. The -= before the += makes it idempotent, so re-running it on every refresh cannot stack
        /// handlers. Unsubscribed from OnDestroy; a domain reload wipes the static event anyway and the next
        /// refresh after it re-subscribes.
        /// </summary>
        private void EnsureActiveSceneWatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnEditorSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnEditorSceneOpened;
        }

        private void UnsubscribeActiveSceneWatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnEditorSceneOpened;
        }

        private void OnEditorSceneOpened(
            UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            // A window closed between subscribing and the callback compares == null, and calling Repaint on it
            // throws. OnDestroy unsubscribes, but a window destroyed without one - a throwaway instance in the
            // self-test - would leave this handler pointing at nothing.
            if (this == null)
            {
                return;
            }

            SyncWorkflowStageProgressToActiveScene();
            Repaint();
        }

        private static string GetActiveSceneGuid()
        {
            string path = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>
        /// Drops stage progress that was earned in a different scene, so step 1/2/3 cannot claim "완료됨" for work
        /// done on another LOADER.
        /// </summary>
        private void SyncWorkflowStageProgressToActiveScene()
        {
            // Never while Play is live or starting. The scene cannot change there, and clearing the stage flags
            // mid-Play would collapse steps 2/3/4 to 대기중 and take the live-preview panel with its Stop button
            // off screen - the exact failure the [SerializeField] on clothingPrefab exists to prevent.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string activeGuid = GetActiveSceneGuid();

            // An unsaved/untitled scene has no asset to scope progress to. Leave the booking alone rather than
            // clearing the flags on every refresh.
            if (string.IsNullOrEmpty(activeGuid))
            {
                return;
            }

            string bookedGuid = SessionState.GetString(StageProgressSceneGuidSessionKey, string.Empty);
            if (string.Equals(bookedGuid, activeGuid, StringComparison.Ordinal))
            {
                return;
            }

            SessionState.SetString(StageProgressSceneGuidSessionKey, activeGuid);

            if (string.IsNullOrEmpty(bookedGuid))
            {
                // Nothing was scoped yet, so there is no other scene's progress to drop: adopt what is booked
                // instead of wiping flags the user earned in this very scene.
                return;
            }

            // Cascades: clearing step 1 clears steps 2 and 3 as well.
            SetAvatarOutfitStageComplete(false);
            statusMessage = "다른 scene을 열었으므로 1~3번 단계의 완료 표시를 지웠습니다.";
        }
    }
}
