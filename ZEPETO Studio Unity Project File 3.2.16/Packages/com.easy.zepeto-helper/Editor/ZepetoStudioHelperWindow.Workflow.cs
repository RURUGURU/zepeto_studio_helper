using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Play 세션 기록과 헤더의 "현재 작업" / "다음 행동" 줄이 쓰는 내부 stage 번호 네 개, 그리고 그 진행 표시.
    /// stage 번호는 카드 번호가 아니다 - 대응표는 아래 PreviewStage 상수 주석에 있다.
    /// 카드 일곱 장의 상태는 Flow.cs가 WorkflowStatus 불리언으로 카드마다 따로 계산하며 여기를 거치지 않는다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // ------------------------------------------------------------ preview stage identity
        //
        // 창이 그리는 카드는 번호가 붙은 일곱 장이다(Flow.cs). 이 stage 번호는 아직 넷뿐이다. 카드 레이아웃보다
        // 먼저 만들어져서 번호가 서로 맞지 않는다. 대응은 이렇다.
        //
        //     PreviewStageAvatarOutfit = 1  ->  card 1  "아바타 준비"
        //     PreviewStageMotion       = 2  ->  card 2  "동작 고르기"  AND  card 5  "내 캐릭터로 확인"
        //     PreviewStageClipAdjust   = 3  ->  card 6  "클립 조정"
        //     PreviewStageExport       = 4  ->  card 7  "제페토로 내보내기"
        //
        // 카드 3, 4(Blender 왕복)는 자기 stage가 없다. 거기서 만든 결과물은 카드 2의 동작 목록으로 돌아오므로
        // 그 우회 구간 전체가 stage 2 안에서 벌어진다.
        //
        // stage 2를 카드 둘이 나눠 쓰는 것은 의도한 설계다. 카드 2의 미리보기는 Motion.cs에서
        // RequestPlayMode(PreviewStageMotion)으로 Play에 들어가고, 카드 5의 라이브 확인은 Safety.cs의
        // RequestLivePreviewPlay가 같은 stage를 세운다. 둘 다 "아바타에 동작을 입혀보는 중"이라는 뜻이고
        // stage가 결정하는 것은 어느 Play 세션이 살아 있는가뿐이라, 하나로 묶은 것은 잔재가 아니라 맞다.
        //
        // 값 자체가 load-bearing이라 카드 번호에 맞춰 다시 매기면 안 된다. Motion.cs와 Safety.cs는 이름 붙은
        // 상수로 넘기지만, activePreviewStage가 SessionState(ActivePreviewStageSessionKey)에 저장되므로 지금
        // 열려 있는 에디터 세션이 이미 이 int 중 하나를 들고 있을 수 있다. 게다가 클립 조정 미리보기의 Play
        // 경로는 stage 3 하나만 보고 열린다(Safety.cs RequestPlayMode의 previewStage == PreviewStageClipAdjust).
        // 번호를 옮기면 그 미리보기가 조용히 죽는다.
        private const int PreviewStageAvatarOutfit = 1;
        private const int PreviewStageMotion = 2;
        private const int PreviewStageClipAdjust = 3;
        private const int PreviewStageExport = 4;

        // "지금 어느 stage도 Play를 소유하지 않음"을 뜻하는 sentinel. 위 네 값과 달리 저장된 의미가 없는
        // '없음' 표시라서, 여섯 군데에 흩어져 있던 -1 리터럴을 이름 하나로 모은 것뿐이다.
        private const int PreviewStageNone = -1;

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

            // 숨길 수 없는 유일한 Stop. 나머지 Stop은 전부 step 카드 안에 있다. 지금 레이아웃은 카드를 접지
            // 않지만(Flow.cs 규칙 2), 그 전 레이아웃에서는 stage 잠금·완료·도메인 리로드로 날아간 상태 때문에
            // 카드가 통째로 접혔고, 그 일이 Play 중에 벌어지면 사용자는 돌아가는 Game view를 보면서 이 창
            // 안에는 빠져나갈 길이 없는 채로 남았다. 이 버튼은 접힐 수 있는 모든 것 위에 있다.
            //
            // 무조건 그린다. 달라지는 것은 isPlaying이 정하는 enabled뿐이다. `if (isPlaying && ...)`로 감싸면
            // Play가 프레임 도중에 켜지거나 꺼질 때 Layout 패스와 Repaint 패스의 컨트롤 개수가 달라져
            // GUILayout 그룹이 깨지고 컨트롤이 튄다 - 버튼이 나타났다가 사라진다. 존재 여부는 흔들리는 상태에
            // 의존하면 안 되고, `enabled`만 의존해도 된다.
            if (DrawColoredActionButton("■ Stop", isPlaying, StopRed,
                    GUILayout.Width(90f), GUILayout.Height(20f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            // 같은 규칙: 항상 그리고, 글자만 바뀐다.
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
        /// 헤더의 "현재 작업" 줄. 여기 찍히는 번호는 사용자가 아래 카드에서 그대로 찾을 수 있는 번호여야 하므로,
        /// 각 줄은 해당 카드의 제목을 글자 그대로 옮겨 적는다(Flow.cs).
        ///
        /// stage 넷이 카드 일곱 장을 덮는다. stage 2는 카드 2와 카드 3~5에 걸치는데, 그중 자기만의 감지 가능한
        /// 상태를 가진 것은 라이브 확인 카드뿐이라(Blender 감시가 켜져 있는가) 그 카드만 따로 한 줄을 받는다.
        /// 카드 3, 4는 사용자가 Blender에 가 있는 동안 보고할 상태가 없다. 그쪽은 아래 "다음 행동" 줄이 가리킨다.
        /// </summary>
        private string GetCurrentStageText(WorkflowStatus workflow)
        {
            switch (GetActiveStageNumber(workflow))
            {
                case PreviewStageAvatarOutfit:
                    return "1. 아바타 준비";
                case PreviewStageMotion:
                    // 글자만 바뀌는 분기. 카드 5의 Done 판정(Flow.cs)도 같은 두 값을 보지만, 여기서 달라지는
                    // 것은 텍스트뿐이라 무조건 그리기 규칙을 어기지 않는다 - 컨트롤의 존재는 이 값에 안 걸린다.
                    return EditorApplication.isPlaying && LiveReloadArmed
                        ? "5. 내 캐릭터로 확인"
                        : "2. 동작 고르기";
                case PreviewStageClipAdjust:
                    return "6. 클립 조정";
                default:
                    return "7. 제페토로 내보내기";
            }
        }

        private void LoadWorkflowStageProgress()
        {
            avatarOutfitStageComplete = SessionState.GetBool(AvatarOutfitStageCompleteSessionKey, false);
            motionSelectStageComplete = SessionState.GetBool(MotionSelectStageCompleteSessionKey, false);
            clipStageComplete = SessionState.GetBool(ClipStageCompleteSessionKey, false);
            activePreviewStage = SessionState.GetInt(ActivePreviewStageSessionKey, PreviewStageNone);
        }

        private void SaveWorkflowStageProgress()
        {
            SessionState.SetBool(AvatarOutfitStageCompleteSessionKey, avatarOutfitStageComplete);
            SessionState.SetBool(MotionSelectStageCompleteSessionKey, motionSelectStageComplete);
            SessionState.SetBool(ClipStageCompleteSessionKey, clipStageComplete);
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
        }

        // 완료 해제는 아래로 번진다: 1번을 풀면 2번과 6번의 완료 표시도 함께 풀린다. 뒤 단계의 "완료"는 앞
        // 단계의 결과물을 전제로 하기 때문이다 - 의상이나 아바타를 바꾼 뒤에도 2/6번이 완료로 남아 있으면
        // 사용자는 낡은 전제 위에서 저장하게 된다. Scenes.cs가 다른 scene을 열 때 이 규칙에 기대어
        // SetAvatarOutfitStageComplete(false) 한 번만 호출한다.
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

        /// <summary>
        /// 한 번의 OnGUI가 쓰는 상태 스냅샷을 통째로 만든다. 대입 순서가 load-bearing이다. 세 층이 위에서
        /// 아래로만 의존한다.
        ///  1. 원시 상태 - loader / zepetoId / clothingPrefab / 연결된 clip 처럼 씬과 에셋에서 그냥 읽는 값.
        ///  2. 파생 자격 - HasAvatarPlayInputs -> hasMotionPlayInputs 처럼 1번을 조합해 얻는 값.
        ///  3. 지금 눌러도 되는가 - CanPlayMotion / CanClipEdit.
        /// 3번 두 값이 안전 상태를 다르게 접는 이유: Play는 실제로 도메인을 띄우므로 컴파일 중 / 갱신 중 /
        /// 이미 Play 중까지 보는 CanEnterPlayMode 전체가 필요하지만, 클립 편집은 에셋만 건드리므로
        /// HasBlockingRisk 하나면 충분하다.
        /// </summary>
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
            // AnimationCopyRoot만 봐서는 답이 안 나온다. 2번이 만든 _editable 복사본은 거기 떨어지지만,
            // Blender/라이브 파이프라인이 만들어내는 것은 전부 CustomMotionRoot에 떨어진다. 복사 루트만
            // 검사했더니 하필 이 도구가 만들라고 존재하는 바로 그 모션들에서 카드 6이 영영 열리지 않았다.
            // 두 루트 모두 자격이 있고, 두 루트 판정은 IsClipEditEligiblePath 한 곳에만 두어 여기와
            // Validation.cs가 서로 따로 놀 수 없게 했다.
            workflow.HasEditableAssignedAnimation = workflow.HasAssignedAnimation
                && IsClipEditEligiblePath(workflow.AssignedAnimationPath);
            workflow.AnimatorControllerPath = GetAnimatorControllerPath();
            workflow.HasLocalAnimatorController = !string.IsNullOrEmpty(workflow.AnimatorControllerPath)
                && !IsPackageOrPackageCachePath(workflow.AnimatorControllerPath);
            workflow.HasAvatarPlayInputs = workflow.HasLoader && workflow.HasZepetoId;
            bool hasMotionPlayInputs = workflow.HasAvatarPlayInputs && workflow.HasAssignedAnimation;
            workflow.CanPlayMotion = hasMotionPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanClipEdit = workflow.HasEditableAssignedAnimation && !snapshot.HasBlockingRisk;
            return workflow;
        }
    }
}
