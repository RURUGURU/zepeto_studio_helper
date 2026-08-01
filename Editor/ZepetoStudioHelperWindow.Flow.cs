using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 창의 step 레이아웃: 번호 붙은 일곱 단계를 위에서 아래로, 한 단계에 한 가지 일씩.
    ///
    /// 예전 4카드 레이아웃을 대체했다. 거기서는 Blender 왕복 전체가 step 2 안에 A/B/C/C-2 라는 알파벳
    /// 하위 상자로 묻혀 있었다. 그 탓에 step 2 하나가 help box 여섯 개와 초록 Play 버튼 두 개를 담았고,
    /// 가장 먼저 하는 일(몸 내보내기)이 가장 마지막 일(완성된 fbx 가져오기) 아래에 놓였으며, step 카드는
    /// 자기 stage가 잠기거나 완료되면 접히기 때문에 Blender 도구함 전체가 하필 그것을 쓰는 동안 사라졌다.
    ///
    /// 이 레이아웃이 지키는 규칙 둘:
    ///  1. 모든 step은 번호다. 사용자가 하는 일이면 번호와 고정된 자리를 갖는다.
    ///  2. stage 잠금 뒤에 아무것도 숨기지 않는다. step은 자기 상태를 보여주고 무엇이 빠졌는지 말할 뿐,
    ///     자기 내용을 "이전 단계를 완료하면 열립니다"로 바꿔치지 않는다. 막다른 길을 만든 것이 바로
    ///     잠금이었다 - 누를 수 없는 버튼, 그리고 접혀서 보이지 않는 설명.
    /// </summary>
    /// <remarks>
    /// [QC][Invariant:sequential_unlock]
    /// 순차 잠금은 규칙 2 이전의 모델이었다. 그 규칙은 "뒤 stage에는 이전 실행이 남긴 낡은 완료 에셋이 있을
    /// 수 있으므로, 앞의 필수 단계가 끝나지 않았으면 뒤 stage는 잠긴 채로 있어야 하고 완료 여부보다 대기
    /// 여부를 먼저 본다"였다. 위 규칙 2를 택하면서 폐기했고, 그것을 강제하던 기계(Workflow.cs의
    /// GetSequentialStageState / IsStageComplete / IsStageWaiting / enum StepState)는 호출자가 하나도 남지
    /// 않아 함께 지웠다. 이 기록을 규칙 2 옆에 붙여 두는 이유는, 둘이 떨어져 있던 동안 서로 정반대 말을
    /// 하면서도 아무도 눈치채지 못했기 때문이다. 잠금을 되살리고 싶으면 규칙 2부터 뒤집어야 한다.
    /// </remarks>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // 카드 머리의 배지가 이 넷 중 하나를 고른다. 사용자에게 보이는 뜻은 FlowStateLabel이 붙이는 글자다.
        //   Done     "완료" - 이 카드에서 할 일은 끝났다.
        //   Now      "지금" - 지금 손댈 카드.
        //   Later    "아직" - 순서상 나중. 잠긴 것이 아니라 내용은 그대로 다 보인다(위 규칙 2).
        //   Optional "선택" - 건너뛰어도 되는 카드.
        // 배지는 색과 글자만 정한다. 어떤 컨트롤의 존재 여부도 여기에 걸려 있지 않다.
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
        /// step 카드 하나. 본문은 언제나 그린다 - 무엇이 빠졌는지 알리는 줄까지 포함해서 무엇을 담을지는
        /// 호출자가 정한다. state는 머리 띠 색과 배지만 칠한다.
        /// </summary>
        /// <remarks>
        /// 여기서 연 세로 그룹은 EndFlowStep이 닫는다. 둘 사이에서 `return`하면 그룹이 닫히지 않은 채로
        /// 프레임이 끝나 그 OnGUI의 GUILayout 스택이 통째로 어긋난다 - 위 규칙 2가 막으려는 바로 그 함정이다.
        /// 카드 본문은 조기 반환 대신 DrawMissing 한 줄로 부족한 것을 말하고 끝까지 그린다.
        /// </remarks>
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
        /// "무엇이 빠졌는가" 줄. 잠금이 아니라, 무엇을 하러 가면 되는지 알려 주는 문장일 뿐이다.
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

            DrawSelectedMotionPlayStopButtons(workflow);
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
            // 배지를 Now로 고정한다. 이 카드의 일은 Unity 밖 Blender에서 끝나므로 여기에는 "끝났다"로 볼
            // 신호 자체가 없다. 결과물이 돌아왔는지는 카드 5의 라이브 확인과 카드 2의 목록이 대신 보여준다.
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
            // 배지를 Later로 고정한다. .zepeto 파일이 실제로 있는지는 DrawSaveExportBody 안에서
            // GetExportPackageStatusText가 "출력 파일" 줄에 그대로 찍는다. 파일이 생겼다고 배지를 완료로
            // 바꾸면, 실제 업로드는 ZEPETO Studio 웹에서 아직 남았는데도 다 끝난 것으로 읽힌다.
            BeginFlowStep(7, "제페토로 내보내기", FlowState.Later,
                "의상은 .zepeto로 내보내고, 모션은 ZEPETO World에 넣습니다.");

            DrawSaveExportBody(workflow);
            DrawPublishGuide();
            EndFlowStep();
        }
    }
}
