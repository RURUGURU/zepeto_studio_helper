using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Binding to the ZEPETO template LOADER and driving the SDK playback slot.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void OnSceneViewGui(SceneView sceneView)
        {
            // ScenePreviewOverlayEnabled, not the old showScenePreviewOverlay field: that field had lost its
            // toggle and was never written, so the overlay could not be turned off any more. The pref pair lives
            // next to the preview-body toggle in ScenePreview.cs.
            if (!ScenePreviewOverlayEnabled || sceneView == null)
            {
                return;
            }

            if (!EditorApplication.isPlaying && !LoaderHasPreviewRenderers())
            {
                return;
            }

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 292f, 108f), EditorStyles.helpBox);
            GUILayout.Label("Scene 보조 / Preview Focus", EditorStyles.boldLabel);
            GUILayout.Label(loader == null ? "LOADER: 없음 / Missing" : "LOADER: " + loader.name, EditorStyles.miniLabel);
            GUILayout.Label(EditorApplication.isPlaying ? "Play 중: 실제 아바타 확인" : "정지 중: LOADER 초점 확인", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loader == null))
            {
                if (GUILayout.Button("선택 / Select", EditorStyles.miniButtonLeft))
                {
                    SelectAndFrameLoader();
                }

                if (GUILayout.Button("초점 / Focus", EditorStyles.miniButtonRight))
                {
                    FrameLoaderForScenePreview();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!LoaderHasPreviewRenderers())
            {
                GUILayout.Label(EditorApplication.isPlaying
                    ? "아바타 로딩 전/실패: ID와 SDK 상태 확인"
                    : "정지 중에는 아바타 mesh가 없을 수 있음", EditorStyles.miniLabel);
            }

            GUILayout.Label(EditorApplication.isPlaying
                ? "Stop 후에만 저장/Export 가능"
                : "Play 버튼으로 실제 움직임 확인", EditorStyles.miniLabel);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void SelectAndFrameLoader()
        {
            if (loader == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (loader == null)
            {
                statusMessage = "LOADER를 찾지 못했습니다. 작업 준비 / Setup에서 LOADER를 다시 찾으세요.";
                return;
            }

            FrameLoaderForScenePreview();

            statusMessage = "Scene View에서 LOADER를 선택했습니다. 아바타와 의상 관통을 확인하세요.";
        }

        // [QC][Guard:repaint_cost]
        // These getters run from OnGUI. Without a LOADER the rebind would otherwise walk every scene root on every
        // repaint, so rebinding is throttled while explicit user actions reset the timer for an immediate retry.
        private void EnsureLoaderBinding()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastLoaderSearchTime < LoaderSearchIntervalSeconds)
            {
                return;
            }

            lastLoaderSearchTime = now;
            FindLoaderAndSerializedFields();
        }

        private AnimationClip GetAssignedAnimationClip()
        {
            if (!TryUpdateSerializedObject(animationClipObject) || animationClipProperty == null)
            {
                EnsureLoaderBinding();
            }

            if (!TryUpdateSerializedObject(animationClipObject) || animationClipProperty == null)
            {
                return null;
            }

            return animationClipProperty.objectReferenceValue as AnimationClip;
        }

        private UnityEngine.Object GetAssignedAnimatorController()
        {
            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                EnsureLoaderBinding();
            }

            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                return null;
            }

            return animatorControllerProperty.objectReferenceValue;
        }

        private string GetAnimatorControllerPath()
        {
            UnityEngine.Object controller = GetAssignedAnimatorController();
            return controller == null ? string.Empty : AssetDatabase.GetAssetPath(controller);
        }

        private string GetCurrentZepetoId()
        {
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                EnsureLoaderBinding();
            }

            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                return string.Empty;
            }

            return zepetoIdProperty.stringValue;
        }

        private static bool TryUpdateSerializedObject(SerializedObject serializedObject)
        {
            // [QC][Guard:stale_serialized_object]
            // ZEPETO export and domain reloads can destroy the target behind a cached SerializedObject.
            // Returning false lets callers refind LOADER instead of throwing "target has been destroyed".
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return false;
            }

            try
            {
                serializedObject.Update();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void FindLoaderAndSerializedFields()
        {
            if (loader == null)
            {
                loader = FindLoaderGameObject();
            }

            zepetoIdObject = null;
            animationClipObject = null;
            animatorControllerObject = null;
            zepetoIdProperty = null;
            animationClipProperty = null;
            animatorControllerProperty = null;

            // [QC][Guard:no_text_clobber]
            // This runs from OnGUI through GetCurrentZepetoId, so it must never overwrite what the user typed.
            // Without a LOADER the id field keeps its value so accounts can still be registered.
            if (loader == null)
            {
                return;
            }

            // [QC][Invariant:field_binding_scope]
            // In the official SDK these three fields are spread over two different components:
            //   zepetoId          -> Zepeto.ZepetoCharacterCustomLoader
            //   AnimationClip     -> ZEPETO.Studio.PlaygroundController
            //   AnimatorController-> ZEPETO.Studio.PlaygroundController
            // Whether both components sit on the LOADER object depends on how the template scene is built, so the
            // search widens from the object, to its children, to the whole scene instead of giving up immediately.
            BindLoaderFields(loader.GetComponents<Component>());

            if (!HasAllLoaderFields())
            {
                BindLoaderFields(loader.GetComponentsInChildren<Component>(true));
            }

            if (!HasAllLoaderFields())
            {
                BindLoaderFields(CollectLoadedSceneComponents());
            }
        }

        private bool HasAllLoaderFields()
        {
            return zepetoIdProperty != null && animationClipProperty != null && animatorControllerProperty != null;
        }

        private void BindLoaderFields(Component[] components)
        {
            if (components == null)
            {
                return;
            }

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                SerializedObject serializedObject = new SerializedObject(component);

                if (zepetoIdProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("zepetoId");
                    if (property != null && property.propertyType == SerializedPropertyType.String)
                    {
                        zepetoIdObject = serializedObject;
                        zepetoIdProperty = property;
                        zepetoIdText = string.IsNullOrEmpty(property.stringValue) ? zepetoIdText : property.stringValue;
                    }
                }

                if (animationClipProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("AnimationClip");
                    if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        animationClipObject = serializedObject;
                        animationClipProperty = property;
                    }
                }

                if (animatorControllerProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("AnimatorController");
                    if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        animatorControllerObject = serializedObject;
                        animatorControllerProperty = property;
                    }
                }

                if (HasAllLoaderFields())
                {
                    return;
                }
            }
        }

        private static Component[] CollectLoadedSceneComponents()
        {
            List<Component> components = new List<Component>();
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    components.AddRange(roots[r].GetComponentsInChildren<Component>(true));
                }
            }

            return components.ToArray();
        }

        // [QC][Invariant:loader_lookup]
        // GameObject.Find only sees active objects in the active scene. A template LOADER can be inactive or live in
        // an additively loaded scene, so every loaded scene's root hierarchy is searched instead.
        private static GameObject FindLoaderGameObject()
        {
            GameObject direct = GameObject.Find("LOADER");
            if (direct != null)
            {
                return direct;
            }

            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    GameObject match = FindChildNamedLoader(roots[r]);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static GameObject FindChildNamedLoader(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, "LOADER", StringComparison.Ordinal))
            {
                return root;
            }

            Transform rootTransform = root.transform;
            for (int i = 0; i < rootTransform.childCount; i++)
            {
                GameObject match = FindChildNamedLoader(rootTransform.GetChild(i).gameObject);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private void FindDefaultClothingPrefab()
        {
            List<GameObject> prefabs = FindAllOutfitPrefabs();
            if (prefabs.Count == 0)
            {
                clothingPrefab = null;
                statusMessage = AssetDatabase.IsValidFolder(ContentsRoot)
                    ? ContentsRoot + " 아래에서 의상 prefab을 찾지 못했습니다. 의상 prefab을 이 폴더로 옮기세요."
                    : ContentsRoot + " 폴더가 없습니다. ZEPETO Studio 의상 템플릿의 Contents 폴더를 Assets 아래에 넣으세요.";
                return;
            }

            if (clothingPrefab != null && prefabs.Contains(clothingPrefab))
            {
                statusMessage = "선택된 의상: " + clothingPrefab.name;
                return;
            }

            clothingPrefab = null;
            statusMessage = "1번 의상 목록에서 사용할 prefab을 직접 선택하세요.";
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        // [QC][Invariant:readonly_asset_roots]
        // The helper must never write into the SDK's own package files - that is why EnsureLocalAnimatorController
        // copies the controller to Assets/ZepetoHelper/Controllers first. But read-only-ness is a property of a
        // path SEGMENT, not of a substring: matching "PackageCache" anywhere also condemned a perfectly writable
        // user folder like "Assets/MyPackageCache/Controllers", and Play then dead-ended on a "make a local
        // controller" requirement that was already satisfied and could never be satisfied again.
        private static bool IsPackageOrPackageCachePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            // AssetDatabase hands out forward slashes, but a path built by hand or read from a log can be
            // backslashed.
            string normalized = assetPath.Replace('\\', '/');
            return normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase)
                || normalized.IndexOf("/Library/PackageCache/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool EnsureLocalAnimatorController(out string message)
        {
            message = string.Empty;
            if (animatorControllerProperty == null || animatorControllerObject == null)
            {
                message = "LOADER AnimatorController field was not found.";
                return false;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play 중에는 AnimatorController를 바꾸지 않습니다. 먼저 Stop을 눌러주세요.";
                return false;
            }

            if (!TryUpdateSerializedObject(animatorControllerObject))
            {
                message = "LOADER AnimatorController reference is stale. Reopen the work scene and retry.";
                return false;
            }

            UnityEngine.Object currentController = animatorControllerProperty.objectReferenceValue;
            if (currentController == null)
            {
                // A missing reference is recoverable: the SDK always ships the stock override controller, so fall
                // back to it instead of dead-ending the user with "AnimatorController is empty".
                currentController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SdkPlaygroundControllerPath);
                if (currentController == null)
                {
                    message = "LOADER AnimatorController가 비어 있고 SDK 기본 controller도 찾지 못했습니다: " + SdkPlaygroundControllerPath;
                    return false;
                }
            }

            string sourcePath = AssetDatabase.GetAssetPath(currentController);
            if (string.IsNullOrEmpty(sourcePath))
            {
                message = "Could not resolve AnimatorController asset path.";
                return false;
            }

            if (!IsPackageOrPackageCachePath(sourcePath))
            {
                message = "AnimatorController is already project-local: " + sourcePath;
                return true;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Controllers");

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LocalPlaygroundControllerPath) == null)
            {
                string copyMessage;
                if (!CreateLocalAnimatorControllerCopy(currentController, sourcePath, out copyMessage))
                {
                    message = copyMessage;
                    return false;
                }

                AssetDatabase.ImportAsset(LocalPlaygroundControllerPath);
            }

            UnityEngine.Object localController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LocalPlaygroundControllerPath);
            if (localController == null)
            {
                message = "Local AnimatorController copy could not be loaded: " + LocalPlaygroundControllerPath;
                return false;
            }

            Undo.RecordObject(animatorControllerObject.targetObject, "Use Local ZEPETO Preview Controller");
            animatorControllerObject.Update();
            animatorControllerProperty.objectReferenceValue = localController;
            animatorControllerObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animatorControllerObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            message = "AnimatorController를 local copy로 변경했습니다: " + LocalPlaygroundControllerPath;
            return true;
        }

        private static bool CreateLocalAnimatorControllerCopy(UnityEngine.Object sourceController, string sourcePath, out string message)
        {
            message = string.Empty;

            AnimatorOverrideController sourceOverrideController = sourceController as AnimatorOverrideController;
            if (sourceOverrideController != null)
            {
                AnimatorOverrideController localController = UnityEngine.Object.Instantiate(sourceOverrideController);
                localController.name = "PlaygroundAnimatorController_local";
                AssetDatabase.CreateAsset(localController, LocalPlaygroundControllerPath);
                message = "Created local AnimatorOverrideController copy.";
                return true;
            }

            if (AssetDatabase.CopyAsset(sourcePath, LocalPlaygroundControllerPath))
            {
                message = "Copied AnimatorController to local project asset.";
                return true;
            }

            message = "Could not copy AnimatorController from " + sourcePath + " to " + LocalPlaygroundControllerPath + ".";
            return false;
        }

        private bool AssignAnimationClip(AnimationClip clip, bool preserveClipStageComplete = false)
        {
            if (clip == null || animationClipProperty == null)
            {
                return false;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 LOADER AnimationClip을 바꾸지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return false;
            }

            string controllerMessage;
            if (!EnsureLocalAnimatorController(out controllerMessage))
            {
                statusMessage = "AnimationClip 연결 전에 local AnimatorController가 필요합니다. " + controllerMessage;
                return false;
            }

            // [AUDIT][Risk:Major][Scope:loader_binding]
            // Temporary Play previews pass preserveClipStageComplete=true so preview assignment does not unlock
            // completed workflow stages. Real clip changes intentionally reset step 3 below.
            if (!TryUpdateSerializedObject(animationClipObject))
            {
                statusMessage = "LOADER AnimationClip 참조가 끊어졌습니다. 작업 scene을 다시 열고 시도하세요.";
                return false;
            }

            AnimationClip previousClip = animationClipProperty.objectReferenceValue as AnimationClip;
            Undo.RecordObject(animationClipObject.targetObject, "Assign ZEPETO Preview Animation");
            animationClipProperty.objectReferenceValue = clip;
            animationClipObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animationClipObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            // [AUDIT][Risk:Critical][Scope:motion_playback]
            // Setting PlaygroundController.AnimationClip does NOT change what the avatar performs. The SDK's
            // ZepetoBaseModel controller plays a single slot named "dynamic", and PlaygroundAnimatorController
            // ships with that slot mapped to A_pose - a 0.04s standing pose. Unless the override table is
            // rewritten the avatar just stands still no matter which motion is selected.
            string overrideMessage;
            bool overrideApplied = ApplyClipToOverrideController(clip, out overrideMessage);

            statusMessage = overrideApplied
                ? "동작을 연결했습니다: " + clip.name + " (재생 슬롯까지 반영됨)"
                : "AnimationClip은 연결했지만 재생 슬롯을 바꾸지 못했습니다: " + overrideMessage;

            if (previousClip != clip && !preserveClipStageComplete)
            {
                SetClipStageComplete(false);
            }
            ValidateState();
            return true;
        }

        /// <summary>
        /// Rewrites every slot of the project-local AnimatorOverrideController to the given clip. This is what
        /// actually makes the avatar perform the motion; the serialized AnimationClip field alone does nothing.
        /// </summary>
        private bool ApplyClipToOverrideController(AnimationClip clip, out string message)
        {
            message = string.Empty;

            if (clip == null)
            {
                message = "연결할 동작이 비어 있습니다.";
                return false;
            }

            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                message = "LOADER의 AnimatorController 필드를 찾지 못했습니다.";
                return false;
            }

            AnimatorOverrideController overrideController =
                animatorControllerProperty.objectReferenceValue as AnimatorOverrideController;
            if (overrideController == null)
            {
                message = "AnimatorController가 AnimatorOverrideController가 아닙니다.";
                return false;
            }

            string controllerPath = AssetDatabase.GetAssetPath(overrideController);
            if (IsPackageOrPackageCachePath(controllerPath))
            {
                // Writing into Library/PackageCache would corrupt the SDK asset, so refuse rather than damage it.
                message = "AnimatorController가 아직 package 원본입니다. 먼저 Local Controller Fix를 실행하세요.";
                return false;
            }

            List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);
            if (overrides.Count == 0)
            {
                message = "AnimatorOverrideController에 교체 가능한 슬롯이 없습니다.";
                return false;
            }

            bool changed = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                if (overrides[i].Value != clip)
                {
                    changed = true;
                }

                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, clip);
            }

            if (!changed)
            {
                message = "이미 같은 동작이 재생 슬롯에 연결되어 있습니다.";
                return true;
            }

            overrideController.ApplyOverrides(overrides);
            EditorUtility.SetDirty(overrideController);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(controllerPath))
            {
                AssetDatabase.ImportAsset(controllerPath);
            }

            return true;
        }

        /// <summary>
        /// The clip the avatar will actually perform, read from the override table rather than the inspector field.
        /// </summary>
        private AnimationClip GetPlaybackClip()
        {
            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                return null;
            }

            AnimatorOverrideController overrideController =
                animatorControllerProperty.objectReferenceValue as AnimatorOverrideController;
            if (overrideController == null || overrideController.overridesCount == 0)
            {
                return null;
            }

            List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                if (overrides[i].Value != null)
                {
                    return overrides[i].Value;
                }
            }

            return null;
        }

        private static void SelectAndPing(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private void FrameLoaderForScenePreview()
        {
            if (loader == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (loader == null)
            {
                return;
            }

            Selection.activeGameObject = loader;
            EditorGUIUtility.PingObject(loader);

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            Bounds bounds = GetLoaderPreviewBounds();
            sceneView.Frame(bounds, false);
            sceneView.LookAt(bounds.center, sceneView.rotation, Mathf.Max(1.6f, bounds.extents.magnitude * 1.8f));
            sceneView.Repaint();
        }

        private Bounds GetLoaderPreviewBounds()
        {
            if (loader == null)
            {
                return new Bounds(Vector3.up, Vector3.one * 2f);
            }

            Renderer[] renderers = loader.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = new Bounds(loader.transform.position + Vector3.up, Vector3.one * 2f);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds || bounds.size.sqrMagnitude < 0.01f)
            {
                bounds = new Bounds(loader.transform.position + Vector3.up, Vector3.one * 2.2f);
            }

            return bounds;
        }

        private bool LoaderHasPreviewRenderers()
        {
            if (loader == null)
            {
                return false;
            }

            Renderer[] renderers = loader.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
