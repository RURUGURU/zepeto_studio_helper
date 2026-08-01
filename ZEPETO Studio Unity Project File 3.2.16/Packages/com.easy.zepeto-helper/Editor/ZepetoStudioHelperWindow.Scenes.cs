using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// LOADER가 들어 있는 작업 scene을 찾아내고 여는 부분.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // 작업 scene 선택은 목록 위치가 아니라 GUID다. workSceneGuids는 RefreshWorkSceneCandidates가 불릴 때마다
        // 다시 만들어지고 다시 정렬되므로, 팝업을 클릭한 순간에 붙잡아 둔 인덱스는 scene이 하나라도 추가·개명·
        // 삭제되는 즉시 다른 scene을 가리키기 시작했다 — 그리고 "씬 열기"는 화면에 아무 변화도 없이 그 다른
        // scene을 열었다. selectedWorkSceneIndex(창 셸 partial)는 팝업의 표시 위치로만 남아 있고, 목록을 다시
        // 만들 때마다 이 GUID에서 되계산된다.
        private string selectedWorkSceneGuid = string.Empty;

        // 창 셸 partial의 StageComplete 키 세 개는 세션 범위이지 scene 범위가 아니다. 그래서 LOADER가 들어 있는
        // 다른 scene을 열면, 이전 scene에서 완료한 단계가 "완료됨"으로 그대로 보였다. 이 키는 예약된 진행 상황이
        // 어느 scene의 것인지를 기록한다.
        private const string StageProgressSceneGuidSessionKey = "Easy.ZepetoHelper.StageComplete.SceneGuid";

        // [QC][Invariant:no_hardcoded_scene]
        // ZEPETO SDK 패키지는 작업 scene을 함께 배포하지 않는다. 그러므로 헬퍼는 Assets/Playground.unity 같은
        // 고정 경로를 가정하지 말고, 프로젝트 안에서 실제로 LOADER를 가진 scene을 직접 찾아내야 한다.
        //
        // 네 단계로 돈다: (1) 활성 scene 감시를 걸고, (2) 곧 교체될 배열을 참조하는 팝업 인덱스를 먼저 GUID로
        // 확정하고, (3) 목록을 다시 만들어 정렬·필터하고, (4) 그 GUID를 새 배열의 인덱스로 되돌린 뒤 단계 진행
        // 상황을 활성 scene에 맞춘다. 2번과 4번의 순서가 이 메서드의 전부다.
        private void RefreshWorkSceneCandidates()
        {
            EnsureActiveSceneWatch();

            // 팝업 인덱스는 아직 곧 교체될 배열을 가리키고 있으므로, 배열을 다시 만들기 전에 GUID로 확정한다.
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

                    // 텍스트로 읽지 못한 scene도 목록에 넣되 꼬리표를 붙인다. 숨기는 쪽이 더 나빴고, 아무도
                    // 확인하지 않은 채로 LOADER가 있다고 주장하는 것은 거짓말이기 때문이다.
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
        /// 표시 인덱스 -> 선택된 GUID. 그 인덱스가 선택될 때 쓰인 배열을 기준으로 푼다. 반드시 그 배열이
        /// 교체되기 전에 돌아야 한다.
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
        /// 선택된 GUID -> 표시 인덱스. 선택했던 scene이 목록에서 사라졌으면 첫 후보로 되돌아간다. 예전에
        /// 원시 인덱스를 clamp 하던 것이 하던 일이 이것이다.
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
        /// .unity 파일을 텍스트로 훑어 LOADER 오브젝트를 찾는다. 싸고, scene을 로드할 필요가 없다.
        ///
        /// 반환값은 bool 두 개지만 답은 세 가지다: 있다(true, contentUnverified=false) / 없다(false) /
        /// 알 수 없다(true, contentUnverified=true). 세 번째를 두 번째로 접으면 안 된다.
        ///
        /// 이 방법은 YAML(텍스트) scene에서만 통한다. 바이너리나 압축 형식으로 저장된 scene은 이렇게 훑을 수
        /// 없는데, 그런 scene을 조용히 버렸더니 프로젝트에 멀쩡한 작업 scene을 두고도 사용자가
        /// "LOADER가 들어 있는 scene을 찾지 못했습니다"만 보게 됐다. 그래서 그런 scene은 contentUnverified를
        /// 켠 채 후보로 돌려주고, 호출자는 숨기는 대신 꼬리표를 달아 목록에 올린다.
        ///
        /// 의도적 제한: 앞의 200,000줄까지만 읽는다. LOADER가 그 뒤에 나오는 텍스트 scene은 LOADER가 없는
        /// 것으로 보고된다.
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

                    // 텍스트 scene은 예외 없이 YAML 헤더로 시작한다. 그 밖의 것은 바이너리/압축이고, 텍스트로
                    // 읽어서는 있다고도 없다고도 확인할 수 없다.
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
                // 존재하지만 읽을 수 없는 파일(잠겨 있거나, 읽는 도중 스트림 오류)은 바이너리 scene과 같은
                // 상황이다: 확인 불가이므로 꼬리표를 달아 후보로 내놓는다. 경로 자체를 풀지 못한 것은 애초에
                // 후보가 아니다.
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

            // GUID로 연다. 바로 위의 refresh가 목록을 다시 만들고 다시 정렬했으므로 팝업 인덱스는 이제 표시
            // 위치일 뿐이고, 사용자가 고른 것과 다른 scene을 가리키기 쉽다.
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

            // 강제 재탐색: EnsureLoaderBinding의 시간 제한을 무효화해 다음 호출이 무조건 다시 찾게 한다
            // (Loader.cs의 [QC][Guard:repaint_cost] 참고). scene이 바뀌었으므로 옛 LOADER 참조는 무의미하다.
            lastLoaderSearchTime = -1000d;
            RefreshAll();
            statusMessage = "작업 scene을 열었습니다: " + scenePath;
        }

        /// <summary>
        /// OpenSelectedWorkScene을 거치지 않는 scene 변경을 알아챈다 — Project 창에서 scene을 더블클릭하거나
        /// File &gt; Open Scene으로 여는 경우.
        ///
        /// OnEnable이 아니라 RefreshWorkSceneCandidates에서 구독하는 이유는 OnEnable을 창 셸 partial이 소유하기
        /// 때문이다. +=  앞의 -=가 이것을 멱등으로 만들어서, 매 refresh마다 다시 돌려도 핸들러가 쌓이지 않는다.
        /// 구독 해제는 OnDestroy가 한다. 도메인 리로드는 어차피 static 이벤트를 통째로 날리고, 그 뒤 첫 refresh가
        /// 다시 구독한다.
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
            // 이미 파괴된 창에서 이 핸들러가 도는 경우를 막는다. sceneOpened는 static 이벤트이고 호출 목록은
            // 이벤트가 발행되는 순간 스냅샷되므로, 그 뒤에 파괴된 창의 핸들러도 그대로 실행된다. 파괴된
            // EditorWindow는 == null로 비교되고, 거기에 Repaint를 걸면 예외가 난다.
            //
            // 구독 해제 자체는 OnDestroy(-> UnsubscribeActiveSceneWatch)가 확실히 한다. 자체 테스트가
            // CreateInstance로 만든 임시 창을 DestroyImmediate 할 때도 OnDestroy는 정상적으로 발생하므로
            // (LivePreview.cs의 OnDestroy 주석 참고) 그 경로는 여기서 걱정할 대상이 아니다.
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
        /// 다른 scene에서 얻은 단계 진행 상황을 버린다. 그래야 1/2/3번 단계가 다른 LOADER에서 한 작업을 두고
        /// "완료됨"이라고 주장하지 못한다.
        /// </summary>
        private void SyncWorkflowStageProgressToActiveScene()
        {
            // Play가 돌거나 시작하는 중에는 절대 하지 않는다. 그때는 scene이 바뀔 수도 없거니와, Play 도중에
            // 단계 플래그를 지우면 2/3/4번 단계가 대기중으로 무너지면서 라이브 프리뷰 패널이 Stop 버튼째
            // 화면 밖으로 사라진다 — clothingPrefab의 [SerializeField]가 막으려고 존재하는 바로 그 실패다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string activeGuid = GetActiveSceneGuid();

            // 저장되지 않은/이름 없는 scene은 진행 상황을 묶어 둘 에셋이 없다. 매 refresh마다 플래그를 지우는
            // 대신 예약을 그대로 둔다.
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
                // 아직 아무 scene에도 묶여 있지 않았으므로 버릴 "다른 scene의 진행 상황"이 없다. 사용자가 바로
                // 이 scene에서 얻은 플래그를 지우는 대신, 예약된 것을 이 scene의 것으로 받아들인다.
                return;
            }

            // 연쇄적으로 지워진다: 1번 단계를 지우면 2번과 3번도 함께 지워진다.
            SetAvatarOutfitStageComplete(false);
            statusMessage = "다른 scene을 열었으므로 1~3번 단계의 완료 표시를 지웠습니다.";
        }
    }
}
