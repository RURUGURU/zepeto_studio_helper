using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The 1-4 stage state machine and its progress display.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
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
            public bool HasZepetoIdField;
            public bool HasZepetoId;
            public bool HasOutfit;
            public bool OutfitIsUnderContents;
            public bool HasSelectedPackageAnimation;
            public bool HasCopiedAnimation;
            public bool HasAssignedAnimation;
            public bool HasEditableAssignedAnimation;
            public bool HasLocalAnimatorController;
            public bool HasPreviewInputs;
            public bool HasAvatarPlayInputs;
            public bool HasMotionPlayInputs;
            public bool CanPlay;
            public bool CanPlayAvatarOutfit;
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
                return 1;
            }

            if (!workflow.HasEditableAssignedAnimation || !motionSelectStageComplete)
            {
                return 2;
            }

            if (!clipStageComplete)
            {
                return 3;
            }

            return 4;
        }

        private string GetCurrentStageText(WorkflowStatus workflow)
        {
            switch (GetActiveStageNumber(workflow))
            {
                case 1:
                    return "1. 아바타+의상 준비";
                case 2:
                    return "2. 동작 선택";
                case 3:
                    return "3. 클립 조정";
                default:
                    return "4. 저장/Export";
            }
        }

        private bool IsStageComplete(WorkflowStatus workflow, int stage)
        {
            switch (stage)
            {
                case 1:
                    return workflow.HasAvatarPlayInputs && workflow.HasOutfit && avatarOutfitStageComplete;
                case 2:
                    return workflow.HasEditableAssignedAnimation && motionSelectStageComplete;
                case 3:
                    return workflow.CanClipEdit && clipStageComplete;
                case 4:
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
            workflow.HasZepetoIdField = zepetoIdProperty != null;
            workflow.HasZepetoId = !string.IsNullOrEmpty(workflow.CurrentZepetoId);
            workflow.HasOutfit = clothingPrefab != null;
            workflow.OutfitPath = clothingPrefab == null ? string.Empty : AssetDatabase.GetAssetPath(clothingPrefab);
            workflow.OutfitIsUnderContents = !string.IsNullOrEmpty(workflow.OutfitPath)
                && workflow.OutfitPath.StartsWith(ContentsRoot + "/", StringComparison.OrdinalIgnoreCase);
            workflow.HasSelectedPackageAnimation = GetSelectedPackageAnimation() != null;
            workflow.HasCopiedAnimation = copiedAnimationClip != null;
            workflow.AssignedAnimation = GetAssignedAnimationClip();
            workflow.HasAssignedAnimation = workflow.AssignedAnimation != null;
            workflow.AssignedAnimationPath = workflow.AssignedAnimation == null ? string.Empty : AssetDatabase.GetAssetPath(workflow.AssignedAnimation);
            workflow.HasEditableAssignedAnimation = workflow.HasAssignedAnimation
                && workflow.AssignedAnimationPath.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase);
            workflow.AnimatorControllerPath = GetAnimatorControllerPath();
            workflow.HasLocalAnimatorController = !string.IsNullOrEmpty(workflow.AnimatorControllerPath)
                && !IsPackageOrPackageCachePath(workflow.AnimatorControllerPath);
            workflow.HasPreviewInputs = workflow.HasLoader && workflow.HasOutfit && workflow.HasAssignedAnimation && workflow.HasLocalAnimatorController;
            workflow.HasAvatarPlayInputs = workflow.HasLoader && workflow.HasZepetoId;
            workflow.HasMotionPlayInputs = workflow.HasAvatarPlayInputs && workflow.HasAssignedAnimation;
            workflow.CanPlay = workflow.HasPreviewInputs && CanEnterPlayMode(snapshot);
            workflow.CanPlayAvatarOutfit = workflow.HasAvatarPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanPlayMotion = workflow.HasMotionPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanClipEdit = workflow.HasEditableAssignedAnimation && !snapshot.HasBlockingRisk;
            return workflow;
        }
    }
}
