using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The step cards the user actually clicks through.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void DrawWarningCleanupPanel()
        {
            // Always drawn, never conditionally present.
            //
            // This used to `return` before its BeginVertical when there was nothing wrong, so the whole panel -
            // ten controls in two horizontal groups - appeared and disappeared based on sessionErrorCount, a
            // static that Application.logMessageReceived mutates from a background callback, and on a safety
            // snapshot that refreshes itself on a 2-second timer mid-draw. Either could flip between the
            // Layout and Repaint passes of one OnGUI cycle and corrupt the group. The window that flips it is
            // precisely an SDK exception loop - the situation this panel exists to rescue the user from.
            SafetySnapshot snapshot = GetSafetySnapshot(false);
            bool hasPackageController = IsPackageOrPackageCachePath(GetAnimatorControllerPath());
            bool hasProblem = snapshot.HasWarning
                || snapshot.HasBlockingRisk
                || hasPackageController
                || sessionErrorCount > 0;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("경고 복구 / Warning Cleanup", EditorStyles.boldLabel);

            string problemText;
            MessageType problemType;
            if (snapshot.HasBlockingRisk)
            {
                problemText = "Play가 막힌 이유: " + snapshot.Message;
                problemType = MessageType.Error;
            }
            else if (snapshot.HasWarning)
            {
                problemText = "복구 가능한 경고입니다. 복구 후 다시 시도하세요: " + snapshot.Message;
                problemType = MessageType.Warning;
            }
            else if (hasPackageController)
            {
                problemText = "AnimatorController가 package cache를 가리킵니다. local copy로 바꿔야 package cache warning을 피할 수 있습니다.";
                problemType = MessageType.Warning;
            }
            else
            {
                problemText = "이상 없음. 문제가 생기면 여기 이유가 표시되고 아래 버튼으로 복구합니다.";
                problemType = MessageType.None;
            }

            DrawMiniHelp(problemText, problemType);

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
                statusMessage = "package cache warning이 이미 찍혔다면 Unity 종료 후 Library/PackageCache/zepeto.studio@3.2.12를 재생성하면 됩니다. helper는 이후 Assets/ZepetoHelper local asset만 수정합니다.";
            }
            EditorGUILayout.EndHorizontal();

            // Also unconditional: gating this on the snapshot reintroduced the same Layout/Repaint mismatch.
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

        // [AUDIT][Risk:Critical][Scope:play_stability]
        // This is the single most common cause of "the avatar loads but never moves": Unity recompiles scripts
        // while Play is running, the domain reloads, and the ZEPETO context is left with null internals. The
        // symptom is a per-frame NullReferenceException loop from ZepetoContext / SwingBoneProcessor, which the
        // safety snapshot already detects - but detection after the fact is worse than not letting it happen.
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
        /// The id field and its apply button. Extracted from the old step-1 card so the flow layout can place
        /// it directly; the surrounding 1-1 / 1-2 sub-blocks are gone - step 1 is one card now.
        /// </summary>
        private void DrawZepetoIdRow(WorkflowStatus workflow)
        {
            string currentId = string.IsNullOrEmpty(workflow.CurrentZepetoId) ? "ID 없음" : workflow.CurrentZepetoId;
            DrawStatusRow("현재 아이디", currentId);

            // Ids are typed in directly. There used to be a saved-id list with a dropdown and add/delete
            // buttons; it was three extra controls guarding a value that is short, rarely changed, and already
            // visible in the '현재' row below.
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

        private void DrawOutfitChoiceRow(WorkflowStatus workflow)
        {
            List<GameObject> prefabs = FindAllOutfitPrefabs();
            if (prefabs.Count == 0)
            {
                DrawStatusRow("의상 / Outfit", "Assets/Contents 아래 prefab 없음");
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
                DrawMiniHelp("1번 완료 조건: ID 적용, 의상 적용, Assets/Contents 아래 의상 prefab.", MessageType.None);
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
            // AssetDatabase.FindAssets logs a console warning for a folder that does not exist. In a fresh project
            // that fires on every repaint and the helper's own safety panel then reports its own noise as warnings.
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

        private static string GetPlayDisabledReason(WorkflowStatus workflow, bool requireAnimation)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                return workflow.Safety.Message;
            }

            if (!workflow.HasLoader)
            {
                return "LOADER를 먼저 찾아야 합니다.";
            }

            if (!workflow.HasZepetoId)
            {
                return "아이디를 먼저 적용해야 합니다.";
            }

            if (requireAnimation && !workflow.HasAssignedAnimation)
            {
                return "2번에서 동작을 고르고 '2번 적용 / 이 동작 쓰기'를 눌러야 합니다.";
            }

            // Never empty. DrawStagePlayStopButtons only prints a reason when this is non-empty, so returning
            // "" produced a greyed-out Play button with nothing on screen explaining why.
            return "Unity가 컴파일/갱신 중이면 끝난 뒤 다시 눌러보세요.";
        }


        private void DrawStagePlayStopButtons(string playLabel, bool canPlay, string stopLabel, string disabledReason = "", int stageToKeepOpen = -1)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton(playLabel, canPlay && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                RequestPlayMode(stageToKeepOpen);
            }

            string visibleStopLabel = stopLabel.StartsWith("■", StringComparison.Ordinal) ? stopLabel : "■ " + stopLabel;

            // Stop is enabled whenever Play is running, never gated on which stage started it.
            //
            // It used to require activePreviewStage == stageToKeepOpen, so a Play started any other way -
            // Unity's own toolbar button, or the live-preview panel, which back then claimed no stage at all -
            // left EVERY Stop in this window greyed out while the Game view was clearly running. There is no
            // situation where taking away the way back to edit mode is the right answer, least of all for a
            // beginner. (Live preview now claims PreviewStageMotion, but this button no longer cares either way:
            // nothing here reads activePreviewStage.)
            // Always red while it works. Colouring a non-owning stage's Stop grey made an enabled button look
            // dead - which is how it was reported ("stop이 계속 검은색이여"). If a stage distinction is ever
            // wanted it belongs in the label, never in a colour that reads as disabled on the one control that
            // gets the user out of Play.
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

        private void DrawSelectedMotionPlayStopButtons(WorkflowStatus workflow, bool isActiveStage)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            bool isStageAccessible = isActiveStage || !IsStageWaiting(workflow, PreviewStageMotion);
            bool canPlaySelected = isStageAccessible && CanPlaySelectedMotion(workflow);
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton("미리보기 Play", canPlaySelected && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                PlaySelectedMotionPreview();
            }

            string stopLabel = isTemporarySelectedMotionPreview ? "미리보기 Stop" : "Stop";
            string visibleStopLabel = stopLabel.StartsWith("■", StringComparison.Ordinal) ? stopLabel : "■ " + stopLabel;
            // Same rule as DrawStagePlayStopButtons: Play running means Stop works, whoever started it, and
            // it stays red so it never reads as disabled.
            if (DrawColoredActionButton(visibleStopLabel, isPlaying, StopRed, GUILayout.Height(34f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            if (!canPlaySelected && !isPlaying)
            {
                string disabledReason = isStageAccessible
                    ? GetSelectedMotionPlayDisabledReason(workflow)
                    : "먼저 1번 아바타/의상 적용을 완료해야 합니다.";
                DrawMiniHelp("Play 비활성화: " + disabledReason, MessageType.None);
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
            if (workflow.Safety.HasBlockingRisk)
            {
                return workflow.Safety.Message;
            }

            if (!workflow.HasLoader)
            {
                return "LOADER를 먼저 찾아야 합니다.";
            }

            if (!workflow.HasZepetoId)
            {
                return "아이디를 먼저 적용해야 합니다.";
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

        private void DrawUseSelectedMotionButton(WorkflowStatus workflow)
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            bool selectedAlreadyAssigned = selected != null
                && workflow.AssignedAnimation != null
                && IsClipDerivedFromPackage(workflow.AssignedAnimation, selected);
            bool canCompleteAssignedStage = selectedAlreadyAssigned && !EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUI.DisabledScope(selected == null || (selectedAlreadyAssigned && !canCompleteAssignedStage) || animationClipProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawColoredActionButton(selectedAlreadyAssigned && motionSelectStageComplete ? "2번 완료됨" : "2번 적용 / 이 동작 쓰기", selected != null && (!selectedAlreadyAssigned || canCompleteAssignedStage) && animationClipProperty != null && !EditorApplication.isPlayingOrWillChangePlaymode, ActionBlue, GUILayout.Height(34f)))
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
                // Start must leave room for at least one frame of range, otherwise the End clamp below inverts.
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
                    // Any speed/range/loop change invalidates the previously completed clip step.
                    // The user must press the blue '6번 적용' button below again so the saved .anim matches the UI.
                    SetClipStageComplete(false);
                    SaveClipAdjustSessionState(GetClipAdjustStatePath(assignedClip));
                }
            }

            DrawMiniHelp("저장 결과: 2.0x는 길이가 절반, 0.5x는 길이가 두 배가 됩니다. 원본 package와 기존 복사본은 직접 수정하지 않습니다. 반복 재생은 저장될 clip의 Loop 설정입니다.", MessageType.None);

            DrawStagePlayStopButtons("Play로 배속 확인", workflow.CanPlayMotion, "Stop", GetPlayDisabledReason(workflow, true), PreviewStageClipAdjust);

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

            // The official SDK export can finish after ExecuteMenuItem returns, so the result stays re-checkable
            // instead of leaving a stale "아직 생성 전" line on screen.
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

            string zepetoStudioVersion;
            string zepetoStudioSource;
            bool hasZepetoStudio = TryGetZepetoStudioPackage(out zepetoStudioVersion, out zepetoStudioSource);
            DrawStatusRow(
                RequiredPackage,
                hasZepetoStudio ? zepetoStudioVersion + " (" + zepetoStudioSource + ")" : "설치되지 않음");
            DrawStatusRow("Console", string.Format("Warnings {0} / Errors {1}", sessionWarningCount, sessionErrorCount));
            DrawStatusRow("Controller", string.IsNullOrEmpty(workflow.AnimatorControllerPath) ? "없음" : workflow.AnimatorControllerPath);

            if (validationMessages.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(5, validationMessages.Count); i++)
                {
                    DrawMiniHelp(validationMessages[i].Text, validationMessages[i].Type);
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
