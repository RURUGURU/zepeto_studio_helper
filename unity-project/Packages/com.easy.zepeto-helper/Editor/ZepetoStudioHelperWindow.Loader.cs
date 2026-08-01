using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// ZEPETO 템플릿의 LOADER에 바인딩하고 SDK 재생 슬롯을 조작하는 부분.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void OnSceneViewGui(SceneView sceneView)
        {
            // 옛 showScenePreviewOverlay 필드가 아니라 ScenePreviewOverlayEnabled를 쓴다: 그 필드는 토글을
            // 잃어버린 채 아무 데서도 쓰이지 않아서 오버레이를 더 이상 끌 수 없었다. 이 pref 쌍은
            // ScenePreview.cs의 미리보기 몸 토글 옆에 산다.
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
                    // "선택"은 "초점"에 안내 문구 한 줄을 더한 것이 전부다. 예전에는 별도 메서드였는데 그
                    // 메서드가 FrameLoaderForScenePreview의 LOADER 재탐색 가드를 그대로 한 벌 더 들고 있었다.
                    FrameLoaderForScenePreview();
                    statusMessage = loader == null
                        ? "LOADER를 찾지 못했습니다. 작업 준비 / Setup에서 LOADER를 다시 찾으세요."
                        : "Scene View에서 LOADER를 선택했습니다. 아바타와 의상 관통을 확인하세요.";
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

        // [QC][Guard:repaint_cost]
        // 이 게터들은 OnGUI에서 불린다. LOADER가 없으면 재바인딩이 매 repaint마다 씬의 모든 루트를 훑게 되므로
        // 재바인딩에 시간 제한을 둔다. 사용자가 명시적으로 누른 동작은 타이머를 초기화해서 즉시 다시 찾게 한다.
        //
        // 다른 파일에서 보이는 `lastLoaderSearchTime = -1000d`가 그 초기화 관용구다. 실제 시각과 절대 겹칠 수
        // 없는 음수를 넣어 다음 EnsureLoaderBinding 호출이 무조건 다시 찾게 만드는 "강제 재탐색"이다.
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
            // ZEPETO export나 도메인 리로드는 캐시된 SerializedObject 뒤의 대상을 파괴할 수 있다. false를
            // 돌려주면 호출자가 "target has been destroyed" 예외를 맞는 대신 LOADER를 다시 찾을 수 있다.
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
            // 이 메서드는 GetCurrentZepetoId를 통해 OnGUI에서 돌기 때문에 사용자가 입력 중인 값을 절대 덮어써서
            // 는 안 된다. LOADER가 없어도 아이디 입력란은 값을 유지해서 계정 등록은 계속할 수 있다.
            if (loader == null)
            {
                return;
            }

            // [QC][Invariant:field_binding_scope]
            // 공식 SDK에서 이 세 필드는 서로 다른 두 컴포넌트에 흩어져 있다:
            //   zepetoId          -> Zepeto.ZepetoCharacterCustomLoader
            //   AnimationClip     -> ZEPETO.Studio.PlaygroundController
            //   AnimatorController-> ZEPETO.Studio.PlaygroundController
            // 두 컴포넌트가 모두 LOADER 오브젝트에 붙어 있는지는 템플릿 씬을 어떻게 짰느냐에 달렸으므로, 바로
            // 포기하지 않고 오브젝트 자신 -> 자식들 -> 씬 전체 순으로 검색 범위를 넓힌다.
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

        /// <summary>
        /// 넘겨받은 컴포넌트들을 훑으며 아직 비어 있는 필드만 채운다.
        ///
        /// 컴포넌트마다 SerializedObject를 새로 만드는 것은 낭비가 아니다. 세 필드가 서로 다른 컴포넌트에서
        /// 오는 것이 정상이므로(위 field_binding_scope 참고) 함께 캡처해 두는 SerializedObject도 필드마다
        /// 달라야 한다.
        ///
        /// 필드별로 먼저 찾은 것이 이긴다. 그래서 호출자는 가장 좁은 범위(LOADER 자신)부터 시작해 점점
        /// 넓히고, 세 개가 다 차면 그 자리에서 멈춘다.
        /// </summary>
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
        // GameObject.Find는 활성 씬의 활성 오브젝트만 본다. 템플릿 LOADER는 비활성일 수도 있고 additive로 로드된
        // 씬에 있을 수도 있으므로, 로드된 모든 씬의 루트 계층을 대신 검색한다.
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
        // 헬퍼는 SDK 자신의 package 파일에 절대 쓰면 안 된다 — EnsureLocalAnimatorController가 controller를 먼저
        // Assets/ZepetoHelper/Controllers로 복사하는 이유가 그것이다. 다만 읽기 전용인지는 경로 SEGMENT의
        // 성질이지 부분 문자열의 성질이 아니다: "PackageCache"를 아무 자리에서나 매칭했더니
        // "Assets/MyPackageCache/Controllers" 같은 멀쩡히 쓸 수 있는 사용자 폴더까지 유죄 판정을 받았고, 그러면
        // Play가 "local controller를 만들라"는, 이미 충족됐고 다시는 충족될 수 없는 요구 앞에서 막다른 길에
        // 부딪혔다.
        private static bool IsPackageOrPackageCachePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            // AssetDatabase는 경로를 슬래시(/)로 주지만, 손으로 만들었거나 로그에서 읽어 온 경로는 역슬래시일
            // 수 있다.
            string normalized = assetPath.Replace('\\', '/');
            return normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase)
                || normalized.IndexOf("/Library/PackageCache/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 재생 controller가 프로젝트 안에 쓰기 가능한 사본으로 존재하도록 보장한다. 이미 project-local이면
        /// 아무것도 하지 않고 true를 돌려준다.
        ///
        /// 80줄에 종료 지점이 여덟 개라 미리 지도를 둔다. false로 끝나는 것: 필드를 못 찾음 / Play 중 /
        /// SerializedObject가 끊어짐 / SDK 기본 controller까지 없음 / 에셋 경로를 못 구함 / 복사 실패 /
        /// 만든 사본을 다시 읽지 못함. true로 끝나는 것: 이미 project-local / 사본을 만들어 연결함.
        ///
        /// 가장 뜻밖의 갈래는 참조가 비어 있을 때다 — 실패시키지 않고 SDK 기본 override controller를 조용히
        /// 대신 집어넣는다. 아래 해당 위치의 주석 참고.
        /// </summary>
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
                // 참조가 비어 있는 것은 복구 가능한 상태다: SDK는 기본 override controller를 항상 함께
                // 배포하므로, "AnimatorController가 비었다"로 사용자를 막다른 길에 세우는 대신 그것으로
                // 되돌아간다. 대체가 조용히 일어난다는 점은 알고 있어야 한다 — 사용자가 일부러 비워 둔 필드도
                // 여기서 SDK 기본값으로 채워진다.
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

        /// <summary>
        /// controller를 Assets 아래의 사본으로 만든다. 타입에 따라 경로가 둘로 갈리는 이유가 있다.
        ///
        /// AnimatorOverrideController는 메모리에서 Instantiate 한 뒤 CreateAsset으로 쓴다. 그래야 override
        /// 슬롯 매핑을 그대로 들고 오면서 이름만 PlaygroundAnimatorController_local로 바꿔 달 수 있다.
        /// 그 밖의 controller 타입은 그렇게 손볼 상태가 없으므로 AssetDatabase.CopyAsset으로 파일을 통째로
        /// 복사하는 쪽이 단순하고 안전하다.
        /// </summary>
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

        /// <summary>
        /// LOADER의 직렬화된 AnimationClip 필드에 클립을 연결하고, 이어서 재생 슬롯(override 테이블)까지 다시
        /// 쓴다. 두 번째가 없으면 아바타가 하는 동작은 바뀌지 않는다 — 아래 motion_playback 블록 참고.
        /// </summary>
        /// <param name="preserveClipStageComplete">
        /// true면 3번 단계의 완료 표시를 유지한다. 임시 Play 프리뷰가 쓰는 값이다.
        ///
        /// 호출부에서 벌거벗은 true/false로만 보여 뜻이 드러나지 않지만 enum으로 바꿀 수 없다. 시그니처가
        /// 고정돼 있다: ZepetoCustomMotionRun.cs가 이 메서드를 리플렉션으로 `new object[] { customClip, false }`
        /// 처럼 부르므로 매개변수 타입을 바꾸면 하네스가 조용히 깨진다. 대신 호출부에서
        /// `preserveClipStageComplete: true`처럼 이름 붙은 인자를 쓰면 시그니처를 그대로 두고도 읽힌다.
        /// </param>
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
            // 임시 Play 프리뷰는 preserveClipStageComplete=true로 부르므로, 프리뷰 때문에 이미 완료된 워크플로
            // 단계가 풀리지 않는다. 진짜 클립 변경은 아래에서 일부러 3번 단계를 초기화한다.
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
            // 이 프로젝트에서 가장 비싸게 배운 사실이다. PlaygroundController.AnimationClip에 값을 넣는 것만
            // 으로는 아바타가 하는 동작이 바뀌지 않는다. SDK의 ZepetoBaseModel controller는 "dynamic"이라는
            // 이름의 슬롯 하나만 재생하고, PlaygroundAnimatorController는 그 슬롯이 A_pose에 매핑된 채로
            // 배포된다 — 0.04초(정확히는 0.0417초)짜리 제자리 서 있는 포즈다. override 테이블을 다시 쓰지
            // 않는 한, 어떤 동작을 골라 넣어도 아바타는 그냥 서 있는다. 즉 위의 직렬화 필드 대입은 인스펙터
            // 표시용이고, 재생을 실제로 바꾸는 것은 바로 아래 ApplyClipToOverrideController 호출이다.
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
        /// project-local AnimatorOverrideController의 모든 슬롯을 주어진 클립으로 다시 쓴다. 아바타가 실제로
        /// 그 동작을 하게 만드는 것은 이 메서드이며, 직렬화된 AnimationClip 필드만 바꾸는 것으로는 아무 일도
        /// 일어나지 않는다. 이 규칙의 정본은 여기다 — 다른 파일의 같은 이야기는 각자의 국소적 결과만 적는다.
        ///
        /// "dynamic" 하나가 아니라 슬롯 전부를 덮어쓰는 이유: 재생되는 슬롯이 그 하나인 것은 SDK 쪽 사정이고,
        /// 이름으로 찾아 들어가면 이름이 어긋나는 순간 조용히 빗나간다. 그때 아바타는 A_pose로 서 있고 화면에는
        /// 아무 흔적도 남지 않는다. 이 controller는 헬퍼가 만든 로컬 사본이라 다른 슬롯을 덮어써서 잃을 것도
        /// 없으므로, 이름에 걸지 않고 전부 같은 클립으로 맞춘다.
        ///
        /// 반환값 주의: 모든 슬롯이 이미 이 클립이면 아무것도 쓰지 않고 true를 돌려준다. "할 일이 없었다"도
        /// 성공이다. 호출자가 true를 "방금 파일을 썼다"로 읽으면 안 된다.
        ///
        /// Play 중에는 부르지 말 것. ApplyOverrides + SaveAssets + ImportAsset이 곧 재바인딩이고, 그것이
        /// ZEPETO context를 깨뜨린다 (LivePreview.cs의 클래스 주석 참고).
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
                // Library/PackageCache에 쓰면 SDK 에셋이 망가진다. 망가뜨리는 대신 거절한다.
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
        /// 아바타가 실제로 재생하게 될 클립. 인스펙터의 AnimationClip 필드가 아니라 override 테이블에서 읽는다
        /// — 재생을 결정하는 쪽이 그것이기 때문이다.
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

        /// <summary>
        /// Scene 뷰가 LOADER를 잡을 때 쓸 경계 상자. 렌더러가 하나도 없어도 반드시 쓸 만한 값을 돌려준다.
        ///
        /// Play 전의 LOADER는 사실상 빈 GameObject다 — 진짜 아바타는 런타임에 내려받으므로 감쌀 mesh가 없다.
        /// 그때 Bounds가 0이면 Scene 뷰가 엉뚱하게 튀거나 극단적으로 확대돼서 "초점 맞추기"가 고장 난 것처럼
        /// 보인다. 그래서 렌더러가 없거나 다 합쳐도 크기가 0에 가까우면 LOADER 위치 기준의 사람 크기 상자로
        /// 되돌아간다.
        /// </summary>
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
