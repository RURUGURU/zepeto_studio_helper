using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Puts a visible body in the Scene while Play is stopped.
    ///
    /// The real avatar is downloaded at runtime, so before Play the LOADER is an empty GameObject and the Scene
    /// view shows nothing but a gizmo - there is no way to tell whether the camera is even pointed at the right
    /// place. This drops the exported ZEPETO base body in at the LOADER's position as a stand-in.
    ///
    /// It is never written to the scene file. The object carries HideFlags.DontSave, so it costs no scene diff,
    /// no commit, and cannot end up in an export - and it is removed the moment Play starts, so it can never be
    /// confused with (or overlap) the real avatar.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string PreviewBodyName = "[미리보기] ZEPETO 기본 몸 - Play 하면 사라집니다";
        private const string PreviewBodyEnabledPrefKey = "com.easy.zepeto-helper.showPreviewBody";

        private GameObject previewBody;
        private bool previewBodySyncQueued;

        private static bool PreviewBodyEnabled
        {
            get { return EditorPrefs.GetBool(PreviewBodyEnabledPrefKey, true); }
            set { EditorPrefs.SetBool(PreviewBodyEnabledPrefKey, value); }
        }

        /// <summary>
        /// Creates the stand-in if it should exist and does not yet, or removes it if it should not.
        /// Safe to call every repaint.
        /// </summary>
        private void SyncPreviewBody()
        {
            bool shouldExist = PreviewBodyEnabled
                && !EditorApplication.isPlayingOrWillChangePlaymode
                && loader != null;

            if (!shouldExist)
            {
                ClearPreviewBody();
                return;
            }

            if (previewBody != null)
            {
                return;
            }

            // The SDK's own prefab, not the fbx this tool exports for Blender.
            //
            // The exported fbx is written with materialImportMode stripped, so instantiating it renders as a
            // flat dark silhouette - it looked broken rather than helpful. The package prefab carries the real
            // skin/hair/eye materials, exists in every project without exporting anything first, and is
            // literally what the runtime avatar is built from, so the stand-in matches what Play will show.
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ZepetoBaseModelPath);
            if (source == null)
            {
                return;
            }

            previewBody = UnityEngine.Object.Instantiate(source);
            previewBody.name = PreviewBodyName;

            // DontSave keeps it out of the .unity file and out of version control. It stays visible in the
            // Hierarchy on purpose - a body that appears with no explanation is worse than one the user can see
            // and understand.
            previewBody.hideFlags = HideFlags.DontSave;

            Transform loaderTransform = loader.transform;
            previewBody.transform.SetPositionAndRotation(loaderTransform.position, loaderTransform.rotation);
            previewBody.transform.SetParent(null, true);

            // The SDK prefab ships with its renderers switched off - the runtime turns them on once it has
            // built the avatar from downloaded parts. Instantiating it as-is therefore puts a completely
            // invisible object in the scene, which looks exactly like the feature not working. Measured on
            // zepeto.character 3.1.32: 2 renderers, 0 of them enabled and active.
            Renderer[] renderers = previewBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].gameObject.SetActive(true);
                renderers[i].enabled = true;
            }
        }

        private void ClearPreviewBody()
        {
            if (previewBody == null)
            {
                // A domain reload destroys DontSave objects but leaves this field pointing at nothing, and a
                // previous session can leave one behind. Sweep by name so a stale copy cannot pile up.
                RemoveStrayPreviewBodies();
                return;
            }

            UnityEngine.Object.DestroyImmediate(previewBody);
            previewBody = null;
            RemoveStrayPreviewBodies();
        }

        private static void RemoveStrayPreviewBodies()
        {
            GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == PreviewBodyName)
                {
                    UnityEngine.Object.DestroyImmediate(all[i]);
                }
            }
        }

        /// <summary>
        /// Aims the Scene view at the stand-in, front on, the same way the Blender add-on frames the character.
        /// </summary>
        private void FramePreviewBody()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            Bounds bounds;
            if (previewBody != null)
            {
                Renderer[] renderers = previewBody.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    return;
                }

                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            else if (loader != null)
            {
                bounds = new Bounds(loader.transform.position + Vector3.up * 0.5f, Vector3.one);
            }
            else
            {
                return;
            }

            // Look from +Z toward the character, which is where the booth camera sits.
            sceneView.LookAt(bounds.center, Quaternion.LookRotation(Vector3.back, Vector3.up),
                Mathf.Max(1.4f, bounds.extents.magnitude * 2.4f));
            sceneView.Repaint();
        }

        private void DrawPreviewBodySection()
        {
            // Self-heal. SyncPreviewBody otherwise only runs from OnEnable and the play-mode hook, and both can
            // be missed - a domain reload where the loader is not bound yet, a window opened before the scene,
            // a stray body swept away by another instance. The result was a checked box with nothing on screen,
            // which reads as the feature being broken. Scheduled, never run inside OnGUI: Instantiate and
            // DestroyImmediate during layout corrupt the GUI pass.
            if (PreviewBodyEnabled
                && !EditorApplication.isPlayingOrWillChangePlaymode
                && loader != null
                && previewBody == null
                && !previewBodySyncQueued)
            {
                previewBodySyncQueued = true;
                EditorApplication.delayCall += () =>
                {
                    previewBodySyncQueued = false;
                    SyncPreviewBody();
                    Repaint();
                };
            }

            bool hasRig = AssetDatabase.LoadAssetAtPath<GameObject>(ZepetoBaseModelPath) != null;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft(
                "정지 중 Scene에 기본 몸 보이기",
                PreviewBodyEnabled && hasRig);
            if (EditorGUI.EndChangeCheck())
            {
                PreviewBodyEnabled = enabled;
                SyncPreviewBody();
                if (enabled)
                {
                    FramePreviewBody();
                }
            }

            using (new EditorGUI.DisabledScope(previewBody == null))
            {
                if (DrawSecondaryButton("초점 맞추기", GUILayout.Width(90f), GUILayout.Height(20f)))
                {
                    FramePreviewBody();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!hasRig)
            {
                DrawMiniHelp(
                    "ZEPETO SDK의 기본 모델을 찾지 못했습니다: " + ZepetoBaseModelPath
                    + ". zepeto.character 패키지가 설치되어 있는지 확인하세요.",
                    MessageType.Warning);
            }
            else if (PreviewBodyEnabled)
            {
                DrawMiniHelp(
                    "이 몸은 자리를 보여주는 임시 표시입니다. 씬 파일에 저장되지 않고, Play를 누르면 사라진 뒤 "
                    + "그 자리에 진짜 내 아바타가 들어옵니다.",
                    MessageType.None);
            }
        }
    }
}
