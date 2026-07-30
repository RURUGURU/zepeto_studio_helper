using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The window's step layout: seven numbered steps, top to bottom, one job each.
    ///
    /// Replaces the old four-card layout, where the whole Blender round trip lived as lettered sub-boxes
    /// (A/B/C/C-2) buried inside step 2. That made step 2 hold six help boxes and two green Play buttons, put
    /// the FIRST thing you do (export the body) below the LAST thing (import the finished fbx), and - because
    /// a step card collapses when its stage is locked or complete - made the whole Blender toolbox vanish
    /// exactly while it was being used.
    ///
    /// Two rules this layout keeps:
    ///  1. Every step is a NUMBER. If it is a thing the user does, it has a number and a fixed place.
    ///  2. Nothing is hidden behind a stage lock. A step shows its state and says what is missing; it never
    ///     replaces its own contents with "이전 단계를 완료하면 열립니다". Locking is what produced the
    ///     dead ends: buttons that cannot be pressed, with the explanation collapsed out of sight.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private enum FlowState
        {
            Done,
            Now,
            Later,
            Optional
        }

        private void DrawMotionWorkspace()
        {
            SafetySnapshot snapshot = GetSafetySnapshot(false);
            WorkflowStatus workflow = BuildWorkflowStatus(snapshot);

            DrawV7WorkbenchHeader(workflow);
            DrawWarningCleanupPanel();

            DrawStep1Avatar(workflow);
            DrawStep2PickMotion(workflow);
            DrawStep3ExportBody();
            DrawStep4Blender();
            DrawStep5CheckOnMyCharacter();
            DrawStep6AdjustClip(workflow);
            DrawStep7Export(workflow);

            DrawSetupFoldout(workflow);
            DrawDiagnosticsFoldout(workflow);
        }

        // ---------------------------------------------------------------- card chrome

        /// <summary>
        /// One step card. Always draws its body - the caller decides what to put in it, including a line about
        /// what is missing. The state only colours the header and the badge.
        /// </summary>
        private void BeginFlowStep(int number, string title, FlowState state, string oneLiner)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Color bar = FlowStateColor(state);
            Rect barRect = GUILayoutUtility.GetRect(0f, 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(barRect, bar);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(number + ". " + title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawColoredBadge(FlowStateLabel(state), bar, 62f);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(oneLiner))
            {
                GUILayout.Label(oneLiner, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void EndFlowStep()
        {
            EditorGUILayout.EndVertical();
        }

        private static Color FlowStateColor(FlowState state)
        {
            switch (state)
            {
                case FlowState.Done: return ReadyGreen;
                case FlowState.Now: return ActionBlue;
                case FlowState.Optional: return WaitingGray;
                default: return NeededAmber;
            }
        }

        private static string FlowStateLabel(FlowState state)
        {
            switch (state)
            {
                case FlowState.Done: return "완료";
                case FlowState.Now: return "지금";
                case FlowState.Optional: return "선택";
                default: return "아직";
            }
        }

        /// <summary>
        /// The "what is missing" line. Never a lock - just the sentence that tells the user what to go do.
        /// </summary>
        private static void DrawMissing(string what)
        {
            if (!string.IsNullOrEmpty(what))
            {
                DrawMiniHelp(what, MessageType.None);
            }
        }

        // ---------------------------------------------------------------- 1

        private void DrawStep1Avatar(WorkflowStatus workflow)
        {
            bool ready = workflow.HasAvatarPlayInputs && workflow.HasOutfit;
            BeginFlowStep(1, "아바타 준비", ready ? FlowState.Done : FlowState.Now,
                "내 ZEPETO 아이디와, 입혀볼 의상을 고릅니다.");

            DrawZepetoIdRow(workflow);
            DrawOutfitChoiceRow(workflow);
            DrawAvatarOutfitApplyButton(workflow);
            DrawPreviewBodySection();

            if (!ready)
            {
                DrawMissing(!workflow.HasZepetoId
                    ? "아이디를 입력하고 'ID 적용'을 누르세요."
                    : "의상 목록에서 prefab을 고르고 '의상 적용'을 누르세요.");
            }

            EndFlowStep();
        }

        // ---------------------------------------------------------------- 2

        private void DrawStep2PickMotion(WorkflowStatus workflow)
        {
            bool hasMotion = workflow.HasEditableAssignedAnimation;
            BeginFlowStep(2, "동작 고르기", hasMotion ? FlowState.Done : FlowState.Optional,
                "ZEPETO 기본 동작 중에서 하나 고릅니다. 직접 만들 거면 3번부터 하세요.");

            DrawMotionChoiceRow(workflow);

            if (loader != null && animationClipProperty == null)
            {
                DrawMiniHelp(
                    "이 LOADER에는 AnimationClip 필드가 없어서 동작을 연결할 수 없습니다. "
                    + "ZEPETO Studio 템플릿의 LOADER에는 PlaygroundController 컴포넌트가 붙어 있어야 합니다.",
                    MessageType.Warning);
            }

            string blockReason = GetSelectedMotionBlockReason();
            if (!string.IsNullOrEmpty(blockReason))
            {
                DrawMiniHelp(blockReason, MessageType.Warning);
            }

            DrawSelectedMotionPlayStopButtons(workflow, true);
            DrawUseSelectedMotionButton(workflow);
            EndFlowStep();
        }

        // ---------------------------------------------------------------- 3

        private void DrawStep3ExportBody()
        {
            bool exported = System.IO.File.Exists(ToAbsoluteProjectPath(ExportedRigPath));
            BeginFlowStep(3, "Blender용 몸 내보내기", exported ? FlowState.Done : FlowState.Now,
                "처음 한 번만 하면 됩니다. ZEPETO 뼈대를 Blender가 읽을 수 있는 FBX로 내보냅니다.");

            DrawRigExportBody();
            EndFlowStep();
        }

        // ---------------------------------------------------------------- 4

        private void DrawStep4Blender()
        {
            BeginFlowStep(4, "Blender에서 모션 만들기", FlowState.Now,
                "여기서 Unity를 잠깐 떠납니다. Blender에서 포즈를 만들고 'Unity로 보내기'를 누르세요.");

            DrawGoToBlenderBody();
            EndFlowStep();
        }

        // ---------------------------------------------------------------- 5

        private void DrawStep5CheckOnMyCharacter()
        {
            bool armed = EditorApplication.isPlaying && LiveReloadArmed;
            BeginFlowStep(5, "내 캐릭터로 확인", armed ? FlowState.Done : FlowState.Now,
                "Play를 켜둔 채로, Blender에서 보낼 때마다 내 아바타에 바로 반영됩니다.");

            DrawLivePreviewBody();

            showManualImport = EditorGUILayout.Foldout(showManualImport, "직접 등록하기 (Mixamo 등)", true);
            if (showManualImport)
            {
                DrawManualMotionImportBody();
            }

            EndFlowStep();
        }

        // ---------------------------------------------------------------- 6

        private void DrawStep6AdjustClip(WorkflowStatus workflow)
        {
            bool canEdit = workflow.CanClipEdit;
            BeginFlowStep(6, "클립 조정", clipStageComplete ? FlowState.Done : FlowState.Optional,
                "배속, 길이, 반복을 손봅니다. 그대로 써도 되면 건너뛰세요.");

            if (!canEdit)
            {
                DrawMissing("먼저 2번에서 동작을 고르고 '2번 적용 / 이 동작 쓰기'를 눌러야 조정할 수 있습니다. 3~5번에서 만든 모션도 2번 목록에 함께 나오니 거기서 골라 적용하세요.");
            }

            DrawClipAdjustBody(workflow);
            EndFlowStep();
        }

        // ---------------------------------------------------------------- 7

        private void DrawStep7Export(WorkflowStatus workflow)
        {
            BeginFlowStep(7, "제페토로 내보내기", FlowState.Later,
                "의상은 .zepeto로 내보내고, 모션은 ZEPETO World에 넣습니다.");

            DrawSaveExportBody(workflow);
            DrawPublishGuide();
            EndFlowStep();
        }
    }
}
