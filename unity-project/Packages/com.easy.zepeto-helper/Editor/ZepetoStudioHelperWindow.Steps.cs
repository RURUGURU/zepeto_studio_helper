using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 사용자가 실제로 눌러 나가는 step 카드들의 속. 카드의 틀은 Flow.cs가, 속은 여기가 그린다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// 문제가 생겼을 때 빠져나오는 패널. 언제나 그려지고, 조건부로 존재하지 않는다.
        /// </summary>
        /// <remarks>
        /// 예전에는 아무 문제가 없으면 BeginVertical 앞에서 `return`했다. 그 바람에 패널 전체 - 컨트롤 열 개,
        /// 그중 여섯 개는 가로 그룹 둘 안에 있다 - 가 sessionErrorCount와 안전 스냅샷에 따라 나타났다 사라졌다
        /// 했다. sessionErrorCount는 Application.logMessageReceived가 콜백에서 바꾸는 static이고, 안전
        /// 스냅샷은 그리는 도중 2초 타이머로 스스로 갱신된다. 둘 다 한 번의 OnGUI에서 Layout 패스와 Repaint
        /// 패스 사이에 값이 뒤집힐 수 있고, 뒤집히면 GUILayout 그룹이 깨진다. 그리고 그것을 뒤집는 상황이
        /// 바로 SDK 예외 폭주 - 이 패널이 사용자를 구하려고 존재하는 바로 그 상황이다.
        /// </remarks>
        private void DrawWarningCleanupPanel()
        {
            SafetySnapshot snapshot = GetSafetySnapshot(false);
            bool hasPackageController = IsPackageOrPackageCachePath(GetAnimatorControllerPath());
            // HasWarning은 Level != Ok이고 HasBlockingRisk는 Level == HardBlock이라 앞이 뒤를 이미 포함한다.
            bool hasProblem = snapshot.HasWarning
                || hasPackageController
                || sessionErrorCount > 0;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("경고 복구 / Warning Cleanup", EditorStyles.boldLabel);

            MessageType problemType;
            string problemText = GetWarningPanelMessage(snapshot, hasPackageController, out problemType);
            DrawMiniHelp(problemText, problemType);

            // 복구 버튼 넷은 겹쳐 보이지만 하는 일이 서로 다르다.
            //   Stop Play       - Play만 끝낸다. 카운터도 로그 기준선도 건드리지 않는다.
            //   Recover         - Play를 끝내고 + 콘솔을 지우고 + 세션 카운터와 로그 기준선을 다시 잡고
            //                     + 스냅샷을 강제로 다시 잰다. 막혔을 때 제일 먼저 누를 버튼.
            //   Clear Console   - 콘솔과 카운터/기준선만 정리한다. Play는 그대로 둔다.
            //   Fresh Log Check - 아무것도 지우지 않고 다시 재기만 한다. 밖에서 원인을 고친 뒤 확인용.
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Stop Play", GUILayout.Height(26f)))
                {
                    StopPlayMode();
                }
            }

            if (DrawBlueActionButton("Recover", true, GUILayout.Height(26f)))
            {
                RecoverSafetyState();
            }

            if (DrawSecondaryButton("Clear Console", GUILayout.Height(26f)))
            {
                ClearConsoleAndSessionSummary();
            }

            if (DrawSecondaryButton("Fresh Log Check", GUILayout.Height(26f)))
            {
                RefreshSafetySnapshot();
                ValidateState();
                statusMessage = "Fresh log check complete. / 새 로그 상태를 다시 확인했습니다.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(animatorControllerProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Local Controller Fix", GUILayout.Height(24f)))
                {
                    string controllerMessage;
                    if (EnsureLocalAnimatorController(out controllerMessage))
                    {
                        statusMessage = controllerMessage;
                    }
                    else
                    {
                        statusMessage = "Local controller fix failed: " + controllerMessage;
                    }

                    ValidateState();
                }
            }

            if (DrawSecondaryButton("Package Cache Guide", GUILayout.Height(24f)))
            {
                statusMessage = "package cache warning이 이미 찍혔다면 Unity 종료 후 " + GetPackageCacheFolderHint()
                    + "를 재생성하면 됩니다. helper는 이후 Assets/ZepetoHelper local asset만 수정합니다.";
            }
            EditorGUILayout.EndHorizontal();

            // 이것도 무조건 그린다. 스냅샷으로 존재를 결정했더니 위와 똑같은 Layout/Repaint 불일치가 되살아났다.
            bool emergency = snapshot.HasBlockingRisk || snapshot.LogSizeBytes >= LogGrowthBlockBytes;
            using (new EditorGUI.DisabledScope(!emergency && !hasProblem))
            {
                if (DrawPrimaryButton("Emergency Stop Preview / 긴급 정지", emergency || hasProblem))
                {
                    StopPlayMode();
                    ClearConsoleAndSessionSummary();
                    statusMessage = "Emergency stop complete. / 미리보기와 세션 경고를 정리했습니다.";
                }
            }

            showSafetyAdvanced = DrawAdvancedFoldout(showSafetyAdvanced);
            if (showSafetyAdvanced)
            {
                DrawStatusRow("Log Size", FormatBytes(snapshot.LogSizeBytes));
                DrawStatusRow("AnimatorController", string.IsNullOrEmpty(GetAnimatorControllerPath()) ? "없음 / Missing" : GetAnimatorControllerPath());

                if (!string.IsNullOrEmpty(snapshot.Detail))
                {
                    DrawMiniHelp(snapshot.Detail, MessageType.None);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(snapshot.LogPath)))
                {
                    if (DrawSecondaryButton("로그 위치 열기 / Open Log Folder", GUILayout.Height(24f)))
                    {
                        OpenLogLocation(snapshot.LogPath);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 패널 맨 위 한 줄이 무엇을 말할지만 고른다. (snapshot, hasPackageController)만 보는 순수 함수라
        /// 그리기와 섞이지 않고, 아래 컨트롤들은 이 판단을 다시 계산하지 않는다.
        /// 문제가 없을 때도 반드시 문장 하나를 돌려준다 - 빈 문자열이면 help box가 통째로 사라져 컨트롤
        /// 개수가 프레임마다 달라진다.
        /// </summary>
        private static string GetWarningPanelMessage(SafetySnapshot snapshot, bool hasPackageController, out MessageType type)
        {
            if (snapshot.HasBlockingRisk)
            {
                type = MessageType.Error;
                return "Play가 막힌 이유: " + snapshot.Message;
            }

            if (snapshot.HasWarning)
            {
                type = MessageType.Warning;
                return "복구 가능한 경고입니다. 복구 후 다시 시도하세요: " + snapshot.Message;
            }

            if (hasPackageController)
            {
                type = MessageType.Warning;
                return "AnimatorController가 package cache를 가리킵니다. local copy로 바꿔야 package cache warning을 피할 수 있습니다.";
            }

            type = MessageType.None;
            return "이상 없음. 문제가 생기면 여기 이유가 표시되고 아래 버튼으로 복구합니다.";
        }

        // 이 안내문에는 버전 번호를 손으로 적지 않는다. 예전에는 "zepeto.studio@3.2.12"가 박혀 있었는데 그건
        // MinimumPackageVersion이지 설치된 버전이 아니어서, zepeto.studio 3.2.16을 쓰는 이 프로젝트에서는
        // 있지도 않은 폴더를 지우라고 시켰다. 감지된 버전을 그대로 쓰고, 못 찾으면 자리표시자를 남긴다.
        private static string GetPackageCacheFolderHint()
        {
            string version;
            string source;
            bool detected = TryGetZepetoStudioPackage(out version, out source);
            return "Library/PackageCache/" + RequiredPackage + (detected ? "@" + version : "@<설치된 버전>");
        }

        // [AUDIT][Risk:Critical][Scope:play_stability]
        // "아바타는 뜨는데 전혀 움직이지 않는다"의 가장 흔한 원인이다. Play가 도는 동안 Unity가 스크립트를
        // 다시 컴파일하면 도메인이 리로드되고, ZEPETO 컨텍스트의 내부가 전부 null인 채로 남는다. 증상은
        // ZepetoContext / SwingBoneProcessor에서 매 프레임 터지는 NullReferenceException 폭주이고, 안전
        // 스냅샷이 그걸 이미 감지하기는 한다 - 그러나 벌어진 뒤에 감지하는 것은 애초에 못 벌어지게 하는
        // 것보다 나쁘다.
        private static bool IsRecompileDuringPlayUnsafe()
        {
            return EditorPrefs.GetInt(ScriptCompilationDuringPlayPrefKey, RecompileAndContinuePlaying)
                == RecompileAndContinuePlaying;
        }

        private void DrawRecompileDuringPlayGuard()
        {
            if (!IsRecompileDuringPlayUnsafe())
            {
                return;
            }

            DrawMiniHelp(
                "Unity 설정 경고: 지금 설정은 Play 도중 스크립트가 바뀌면 바로 다시 컴파일합니다. "
                + "그러면 ZEPETO SDK 내부 상태가 끊어져서 아바타가 멈추고 NullReferenceException이 계속 쏟아집니다. "
                + "아래 버튼을 누르면 Play가 끝난 뒤에 컴파일하도록 바꿉니다.",
                MessageType.Warning);

            if (DrawBlueActionButton("Play 중 재컴파일 끄기 (권장)", true, GUILayout.Height(28f)))
            {
                EditorPrefs.SetInt(ScriptCompilationDuringPlayPrefKey, RecompileAfterFinishedPlaying);
                statusMessage = "Unity 설정을 바꿨습니다: Play가 끝난 뒤에 스크립트를 컴파일합니다. "
                    + "(Preferences > General > Script Changes While Playing)";
                Repaint();
            }
        }

        /// <summary>
        /// 아이디 입력칸과 적용 버튼. 예전 step 1 카드에서 떼어내 flow 레이아웃이 직접 배치할 수 있게 했다.
        /// 그 시절의 1-1 / 1-2 하위 블록은 없다 - 이제 step 1은 카드 한 장이다.
        /// </summary>
        private void DrawZepetoIdRow(WorkflowStatus workflow)
        {
            string currentId = string.IsNullOrEmpty(workflow.CurrentZepetoId) ? "ID 없음" : workflow.CurrentZepetoId;
            DrawStatusRow("현재 아이디", currentId);

            // 아이디는 직접 타이핑한다. 예전에는 드롭다운과 추가/삭제 버튼이 달린 저장된 아이디 목록이 있었다.
            // 짧고, 거의 안 바뀌고, 바로 아래 '현재' 줄에 이미 보이는 값 하나를 지키자고 컨트롤 셋을 더 둔 셈이었다.
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                zepetoIdText = EditorGUILayout.TextField("아이디", zepetoIdText);
            }
            if (EditorGUI.EndChangeCheck())
            {
                zepetoIdText = SanitizeZepetoId(zepetoIdText);
            }

            string typedId = SanitizeZepetoId(zepetoIdText);
            string idFormatError = GetZepetoIdFormatError(typedId);
            if (!string.IsNullOrEmpty(typedId) && !string.IsNullOrEmpty(idFormatError))
            {
                DrawMiniHelp(idFormatError, MessageType.Warning);
            }

            bool isValidTypedId = !string.IsNullOrEmpty(typedId) && string.IsNullOrEmpty(idFormatError);
            bool canApplyId = zepetoIdProperty != null
                && isValidTypedId
                && !string.Equals(typedId, SanitizeZepetoId(workflow.CurrentZepetoId), StringComparison.OrdinalIgnoreCase)
                && !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(!canApplyId))
            {
                if (DrawBlueActionButton("ID 적용", canApplyId, GUILayout.Height(30f)))
                {
                    ApplyZepetoId(zepetoIdText);
                }
            }

            if (zepetoIdProperty == null && loader != null)
            {
                DrawMiniHelp("LOADER에 zepetoId 필드가 없어 적용할 수 없습니다. ZEPETO Studio 템플릿의 LOADER인지 확인하세요.", MessageType.Warning);
            }

            if (loader == null)
            {
                DrawMiniHelp("LOADER가 없으면 ID를 적용할 대상이 없습니다. 먼저 LOADER를 찾습니다.", MessageType.Warning);
                if (DrawBlueActionButton("LOADER 찾기", !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(30f)))
                {
                    FindLoaderAndSerializedFields();
                    ValidateState();
                }
            }
            else
            {
                GUILayout.Label("LOADER 연결됨. 여기서는 아이디만 확인하면 됩니다.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>
        /// 의상 고르기. 고르기와 적용이 일부러 두 걸음으로 나뉘어 있다.
        /// </summary>
        /// <remarks>
        /// 드롭다운은 pendingClothingPrefab을 바꾸고, 파란 '의상 적용' 버튼만이 clothingPrefab을 바꾼다.
        /// 뒤의 것이 진짜 적용된 의상이라 WorkflowStatus.HasOutfit과 export가 전부 그쪽을 본다. 목록에서
        /// 이것저것 눌러 보는 동안 적용 상태가 따라 바뀌면, 사용자는 자기가 무엇을 내보내는지 알 수 없다.
        /// 고르기만 해도 1번 완료 표시가 풀리는 이유도 같다 - 고른 것과 적용한 것이 어긋난 상태를
        /// "완료"라고 부를 수는 없다.
        /// </remarks>
        private void DrawOutfitChoiceRow(WorkflowStatus workflow)
        {
            List<GameObject> prefabs = FindAllOutfitPrefabs();
            if (prefabs.Count == 0)
            {
                DrawStatusRow("의상 / Outfit", ContentsRoot + " 아래 prefab 없음");
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (DrawBlueActionButton("의상 목록 새로고침", !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(28f)))
                    {
                        FindDefaultClothingPrefab();
                        ValidateState();
                    }
                }

                return;
            }

            if (pendingClothingPrefab == null && clothingPrefab != null)
            {
                pendingClothingPrefab = clothingPrefab;
            }

            string[] options = BuildOutfitPopupOptions(prefabs);
            int currentIndex = GetOutfitPopupIndex(prefabs, pendingClothingPrefab);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                currentIndex = EditorGUILayout.Popup("의상 선택", currentIndex, options);
            }

            if (EditorGUI.EndChangeCheck())
            {
                pendingClothingPrefab = currentIndex <= 0 ? null : prefabs[currentIndex - 1];
                SetAvatarOutfitStageComplete(false);
                ValidateState();
                Repaint();
            }

            GUILayout.Label("목록에서 테스트할 prefab을 직접 선택합니다.", EditorStyles.wordWrappedMiniLabel);

            bool hasPendingOutfit = pendingClothingPrefab != null;
            bool isSameAsApplied = pendingClothingPrefab != null && pendingClothingPrefab == clothingPrefab;
            string applyLabel = isSameAsApplied ? "의상 적용됨" : "의상 적용";
            if (DrawBlueActionButton(applyLabel, hasPendingOutfit && !isSameAsApplied && !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(30f)))
            {
                ApplySelectedOutfitPrefab();
            }

            if (workflow.HasOutfit)
            {
                DrawStatusRow("적용된 의상", clothingPrefab.name);
            }
        }

        private void ApplySelectedOutfitPrefab()
        {
            if (pendingClothingPrefab == null)
            {
                statusMessage = "적용할 의상 prefab을 먼저 선택하세요.";
                ValidateState();
                return;
            }

            clothingPrefab = pendingClothingPrefab;
            SetAvatarOutfitStageComplete(false);
            SetClipStageComplete(false);
            statusMessage = "의상 적용됨: " + clothingPrefab.name + ". Play로 확인한 뒤 아래 적용 버튼을 누르세요.";
            ValidateState();
            Repaint();
        }

        private void DrawAvatarOutfitApplyButton(WorkflowStatus workflow)
        {
            bool canComplete = workflow.HasAvatarPlayInputs
                && workflow.HasOutfit
                && workflow.OutfitIsUnderContents
                && !EditorApplication.isPlayingOrWillChangePlaymode;

            if (DrawBlueActionButton(avatarOutfitStageComplete ? "1번 완료됨" : "1번 적용", canComplete, GUILayout.Height(34f)))
            {
                SetAvatarOutfitStageComplete(true);
                statusMessage = "1번 아바타 준비가 끝났습니다. 직접 모션을 만들 거면 3번부터 하세요.";
                ValidateState();
                Repaint();
            }

            if (!canComplete && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DrawMiniHelp("1번 완료 조건: ID 적용, 의상 적용, " + ContentsRoot + " 아래 의상 prefab.", MessageType.None);
            }
        }

        private static string[] BuildOutfitPopupOptions(List<GameObject> prefabs)
        {
            string[] options = new string[prefabs.Count + 1];
            options[0] = "선택 안 함";
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                string path = AssetDatabase.GetAssetPath(prefab);
                options[i + 1] = string.IsNullOrEmpty(path) ? prefab.name : path.Substring(ContentsRoot.Length).TrimStart('/');
            }

            return options;
        }

        private static int GetOutfitPopupIndex(List<GameObject> prefabs, GameObject selectedPrefab)
        {
            if (selectedPrefab == null)
            {
                return 0;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == selectedPrefab)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private List<GameObject> FindAllOutfitPrefabs()
        {
            List<GameObject> prefabs = new List<GameObject>();

            // [QC][Guard:missing_search_folder]
            // AssetDatabase.FindAssets는 존재하지 않는 폴더를 넘기면 콘솔 경고를 찍는다. 갓 만든 프로젝트에서는
            // 그게 repaint마다 터지고, 그러면 헬퍼의 안전 패널이 자기가 낸 소음을 경고로 되보고한다.
            if (!AssetDatabase.IsValidFolder(ContentsRoot))
            {
                return prefabs;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ContentsRoot });
            Array.Sort(guids, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            return prefabs;
        }

        /// <summary>
        /// 두 Play 이유 사다리가 공유하는 앞 세 칸. 여기서 걸리면 뒤 칸은 볼 것도 없다.
        /// </summary>
        private static bool TryGetCommonPlayBlockReason(WorkflowStatus workflow, out string reason)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                reason = workflow.Safety.Message;
                return true;
            }

            if (!workflow.HasLoader)
            {
                reason = "LOADER를 먼저 찾아야 합니다.";
                return true;
            }

            if (!workflow.HasZepetoId)
            {
                reason = "아이디를 먼저 적용해야 합니다.";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        /// <summary>
        /// 카드 6·7의 Play가 왜 꺼져 있는지. 아래 GetSelectedMotionPlayDisabledReason과 앞 세 칸을 공유하고
        /// 네 번째 칸에서만 갈라진다. 갈라지는 지점이 다르니 하나로 합치지 않는다: 이쪽이 묻는 것은 LOADER에
        /// 연결된 "작업 동작"이고, 저쪽이 묻는 것은 목록에서 고른 "미리보기용 동작"이다. 사용자가 가야 할
        /// 곳이 서로 다르므로 문장도 달라야 한다.
        /// </summary>
        private static string GetPlayDisabledReason(WorkflowStatus workflow, bool requireAnimation)
        {
            string commonReason;
            if (TryGetCommonPlayBlockReason(workflow, out commonReason))
            {
                return commonReason;
            }

            if (requireAnimation && !workflow.HasAssignedAnimation)
            {
                return "2번에서 동작을 고르고 '2번 적용 / 이 동작 쓰기'를 눌러야 합니다.";
            }

            // 절대로 빈 문자열을 돌려주지 않는다. DrawStagePlayStopButtons는 이유가 비어 있지 않을 때만 줄을
            // 찍으므로, ""를 돌려주던 시절에는 회색 Play 버튼만 있고 화면에는 이유가 아무 데도 없었다.
            return "Unity가 컴파일/갱신 중이면 끝난 뒤 다시 눌러보세요.";
        }


        private void DrawStagePlayStopButtons(string playLabel, bool canPlay, string stopLabel, string disabledReason = "", int previewStage = PreviewStageNone)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton(playLabel, canPlay && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                RequestPlayMode(previewStage);
            }

            // ■ 는 이 창의 Stop 표시다. 호출부는 접두사 없는 라벨만 넘긴다.
            string visibleStopLabel = "■ " + stopLabel;

            // Stop은 Play가 돌고 있으면 언제나 활성이고, 어느 stage가 시작했는지는 따지지 않는다.
            //
            // 예전에는 activePreviewStage == previewStage를 요구했다. 그래서 다른 경로로 시작한 Play -
            // Unity 자체 툴바 버튼이나, 당시 아무 stage도 주장하지 않던 라이브 확인 패널 - 이 돌고 있으면
            // Game view는 멀쩡히 돌아가는데 이 창의 Stop이 전부 회색이었다. 편집 모드로 돌아가는 길을
            // 빼앗는 것이 정답인 상황은 없고, 초보자에게는 더더욱 없다. (라이브 확인은 이제
            // PreviewStageMotion을 주장하지만, 이 버튼은 어느 쪽이든 상관하지 않는다 - 여기서
            // activePreviewStage를 읽는 코드는 하나도 없다.)
            //
            // 동작하는 동안은 언제나 빨갛다. 자기 stage가 아니라고 Stop을 회색으로 칠했더니 멀쩡히 눌리는
            // 버튼이 죽은 것처럼 보였고, 실제로 그렇게 신고가 들어왔다("stop이 계속 검은색이여"). stage를
            // 구분해 보여주고 싶다면 그것은 라벨의 일이지, Play에서 빠져나오는 유일한 컨트롤에 "비활성"으로
            // 읽히는 색을 칠할 일이 아니다.
            if (DrawColoredActionButton(visibleStopLabel, isPlaying, StopRed, GUILayout.Height(34f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            if (!canPlay && !isPlaying && !string.IsNullOrEmpty(disabledReason))
            {
                DrawMiniHelp("Play 비활성화: " + disabledReason, MessageType.None);
            }

            GUILayout.Label(
                isPlaying
                    ? "빨간 Stop을 누르면 Play 확인을 끝내고 편집/저장을 다시 할 수 있습니다."
                    : "Play로 확인한 뒤에는 빨간 Stop을 눌러 편집 상태로 돌아오세요.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSelectedMotionPlayStopButtons(WorkflowStatus workflow)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            bool canPlaySelected = CanPlaySelectedMotion(workflow);
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton("미리보기 Play", canPlaySelected && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                PlaySelectedMotionPreview();
            }

            string stopLabel = isTemporarySelectedMotionPreview ? "미리보기 Stop" : "Stop";
            string visibleStopLabel = "■ " + stopLabel;
            // DrawStagePlayStopButtons와 같은 규칙: Play가 돌고 있으면 누가 시작했든 Stop은 동작하고,
            // 비활성으로 읽히지 않도록 계속 빨갛다.
            if (DrawColoredActionButton(visibleStopLabel, isPlaying, StopRed, GUILayout.Height(34f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            if (!canPlaySelected && !isPlaying)
            {
                DrawMiniHelp("Play 비활성화: " + GetSelectedMotionPlayDisabledReason(workflow), MessageType.None);
            }

            GUILayout.Label(
                isPlaying
                    ? "빨간 미리보기 Stop을 누르면 동작 확인을 끝내고 이전 작업 동작으로 돌아갑니다."
                    : "미리보기 Play로 확인한 뒤 빨간 Stop을 누르고, 마음에 들면 작업 동작으로 사용하세요.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static bool CanPlaySelectedMotion(WorkflowStatus workflow)
        {
            return workflow.HasAvatarPlayInputs
                && workflow.HasSelectedPackageAnimation
                && CanEnterPlayMode(workflow.Safety);
        }

        private static string GetSelectedMotionPlayDisabledReason(WorkflowStatus workflow)
        {
            string commonReason;
            if (TryGetCommonPlayBlockReason(workflow, out commonReason))
            {
                return commonReason;
            }

            if (!workflow.HasSelectedPackageAnimation)
            {
                return "검색하거나 v 버튼으로 재생할 동작을 먼저 선택하세요.";
            }

            return "Unity가 컴파일/갱신 중이면 끝난 뒤 다시 누르세요.";
        }

        private void DrawMotionChoiceRow(WorkflowStatus workflow)
        {
            if (selectedAnimationIndex < 0 && packageAnimations.Count > 0)
            {
                SelectPackageAnimation(0);
            }

            if (packageAnimationNames.Length == 0)
            {
                DrawStatusRow("동작 / Motion", "선택 가능한 기본 동작이 없습니다.");
                return;
            }

            int currentIndex = Mathf.Clamp(selectedAnimationIndex, 0, packageAnimationNames.Length - 1);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                currentIndex = EditorGUILayout.Popup("동작 / Motion", currentIndex, packageAnimationNames);
            }
            if (EditorGUI.EndChangeCheck())
            {
                SelectPackageAnimation(currentIndex);
            }

            DrawStatusRow("연결된 동작", workflow.AssignedAnimation == null ? "없음" : workflow.AssignedAnimation.name);

            DrawMiniHelp("동작을 먼저 고른 뒤 미리보기 Play로 확인하세요. 마음에 들면 아래에서 작업 동작으로 확정합니다.", MessageType.None);
        }

        /// <summary>
        /// 라벨 하나 뒤에 서로 다른 동작 둘이 숨어 있다. 아직 연결되지 않은 동작이면 실제로 연결하고
        /// (UseSelectedAnimation), 이미 연결된 동작이면 연결은 그대로 둔 채 2번 완료 표시만 세운다.
        /// 사용자 눈에는 같은 버튼을 두 번 누르는 흐름으로 보인다: 한 번은 적용, 한 번은 확정.
        /// </summary>
        /// <remarks>
        /// 활성 조건을 DisabledScope와 버튼 인자에 두 번 넘기는 것은 중복이 아니다. DisabledScope는 안쪽
        /// GUI를 잠그고, 버튼에 넘긴 bool은 DrawColoredActionButton이 색을 입힐지 정하는 데 쓴다(Ui.cs).
        /// 둘이 어긋나면 잠긴 버튼이 활성 색으로 보이거나 그 반대가 되므로 반드시 같은 값을 쓴다.
        /// </remarks>
        private void DrawUseSelectedMotionButton(WorkflowStatus workflow)
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            bool selectedAlreadyAssigned = selected != null
                && workflow.AssignedAnimation != null
                && IsClipDerivedFromPackage(workflow.AssignedAnimation, selected);
            bool canUseSelected = selected != null
                && animationClipProperty != null
                && !EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUI.DisabledScope(!canUseSelected))
            {
                if (DrawColoredActionButton(selectedAlreadyAssigned && motionSelectStageComplete ? "2번 완료됨" : "2번 적용 / 이 동작 쓰기", canUseSelected, ActionBlue, GUILayout.Height(34f)))
                {
                    if (selectedAlreadyAssigned)
                    {
                        SetMotionSelectStageComplete(true);
                        statusMessage = "2번 동작을 정했습니다. 이제 6번 클립 조정으로 넘어가거나, 3~5번에서 직접 만드세요.";
                        Repaint();
                    }
                    else
                    {
                        UseSelectedAnimation();
                    }
                }
            }
        }

        private void DrawClipAdjustBody(WorkflowStatus workflow)
        {
            AnimationClip assignedClip = GetAssignedAnimationClip();
            EnsureClipAdjustDefaults(assignedClip);

            float clipLength = assignedClip == null ? 0f : Mathf.Max(0.01f, assignedClip.length);
            DrawStatusRow("대상 clip / Target", assignedClip == null ? "없음" : assignedClip.name + "  " + FormatClipLength(assignedClip));

            // 연결된 clip과 실제로 재생되는 clip은 다를 수 있다. 재생을 정하는 것은 AnimatorOverrideController의
            // 슬롯이지 LOADER의 clip 필드가 아니라서, 둘을 나란히 보여주지 않으면 "분명 적용했는데 안 움직인다"의
            // 원인이 화면 어디에도 없다.
            AnimationClip playbackClip = GetPlaybackClip();
            DrawStatusRow(
                "실제 재생될 동작",
                playbackClip == null ? "없음" : playbackClip.name + "  " + FormatClipLength(playbackClip));
            if (playbackClip != null && playbackClip.length <= StaticPoseMaxLength)
            {
                DrawMiniHelp(
                    "재생 슬롯이 정지 포즈(" + playbackClip.name + ")입니다. 이대로 Play하면 아바타가 움직이지 않습니다. "
                    + "2번에서 동작을 다시 적용하세요.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(assignedClip == null))
            {
                EditorGUI.BeginChangeCheck();
                motionPreviewSpeed = EditorGUILayout.Slider("재생 속도 / Speed", Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f), 0.25f, 2f);
                // 세 슬라이더는 사슬로 묶여 있다. Start는 최소 한 프레임 분량을 남겨야 하고(maxStart), End는
                // 그 Start보다 최소 한 프레임 뒤여야 한다(minEnd). 그런데 Clamp만으로는 부족하다: 사용자가
                // Start를 오른쪽으로 끌면 이 프레임의 End는 이미 그려진 옛 값이라 뒤집힌 채로 통과한다.
                // 아래 사후 보정이 그 한 프레임을 메운다.
                float maxStart = Mathf.Max(0f, clipLength - 0.01f);
                clipTrimStart = EditorGUILayout.Slider("시작 시간 / Start", Mathf.Clamp(clipTrimStart, 0f, maxStart), 0f, maxStart);
                float minEnd = Mathf.Min(clipTrimStart + 0.01f, clipLength);
                clipTrimEnd = EditorGUILayout.Slider("끝 시간 / End", Mathf.Clamp(clipTrimEnd, minEnd, clipLength), 0f, clipLength);
                clipLoop = EditorGUILayout.Toggle("저장된 clip 반복 재생 / Loop Saved Clip", clipLoop);
                if (clipTrimEnd <= clipTrimStart)
                {
                    clipTrimEnd = Mathf.Min(clipLength, clipTrimStart + 0.01f);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // [QA][State:dirty_stage]
                    // 배속/구간/반복을 건드리면 이미 완료한 클립 단계는 무효가 된다.
                    // 저장된 .anim이 화면과 다시 일치하려면 아래 파란 '6번 적용' 버튼을 다시 눌러야 한다.
                    SetClipStageComplete(false);
                    SaveClipAdjustSessionState(GetClipAdjustStatePath(assignedClip));
                }
            }

            DrawMiniHelp("저장 결과: 2.0x는 길이가 절반, 0.5x는 길이가 두 배가 됩니다. 원본 package와 기존 복사본은 직접 수정하지 않습니다. 반복 재생은 저장될 clip의 Loop 설정입니다.", MessageType.None);

            DrawStagePlayStopButtons("Play로 배속 확인", workflow.CanPlayMotion, "Stop", GetPlayDisabledReason(workflow, true), PreviewStageClipAdjust);

            // 조정할 것이 없으면 저장 없이 단계만 완료로 넘긴다. 바꾼 것이 없는데 파일을 다시 쓰면 clip이
            // 매번 새 asset처럼 보이고, 라벨도 '저장'이라고 말해 놓고 아무 변화가 없어 거짓말이 된다.
            bool hasClipAdjustInput = HasClipAdjustInput(assignedClip);
            string applyLabel = hasClipAdjustInput ? "6번 적용 / 저장" : "6번 적용";
            bool canApply = workflow.CanClipEdit && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (DrawColoredActionButton(applyLabel, canApply, ActionBlue, GUILayout.Height(34f)))
            {
                if (hasClipAdjustInput)
                {
                    SaveClipAdjustToCurrentClip();
                }
                else
                {
                    SetClipStageComplete(true);
                    statusMessage = "클립 조정을 완료했습니다. 이제 7번 내보내기로 넘어가세요.";
                    Repaint();
                }
            }

            DrawBlinkControls(assignedClip);

            showClipAdvancedOptions = EditorGUILayout.Foldout(showClipAdvancedOptions, "고급 / Advanced", true);
            if (showClipAdvancedOptions)
            {
                DrawStatusRow("저장 위치 / Save Folder", ClipEditRoot);
                if (lastClipEditedClip != null)
                {
                    DrawStatusRow("마지막 clip edit", lastClipEditedClip.name);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (DrawSecondaryButton("열기", GUILayout.Width(64f)))
                    {
                        SelectAndPing(lastClipEditedClip);
                        EditorApplication.ExecuteMenuItem("Window/Animation/Animation");
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

        }

        private void DrawSaveExportBody(WorkflowStatus workflow)
        {
            string exportPackagePath = GetExpectedZepetoPackagePath();
            DrawStatusRow("Export 대상 동작", workflow.AssignedAnimation == null ? "없음" : workflow.AssignedAnimation.name);
            DrawStatusRow("출력 파일", GetExportPackageStatusText(exportPackagePath));
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ExportPackageExists(exportPackagePath)))
            {
                if (DrawSecondaryButton("출력 파일 선택", GUILayout.Height(26f)))
                {
                    SelectAndPing(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportPackagePath));
                }
            }

            // 공식 SDK export는 ExecuteMenuItem이 돌아온 뒤에 끝날 수 있다. 그래서 결과를 다시 확인할 수 있게
            // 남겨 둔다 - 그러지 않으면 화면에 "아직 생성 전" 줄만 낡은 채로 남는다.
            using (new EditorGUI.DisabledScope(!workflow.HasOutfit || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("결과 다시 확인", GUILayout.Height(26f)))
                {
                    RecheckExportResult();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawStagePlayStopButtons("Play로 저장 결과 확인", workflow.CanPlayMotion, "Stop", GetPlayDisabledReason(workflow, true), PreviewStageExport);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !workflow.HasOutfit))
            {
                if (DrawColoredActionButton(".zepeto 만들기", workflow.HasOutfit && !EditorApplication.isPlayingOrWillChangePlaymode, ActionBlue, GUILayout.Height(34f)))
                {
                    OpenExportMenu();
                }
            }

            DrawMiniHelp("Export는 Unity Play가 꺼진 상태에서만 실행합니다. 실제 업로드와 로그인은 공식 ZEPETO Studio 웹 흐름에서 진행합니다.", MessageType.None);
        }

        /// <summary>
        /// 준비 도구 모음. 여기 버튼 상당수는 카드 안에도 같은 것이 있다.
        /// </summary>
        /// <remarks>
        /// 흐름의 주인은 언제나 카드 쪽이다. 카드는 순서와 완료 표시를 함께 다루지만, 이 foldout의 버튼은
        /// 같은 함수를 부르되 단계 완료를 세우지 않는다. 여기는 "1번부터 순서대로"가 아니라 이미 망가진
        /// 상태를 손보러 오는 곳이다 - scene을 잃었거나, LOADER 참조가 끊겼거나, 컨트롤러가 package
        /// 원본을 가리키는 경우. 그러니 새 사용자에게 안내할 때는 항상 카드 쪽을 가리킨다.
        /// </remarks>
        private void DrawSetupFoldout(WorkflowStatus workflow)
        {
            showDetailedWorkflow = EditorGUILayout.Foldout(showDetailedWorkflow, "작업 준비 / Setup", true);
            if (!showDetailedWorkflow)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            DrawStatusRow("아바타 / Avatar", string.IsNullOrEmpty(workflow.CurrentZepetoId) ? "ID 없음" : workflow.CurrentZepetoId);
            DrawStatusRow("의상 / Outfit", workflow.HasOutfit ? clothingPrefab.name : "없음");
            DrawStatusRow("동작 / Motion", workflow.HasAssignedAnimation ? workflow.AssignedAnimation.name : "없음");

            if (workSceneOptions.Length > 0)
            {
                selectedWorkSceneIndex = EditorGUILayout.Popup(
                    "작업 scene",
                    Mathf.Clamp(selectedWorkSceneIndex, 0, workSceneOptions.Length - 1),
                    workSceneOptions);
            }
            else
            {
                DrawMiniHelp(
                    "LOADER가 들어 있는 scene이 프로젝트에 없습니다. ZEPETO Studio에서 받은 의상 템플릿 프로젝트의 scene과 "
                    + "의상 prefab을 Assets 아래에 넣어야 1~7단계를 진행할 수 있습니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(workSceneGuids.Length == 0 || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("씬 열기", GUILayout.Height(28f)))
                {
                    OpenSelectedWorkScene();
                }
            }

            if (DrawSecondaryButton("씬 다시 찾기", GUILayout.Height(28f)))
            {
                RefreshWorkSceneCandidates();
                statusMessage = workSceneGuids.Length == 0
                    ? "LOADER가 들어 있는 scene을 찾지 못했습니다."
                    : "LOADER scene " + workSceneGuids.Length + "개를 찾았습니다.";
            }

            if (DrawSecondaryButton("LOADER", GUILayout.Height(28f)))
            {
                // 캐시된 참조를 버리고 검색 쿨다운까지 밀어내는 강제 재검색이다. lastLoaderSearchTime을
                // 되돌리지 않으면 LoaderSearchIntervalSeconds 안에는 방금 null로 만든 것을 다시 못 찾는다.
                loader = null;
                lastLoaderSearchTime = -1000d;
                FindLoaderAndSerializedFields();
                ValidateState();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("의상 찾기", GUILayout.Height(28f)))
            {
                FindDefaultClothingPrefab();
                ValidateState();
            }

            using (new EditorGUI.DisabledScope(animatorControllerProperty == null || EditorApplication.isPlayingOrWillChangePlaymode || workflow.HasLocalAnimatorController))
            {
                if (DrawSecondaryButton("Controller Fix", GUILayout.Height(28f)))
                {
                    string controllerMessage;
                    statusMessage = EnsureLocalAnimatorController(out controllerMessage)
                        ? controllerMessage
                        : "Local controller fix failed: " + controllerMessage;
                    ValidateState();
                }
            }

            using (new EditorGUI.DisabledScope(!workflow.HasOutfit || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Export", GUILayout.Height(28f)))
                {
                    OpenExportMenu();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiagnosticsFoldout(WorkflowStatus workflow)
        {
            showDiagnosticsAdvanced = EditorGUILayout.Foldout(showDiagnosticsAdvanced, "문제 해결 / Diagnostics", true);
            if (!showDiagnosticsAdvanced)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("복구", GUILayout.Height(28f)))
            {
                RecoverSafetyState();
            }

            if (DrawSecondaryButton("Console 정리", GUILayout.Height(28f)))
            {
                ClearConsoleAndSessionSummary();
            }

            if (DrawSecondaryButton("검증", GUILayout.Height(28f)))
            {
                ValidateState();
                statusMessage = "Validation complete. / 검증 완료";
            }
            EditorGUILayout.EndHorizontal();

            // 여기 찍히는 줄이 무언가 깨졌을 때 유지보수자가 제일 먼저 읽는 것이다. 특히 버전 줄은
            // 설치된 zepeto.studio를 실제로 감지해서 찍으므로, 사용자가 말하는 버전과 대조할 수 있다.
            string zepetoStudioVersion;
            string zepetoStudioSource;
            bool hasZepetoStudio = TryGetZepetoStudioPackage(out zepetoStudioVersion, out zepetoStudioSource);
            DrawStatusRow(
                RequiredPackage,
                hasZepetoStudio ? zepetoStudioVersion + " (" + zepetoStudioSource + ")" : "설치되지 않음");
            DrawStatusRow("Console", string.Format("Warnings {0} / Errors {1}", sessionWarningCount, sessionErrorCount));
            DrawStatusRow("Controller", string.IsNullOrEmpty(workflow.AnimatorControllerPath) ? "없음" : workflow.AnimatorControllerPath);

            // 다섯 줄까지만 찍는다. 폭주 상황에서는 검증 메시지도 함께 쏟아지는데, 그걸 다 찍으면 이 foldout이
            // 창을 삼켜서 정작 위의 복구 버튼이 화면 밖으로 밀려난다.
            for (int i = 0; i < Mathf.Min(5, validationMessages.Count); i++)
            {
                DrawMiniHelp(validationMessages[i].Text, validationMessages[i].Type);
            }
            EditorGUILayout.EndVertical();
        }
    }
}
