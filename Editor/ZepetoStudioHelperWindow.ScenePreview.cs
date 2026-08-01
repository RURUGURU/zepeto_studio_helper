using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Play가 멈춰 있는 동안 Scene에 눈에 보이는 몸을 세워 둔다.
    ///
    /// 진짜 아바타는 런타임에 내려받으므로 Play 전의 LOADER는 빈 GameObject이고 Scene 뷰에는 기즈모 말고는
    /// 아무것도 없다 — 카메라가 제대로 된 곳을 보고 있는지조차 알 수 없다. 그래서 ZEPETO 기본 몸을 LOADER
    /// 위치에 대역으로 세워 준다.
    ///
    /// 이 오브젝트는 씬 파일에 절대 기록되지 않는다. HideFlags.DontSave를 달고 있어서 씬 diff도, 커밋도,
    /// export에 섞여 들어갈 일도 없다 — 그리고 Play가 시작되는 순간 제거되므로 진짜 아바타와 헷갈리거나
    /// 겹칠 수도 없다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string PreviewBodyName = "[미리보기] ZEPETO 기본 몸 - Play 하면 사라집니다";
        private const string PreviewBodyEnabledPrefKey = "com.easy.zepeto-helper.showPreviewBody";
        private const string ScenePreviewOverlayPrefKey = "com.easy.zepeto-helper.showScenePreviewOverlay";

        private GameObject previewBody;
        private bool previewBodySyncQueued;

        private static bool PreviewBodyEnabled
        {
            get { return EditorPrefs.GetBool(PreviewBodyEnabledPrefKey, true); }
            set { EditorPrefs.SetBool(PreviewBodyEnabledPrefKey, value); }
        }

        /// <summary>
        /// OnSceneViewGui가 Scene 뷰 위에 도움말 상자를 그릴지 여부. 보기 취향이므로 위의 대역 몸 토글과
        /// 마찬가지로 EditorPrefs다 — SessionState가 아니다. SessionState는 세션보다 오래 살아남으면 안 되는
        /// 상태를 위한 것이다.
        /// </summary>
        private static bool ScenePreviewOverlayEnabled
        {
            get { return EditorPrefs.GetBool(ScenePreviewOverlayPrefKey, true); }
            set { EditorPrefs.SetBool(ScenePreviewOverlayPrefKey, value); }
        }

        /// <summary>
        /// 대역 몸이 있어야 하는데 없으면 만들고, 있으면 안 되는데 있으면 지운다. 매 repaint마다 불러도 된다.
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

            // 이 도구가 Blender용으로 내보내는 fbx가 아니라 SDK 자신의 prefab을 쓴다.
            //
            // 내보낸 fbx는 materialImportMode를 떼고 기록되므로 그대로 Instantiate 하면 납작하고 시커먼 실루엣
            // 으로 렌더링된다 — 도움이 되기는커녕 고장 난 것처럼 보였다. package prefab은 진짜 피부·머리·눈
            // 머티리얼을 달고 있고, 아무것도 내보내지 않은 프로젝트에도 이미 있으며, 런타임 아바타가 바로
            // 그것으로부터 만들어진다. 그래서 대역이 Play에서 보게 될 모습과 일치한다.
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ZepetoBaseModelPath);
            if (source == null)
            {
                return;
            }

            previewBody = UnityEngine.Object.Instantiate(source);
            previewBody.name = PreviewBodyName;

            // DontSave가 이 오브젝트를 .unity 파일과 버전 관리 밖에 둔다. Hierarchy에는 일부러 보이게 남긴다 —
            // 설명 없이 나타나는 몸이, 사용자가 보고 이해할 수 있는 몸보다 나쁘다.
            previewBody.hideFlags = HideFlags.DontSave;

            Transform loaderTransform = loader.transform;
            previewBody.transform.SetPositionAndRotation(loaderTransform.position, loaderTransform.rotation);
            previewBody.transform.SetParent(null, true);

            // SDK prefab은 렌더러가 꺼진 채로 배포된다 — 런타임이 내려받은 부품으로 아바타를 다 만든 뒤에
            // 켜기 때문이다. 그대로 Instantiate 하면 완전히 보이지 않는 오브젝트가 씬에 놓이고, 그것은 기능이
            // 동작하지 않는 것과 똑같아 보인다. zepeto.character 3.1.32에서 계측한 값: 렌더러 2개, 그중
            // enabled이면서 active인 것 0개.
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
                // 도메인 리로드는 DontSave 오브젝트를 파괴하면서 이 필드는 아무것도 가리키지 않는 상태로
                // 남겨 두고, 이전 세션이 몸 하나를 두고 갔을 수도 있다. 낡은 사본이 쌓이지 않도록 이름으로
                // 훑어 지운다.
                RemoveStrayPreviewBodies();
                return;
            }

            UnityEngine.Object.DestroyImmediate(previewBody);
            previewBody = null;
            RemoveStrayPreviewBodies();
        }

        /// <summary>
        /// 더 이상 주인이 없는 대역 몸을 위한 회수 경로 — 이전 세션이 씬 파일에 두고 간 것들이다. 정상적으로
        /// 지워지는 길은 추적 중인 `previewBody` 참조 쪽이다.
        ///
        /// 판정 기준은 이름이고, 앞으로도 그래야 한다. "HideFlags.DontSave를 달고 있어야 한다"는 조건을 붙이지
        /// 말 것: 그러면 이 청소가 존재하는 이유인 바로 그 오브젝트들을 건너뛴다. Unity는 DontSave 오브젝트를
        /// 결코 직렬화하지 않으므로, 에디터를 껐다 켠 뒤에도 이 이름으로 살아남은 것은 플래그를 잃었다는 증거고
        /// (옛 빌드, Hierarchy 복제, 사용자가 직접 지운 경우) 정의상 새어 나온 대역이다 — 게다가 그것은 이제
        /// 진짜로 .unity 파일과 모든 .zepeto export에 들어간다. DontSave 조건은 이 청소를 두 겹으로 무의미하게
        /// 만들었다. FindObjectsOfType은 애초에 DontSave 오브젝트를 돌려주지도 않기 때문이다. 이름 자체가 충분히
        /// 자기 설명적이라("[미리보기] ... Play 하면 사라집니다") 사용자 씬의 다른 무엇도 이 이름을 쓰지 않는다.
        ///
        /// 가드는 하나만 남았고, 그것이 원래 원했던 가드다: 다른 열린 헬퍼 창이 아직 소유한 몸은 건드리지
        /// 않는다(예전에는 이쪽이 청소하는 순간 두 번째 창이 자기 대역을 잃었다).
        /// </summary>
        private static void RemoveStrayPreviewBodies()
        {
            ZepetoStudioHelperWindow[] windows = Resources.FindObjectsOfTypeAll<ZepetoStudioHelperWindow>();
            GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                if (candidate == null || candidate.name != PreviewBodyName)
                {
                    continue;
                }

                if (IsOwnedByLiveHelperWindow(candidate, windows))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        private static bool IsOwnedByLiveHelperWindow(GameObject body, ZepetoStudioHelperWindow[] windows)
        {
            if (windows == null)
            {
                return false;
            }

            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null && windows[i].previewBody == body)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Scene 뷰를 대역 몸의 정면으로 맞춘다. Blender 애드온이 캐릭터를 잡는 방향과 같게 둔다.
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

            // +Z 쪽에서 캐릭터를 바라본다. 부스 카메라가 있는 자리가 그쪽이다.
            sceneView.LookAt(bounds.center, Quaternion.LookRotation(Vector3.back, Vector3.up),
                Mathf.Max(1.4f, bounds.extents.magnitude * 2.4f));
            sceneView.Repaint();
        }

        /// <summary>
        /// 서로 상관없는 네 덩어리가 한 메서드에 있다. 순서대로:
        ///   1. 자가 치유 예약 — 대역 몸이 있어야 하는데 없으면 다음 프레임에 만들도록 예약한다.
        ///   2. 대역 몸 토글 + "초점 맞추기" 버튼.
        ///   3. Scene 뷰 오버레이 토글.
        ///   4. 상황에 따른 도움말 두 종류.
        /// 1번이 반드시 맨 앞이어야 한다. 이번 프레임이 아니라 다음 프레임을 예약하는 코드라서, previewBody에
        /// 의존하는 아래 컨트롤들보다 먼저 돌아야 한 프레임이라도 빨리 복구된다. Draw 메서드에서 기대하지 않을
        /// 유일한 블록이기도 하다.
        /// </summary>
        private void DrawPreviewBodySection()
        {
            // 자가 치유. 이게 없으면 SyncPreviewBody는 OnEnable과 play-mode 훅에서만 도는데, 둘 다 놓칠 수
            // 있다 — loader가 아직 바인딩되지 않은 도메인 리로드, 씬보다 먼저 열린 창, 다른 인스턴스가 쓸어
            // 간 대역 몸. 그 결과는 체크는 되어 있는데 화면에는 아무것도 없는 상태였고, 이는 기능이 고장 난
            // 것으로 읽힌다. 예약만 하고 OnGUI 안에서는 절대 실행하지 않는다: 레이아웃 도중의 Instantiate와
            // DestroyImmediate는 GUI 패스를 망가뜨린다.
            if (PreviewBodyEnabled
                && !EditorApplication.isPlayingOrWillChangePlaymode
                && loader != null
                && previewBody == null
                && !previewBodySyncQueued)
            {
                previewBodySyncQueued = true;
                EditorApplication.delayCall += () =>
                {
                    // 예약과 콜백 사이에 창이 닫힐 수 있다. 파괴된 EditorWindow는 == null로 비교되며, 이
                    // 검사가 없으면 콜백이 아무 창도 소유하지 않는 대역 몸을 만들어 놓고, 이미 없는 창에
                    // Repaint를 건다.
                    if (this == null)
                    {
                        return;
                    }

                    previewBodySyncQueued = false;
                    SyncPreviewBody();
                    Repaint();
                };
            }

            bool hasRig = AssetDatabase.LoadAssetAtPath<GameObject>(ZepetoBaseModelPath) != null;

            EditorGUILayout.BeginHorizontal();

            // 표시값은 hasRig를 섞지 않은 PreviewBodyEnabled 그대로다. 예전에는 `PreviewBodyEnabled && hasRig`를
            // 그려 놓고 토글이 돌려준 값을 그대로 저장했다. 기본 모델 prefab이 없는 프로젝트에서는 체크박스가
            // 항상 꺼진 채로 보이는데, 눌러도 true만 저장되고 화면은 그대로여서 "막혀 있다"가 아니라
            // "고장 났다"로 읽혔다 — 위 자가 치유 블록이 막으려던 것과 같은 종류의 실패다. 이제는 컨트롤을
            // 비활성으로 그리고, 이유는 아래 "기본 모델을 찾지 못했습니다" HelpBox가 설명한다. 컨트롤의 개수와
            // 순서는 그대로이므로 DisabledScope는 GUILayout 그룹에 영향을 주지 않는다.
            using (new EditorGUI.DisabledScope(!hasRig))
            {
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(
                    "정지 중 Scene에 기본 몸 보이기",
                    PreviewBodyEnabled);
                if (EditorGUI.EndChangeCheck())
                {
                    PreviewBodyEnabled = enabled;
                    SyncPreviewBody();
                    if (enabled)
                    {
                        FramePreviewBody();
                    }
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

            // Scene 뷰 오버레이 토글. 위의 대역 몸 토글과 똑같이 EditorPrefs 한 쌍을 읽고 쓰며, 읽는 쪽은
            // OnSceneViewGui 하나뿐이다. 여기 있는 모든 컨트롤이 그렇듯 무조건 그린다.
            EditorGUI.BeginChangeCheck();
            bool overlayEnabled = EditorGUILayout.ToggleLeft(
                "Scene View에 도움말 겹쳐 보이기",
                ScenePreviewOverlayEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                ScenePreviewOverlayEnabled = overlayEnabled;

                // Scene 뷰는 별개의 창이라 자기 일정대로 다시 그린다. 여기서 밀어 주지 않으면 다른 무언가가
                // 다시 그리게 만들 때까지 옛 상태가 화면에 남는다.
                SceneView.RepaintAll();
            }

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
