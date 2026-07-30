using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The internal 4-stage state machine that drives the seven step cards, and its progress display.
    /// The stage numbers are NOT card numbers - see the PreviewStage constants below for the mapping.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // ------------------------------------------------------------ preview stage identity
        //
        // The window draws SEVEN numbered cards (Flow.cs). This state machine still has only FOUR stages: it
        // predates the card layout and its numbers do not line up with the card numbers. The mapping is:
        //
        //     PreviewStageAvatarOutfit = 1  ->  card 1  "아바타 준비"
        //     PreviewStageMotion       = 2  ->  card 2  "동작 고르기"  AND  card 5  "내 캐릭터로 확인"
        //     PreviewStageClipAdjust   = 3  ->  card 6  "클립 조정"
        //     PreviewStageExport       = 4  ->  card 7  "제페토로 내보내기"
        //
        // Cards 3 and 4 (the Blender round trip) own no stage of their own. What they produce comes back through
        // card 2's motion list, so the whole detour lives inside stage 2.
        //
        // Stage 2 is deliberately shared by two cards: card 2's preview enters Play through
        // RequestPlayMode(2) (Motion.cs) and card 5's live preview sets the same stage in RequestLivePreviewPlay
        // (Safety.cs). Both mean "a motion is being tried on the avatar", and the only thing the stage decides is
        // which Play session is active - so one stage for both is correct, not a leftover.
        //
        // The VALUES are load-bearing and must never be renumbered to match the card numbers. activePreviewStage
        // is persisted in SessionState (ActivePreviewStageSessionKey), so a live editor session can already hold
        // one of these ints, and Motion.cs passes its stage as a literal 2. Renumbering silently kills the
        // clip-adjust preview, whose Play path is gated on stage 3 alone (Safety.cs, RequestPlayMode).
        private const int PreviewStageAvatarOutfit = 1;
        private const int PreviewStageMotion = 2;
        private const int PreviewStageClipAdjust = 3;
        private const int PreviewStageExport = 4;

        private enum StepState
        {
            Ready,
            InProgress,
            Needed,
            Waiting,
            Blocked
        }

        private struct WorkflowStatus
        {
            public SafetySnapshot Safety;
            public string CurrentZepetoId;
            public bool HasLoader;
            public bool HasZepetoId;
            public bool HasOutfit;
            public bool OutfitIsUnderContents;
            public bool HasSelectedPackageAnimation;
            public bool HasAssignedAnimation;
            public bool HasEditableAssignedAnimation;
            public bool HasLocalAnimatorController;
            public bool HasAvatarPlayInputs;
            public bool HasMotionPlayInputs;
            public bool CanPlayMotion;
            public bool CanClipEdit;
            public string OutfitPath;
            public string AssignedAnimationPath;
            public string AnimatorControllerPath;
            public AnimationClip AssignedAnimation;
        }

        private void DrawV7WorkbenchHeader(WorkflowStatus workflow)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("v7 ZEPETO 작업대", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isPlaying ? "Play 중" : "Stop 상태", EditorStyles.miniBoldLabel);

            // The one Stop that cannot be hidden. Every other Stop lives inside a step card, and a step card
            // can collapse - on a stage lock, on completion, or because some state was lost in a domain
            // reload. When that happens while Play is running the user is left watching a running Game view
            // with no way out inside this window. This button sits above everything that can collapse.
            //
            // Drawn UNCONDITIONALLY, enabled by isPlaying. Wrapping it in `if (isPlaying && ...)` changes the
            // control count between the Layout and Repaint passes whenever Play starts or stops mid-frame,
            // which corrupts the GUILayout group and makes controls flicker out - the button appears, then
            // vanishes. Presence must not depend on volatile state; only `enabled` may.
            if (DrawColoredActionButton("■ Stop", isPlaying, StopRed,
                    GUILayout.Width(90f), GUILayout.Height(20f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            // Same rule: always drawn, only the text changes.
            DrawMiniHelp(
                isPlaying && LiveReloadArmed
                    ? "라이브 확인 중 · 적용된 횟수 " + LiveReloadCount
                        + " — Blender에서 'Unity로 보내기'를 누른 뒤 Unity 창을 다시 클릭하세요."
                    : "1번부터 아래로 진행하세요. 직접 모션을 만들 거면 3 → 4 → 5번이 핵심입니다.",
                isPlaying && LiveReloadArmed ? MessageType.Info : MessageType.None);

            DrawRecompileDuringPlayGuard();
            DrawWorkflowStatusLine(workflow);

            if (workflow.Safety.HasBlockingRisk)
            {
                DrawMiniHelp("막힌 이유: " + workflow.Safety.Message, MessageType.Error);
            }
            else if (workflow.Safety.HasWarning)
            {
                DrawMiniHelp("경고: " + workflow.Safety.Message, MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWorkflowStatusLine(WorkflowStatus workflow)
        {
            DrawStatusRow("현재 작업", GetCurrentStageText(workflow));
            DrawStatusRow("다음 행동", GetWorkflowHint(workflow));
        }

        private string GetWorkflowHint(WorkflowStatus workflow)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                return "복구 / Recover";
            }

            if (!workflow.HasAvatarPlayInputs)
            {
                return "1번에서 아이디와 의상을 확인한 뒤 Play";
            }

            if (!workflow.HasOutfit || !workflow.OutfitIsUnderContents)
            {
                return "1번의 '의상 선택' 목록에서 고른 뒤 파란 '의상 적용' 버튼";
            }

            if (!avatarOutfitStageComplete)
            {
                return "1번 맨 아래에서 확인 후 '1번 적용'";
            }

            if (!workflow.HasEditableAssignedAnimation || !motionSelectStageComplete)
            {
                return "2번에서 동작을 고르고 '2번 적용 / 이 동작 쓰기' (직접 만들 거면 3번부터)";
            }

            if (!clipStageComplete)
            {
                return HasClipAdjustInput(workflow.AssignedAnimation)
                    ? "6번에서 '6번 적용 / 저장'을 눌러 클립 조정을 저장하세요"
                    : "6번에서 배속/길이를 확인하고 '6번 적용'을 누르세요";
            }

            return "7번에서 'Play로 저장 결과 확인' 후 '.zepeto 만들기'";
        }

        private int GetActiveStageNumber(WorkflowStatus workflow)
        {
            if (!workflow.HasAvatarPlayInputs || !workflow.HasOutfit || !avatarOutfitStageComplete)
            {
                return PreviewStageAvatarOutfit;
            }

            if (!workflow.HasEditableAssignedAnimation || !motionSelectStageComplete)
            {
                return PreviewStageMotion;
            }

            if (!clipStageComplete)
            {
                return PreviewStageClipAdjust;
            }

            return PreviewStageExport;
        }

        /// <summary>
        /// The header's "현재 작업" row. Every number printed here must be a number the user can find on a card
        /// below, so each line quotes its card's title verbatim (Flow.cs).
        ///
        /// Four stages cover seven cards. Stage 2 spans card 2 and cards 3-5, and the live-preview card is the
        /// only one of those with its own detectable state - the Blender watcher being armed - so it gets its own
        /// line. Cards 3 and 4 have no state to report while the user is away in Blender; the "다음 행동" row
        /// below points at them instead.
        /// </summary>
        private string GetCurrentStageText(WorkflowStatus workflow)
        {
            switch (GetActiveStageNumber(workflow))
            {
                case PreviewStageAvatarOutfit:
                    return "1. 아바타 준비";
                case PreviewStageMotion:
                    // Label-only branch. Card 5's own Done check (Flow.cs) uses the same two values, and only the
                    // TEXT varies here - no control's existence depends on it, per the unconditional-draw rule.
                    return EditorApplication.isPlaying && LiveReloadArmed
                        ? "5. 내 캐릭터로 확인"
                        : "2. 동작 고르기";
                case PreviewStageClipAdjust:
                    return "6. 클립 조정";
                default:
                    return "7. 제페토로 내보내기";
            }
        }

        private bool IsStageComplete(WorkflowStatus workflow, int stage)
        {
            switch (stage)
            {
                case PreviewStageAvatarOutfit:
                    return workflow.HasAvatarPlayInputs && workflow.HasOutfit && avatarOutfitStageComplete;
                case PreviewStageMotion:
                    return workflow.HasEditableAssignedAnimation && motionSelectStageComplete;
                case PreviewStageClipAdjust:
                    return workflow.CanClipEdit && clipStageComplete;
                case PreviewStageExport:
                    return workflow.HasEditableAssignedAnimation && workflow.HasOutfit && avatarOutfitStageComplete && motionSelectStageComplete && clipStageComplete;
                default:
                    return false;
            }
        }

        private bool IsStageWaiting(WorkflowStatus workflow, int stage)
        {
            return stage > GetActiveStageNumber(workflow);
        }

        private StepState GetSequentialStageState(WorkflowStatus workflow, int stage)
        {
            if (workflow.Safety.HasBlockingRisk && stage == GetActiveStageNumber(workflow))
            {
                return StepState.Blocked;
            }

            // [QC][Invariant:sequential_unlock]
            // Later stages can have stale completed assets from a previous run, but they must stay locked
            // while an earlier required step is incomplete. Check waiting before completed state.
            if (IsStageWaiting(workflow, stage))
            {
                return StepState.Waiting;
            }

            if (IsStageComplete(workflow, stage))
            {
                return StepState.Ready;
            }

            return StepState.InProgress;
        }

        private void LoadWorkflowStageProgress()
        {
            avatarOutfitStageComplete = SessionState.GetBool(AvatarOutfitStageCompleteSessionKey, false);
            motionSelectStageComplete = SessionState.GetBool(MotionSelectStageCompleteSessionKey, false);
            clipStageComplete = SessionState.GetBool(ClipStageCompleteSessionKey, false);
            activePreviewStage = SessionState.GetInt(ActivePreviewStageSessionKey, -1);
        }

        private void SaveWorkflowStageProgress()
        {
            SessionState.SetBool(AvatarOutfitStageCompleteSessionKey, avatarOutfitStageComplete);
            SessionState.SetBool(MotionSelectStageCompleteSessionKey, motionSelectStageComplete);
            SessionState.SetBool(ClipStageCompleteSessionKey, clipStageComplete);
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
        }

        private void SetAvatarOutfitStageComplete(bool isComplete)
        {
            avatarOutfitStageComplete = isComplete;
            SessionState.SetBool(AvatarOutfitStageCompleteSessionKey, isComplete);
            if (!isComplete)
            {
                SetMotionSelectStageComplete(false);
                SetClipStageComplete(false);
            }
        }

        private void SetMotionSelectStageComplete(bool isComplete)
        {
            motionSelectStageComplete = isComplete;
            SessionState.SetBool(MotionSelectStageCompleteSessionKey, isComplete);
            if (!isComplete)
            {
                SetClipStageComplete(false);
            }
        }

        private void SetClipStageComplete(bool isComplete)
        {
            clipStageComplete = isComplete;
            SessionState.SetBool(ClipStageCompleteSessionKey, isComplete);
        }

        private WorkflowStatus BuildWorkflowStatus(SafetySnapshot snapshot)
        {
            WorkflowStatus workflow = new WorkflowStatus();
            workflow.Safety = snapshot;
            workflow.CurrentZepetoId = GetCurrentZepetoId();
            workflow.HasLoader = loader != null;
            workflow.HasZepetoId = !string.IsNullOrEmpty(workflow.CurrentZepetoId);
            workflow.HasOutfit = clothingPrefab != null;
            workflow.OutfitPath = clothingPrefab == null ? string.Empty : AssetDatabase.GetAssetPath(clothingPrefab);
            workflow.OutfitIsUnderContents = !string.IsNullOrEmpty(workflow.OutfitPath)
                && workflow.OutfitPath.StartsWith(ContentsRoot + "/", StringComparison.OrdinalIgnoreCase);
            workflow.HasSelectedPackageAnimation = GetSelectedPackageAnimation() != null;
            workflow.AssignedAnimation = GetAssignedAnimationClip();
            workflow.HasAssignedAnimation = workflow.AssignedAnimation != null;
            workflow.AssignedAnimationPath = workflow.AssignedAnimation == null ? string.Empty : AssetDatabase.GetAssetPath(workflow.AssignedAnimation);
            // [QC][Invariant:clip_edit_reachable]
            // AnimationCopyRoot alone is not the answer: step 2's _editable copies land there, but everything the
            // Blender/live pipeline produces lands in CustomMotionRoot, and testing only the copy root made card 6
            // permanently unreachable for exactly the motions this tool exists to author. Both roots are eligible,
            // and the two-root test lives in IsClipEditEligiblePath so this and Validation.cs cannot drift apart.
            workflow.HasEditableAssignedAnimation = workflow.HasAssignedAnimation
                && IsClipEditEligiblePath(workflow.AssignedAnimationPath);
            workflow.AnimatorControllerPath = GetAnimatorControllerPath();
            workflow.HasLocalAnimatorController = !string.IsNullOrEmpty(workflow.AnimatorControllerPath)
                && !IsPackageOrPackageCachePath(workflow.AnimatorControllerPath);
            workflow.HasAvatarPlayInputs = workflow.HasLoader && workflow.HasZepetoId;
            workflow.HasMotionPlayInputs = workflow.HasAvatarPlayInputs && workflow.HasAssignedAnimation;
            workflow.CanPlayMotion = workflow.HasMotionPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanClipEdit = workflow.HasEditableAssignedAnimation && !snapshot.HasBlockingRisk;
            return workflow;
        }
    }
}
