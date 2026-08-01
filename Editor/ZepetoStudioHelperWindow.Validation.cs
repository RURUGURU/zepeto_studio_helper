using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Diagnostics 목록에 보여줄 검사들.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// Diagnostics 목록을 통째로 다시 만든다.
        /// </summary>
        /// <remarks>
        /// 검사 순서가 곧 설계다: 패키지 → LOADER → 직렬화 필드 셋 → 아이디 → prefab → controller → clip → 안전 상태.
        /// 앞의 검사가 실패하면 뒤의 검사는 원인이 아니라 증상만 보고하게 되므로, 사용자가 위에서부터 읽었을 때
        /// 맨 위 줄이 고쳐야 할 것이 되도록 배치했다.
        /// 목록 중간에서 바인딩을 한 번 다시 잡는 것도 그래서다. loader를 새로 찾았다면 앞선 세 SerializedProperty는
        /// 이미 사라진 오브젝트를 가리키고 있고, 그대로 두면 "필드가 없습니다"라는 잘못된 진단이 나간다.
        /// </remarks>
        private void ValidateState()
        {
            validationMessages.Clear();

            string foundZepetoStudioVersion;
            bool isRequiredPackageVersionValid = IsRequiredZepetoStudioPackageInstalled(out foundZepetoStudioVersion);
            AddValidation(
                isRequiredPackageVersionValid,
                RequiredPackage + " " + foundZepetoStudioVersion + " is installed (minimum " + MinimumPackageVersion + ").",
                string.IsNullOrEmpty(foundZepetoStudioVersion)
                    ? RequiredPackage + " package was not found. Add it from the ZEPETO registry."
                    : RequiredPackage + " " + foundZepetoStudioVersion + " is older than the minimum " + MinimumPackageVersion + ".");

            if (loader == null)
            {
                loader = FindLoaderGameObject();
            }
            AddValidation(loader != null, "Active scene has LOADER.", "Active scene does not have LOADER.");

            if (loader != null && (zepetoIdProperty == null || animationClipProperty == null || animatorControllerProperty == null))
            {
                FindLoaderAndSerializedFields();
            }

            AddValidation(zepetoIdProperty != null, "LOADER has zepetoId serialized field.", "LOADER is missing zepetoId serialized field.");
            AddValidation(animationClipProperty != null, "LOADER has AnimationClip serialized field.", "LOADER is missing AnimationClip serialized field.");
            AddValidation(animatorControllerProperty != null, "LOADER has AnimatorController serialized field.", "LOADER is missing AnimatorController serialized field.");

            if (zepetoIdProperty != null && TryUpdateSerializedObject(zepetoIdObject))
            {
                if (string.IsNullOrEmpty(zepetoIdProperty.stringValue))
                {
                    validationMessages.Add(new ValidationMessage("아이디가 비어 있습니다. 1번에서 내 ZEPETO 아이디를 입력하고 'ID 적용'을 누르세요.", MessageType.Warning));
                }
                else
                {
                    validationMessages.Add(new ValidationMessage("아이디가 설정되어 있습니다: " + zepetoIdProperty.stringValue, MessageType.Info));
                }
            }

            ValidatePrefab();
            ValidateAnimatorController();
            ValidateAnimationClip();

            // 여기는 강제 갱신(true)이어야 한다. Editor/ 전체에서 ValidateState를 부르는 자리는 39곳인데,
            // 바로 앞줄에서 RefreshSafetySnapshot을 먼저 부르는 곳은 네 곳뿐이다: ZepetoStudioHelperWindow.cs의
            // RefreshAll, Safety.cs의 RecoverSafetyState / ClearConsoleAndSessionSummary, Steps.cs의
            // "Fresh Log Check" 버튼. 나머지 35곳 - 아이디 적용, 동작 복사/적용, clip 조정, FBX 설정,
            // 리그 내보내기, Play 요청 - 에는 여기가 안전 상태를 다시 재는 유일한 지점이다.
            //
            // 캐시를 존중하면(false) 사실상 언제나 낡은 값을 읽게 된다. 패널이 매 OnGUI마다
            // GetSafetySnapshot(false)를 부르므로(Flow.cs의 DrawMotionWorkspace, Steps.cs의
            // DrawWarningCleanupPanel) SafetyRefreshIntervalSeconds(2초) 타이머는 거의 항상 데워져 있고,
            // 방금 누른 동작이 SDK 예외 폭주를 시작시켜도 이 목록은 캐시가 식을 때까지 "Safe Status is clean."
            // 을 그대로 유지한다. 바로 그것을 말해 주는 것이 이 목록의 존재 이유다.
            //
            // 비용은 알고 받는다. BuildSafetySnapshot은 로그 꼬리 RecentLogTailBytes(64 KB)를 디스크에서 읽어
            // CriticalLoopKeywords를 다시 훑는다. 그래도 ValidateState는 그리기 경로에 없다 - 39곳은 전부
            // 버튼/변경 이벤트 처리, Play 전환 콜백, 그리고 RefreshAll(창 열기 / OnEnable / scene 전환)이고
            // 매 repaint마다 도는 자리는 하나도 없다. 즉 사용자 동작 한 번에 64 KB를 한 번 더 읽는 것이고,
            // 같은 읽기가 두 번 나는 것은 위의 네 곳뿐이다.
            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (snapshot.HasBlockingRisk)
            {
                validationMessages.Add(new ValidationMessage("Safe Status is blocking risky actions: " + snapshot.Message, MessageType.Error));
            }
            else if (snapshot.HasWarning)
            {
                validationMessages.Add(new ValidationMessage("Safe Status warning: " + snapshot.Message, MessageType.Warning));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("Safe Status is clean.", MessageType.Info));
            }

            Repaint();
        }

        private void ValidatePrefab()
        {
            if (clothingPrefab == null)
            {
                validationMessages.Add(new ValidationMessage("No clothing prefab selected.", MessageType.Warning));
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(clothingPrefab);
            if (prefabPath.StartsWith(ContentsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                validationMessages.Add(new ValidationMessage("Selected prefab is under " + ContentsRoot + ".", MessageType.Info));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("Selected prefab should be under " + ContentsRoot + ". Current path: " + prefabPath, MessageType.Warning));
            }
        }

        private void ValidateAnimationClip()
        {
            if (animationClipProperty == null || !TryUpdateSerializedObject(animationClipObject))
            {
                return;
            }

            AnimationClip clip = animationClipProperty.objectReferenceValue as AnimationClip;
            if (clip == null)
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip is empty.", MessageType.Warning));
                return;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (IsPackageOrPackageCachePath(path))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip points to a package source. Copy it before editing: " + path, MessageType.Warning));
            }
            else if (IsClipEditEligiblePath(path))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip is ready for clip adjust: " + path, MessageType.Info));
            }
            else
            {
                validationMessages.Add(new ValidationMessage(
                    "LOADER AnimationClip points to a project asset. Clip adjust only supports "
                    + AnimationCopyRoot + " or " + CustomMotionRoot + ": " + path, MessageType.Warning));
            }
        }

        /// <summary>
        /// 이 경로의 clip을 clip 편집의 SOURCE로 쓸 수 있는지.
        /// </summary>
        /// <remarks>
        /// 헬퍼가 소유한 두 루트가 모두 자격을 갖는다. 둘 다 이 패키지가 소유하고 프로젝트가 쓸 수 있는 폴더이기
        /// 때문이다: AnimationCopyRoot("Assets/ZepetoHelper/Animations")에는 2번이 만든 "_editable" 사본이 있고,
        /// CustomMotionRoot("Assets/ZepetoHelper/Motions")는 Blender 애드온 브리지와 수동 FBX 임포트가 모션을
        /// 떨어뜨리는 곳이다(LiveFromBlender.anim, 추출한 take들). 앞의 하나만 받아주던 때에는 Blender / 임포트
        /// 경로가 만들어낸 모든 것에 대해 6번이 영구히 막혀 있었다.
        /// SDK 자신의 패키지 폴더는 계속 자격이 없어야 한다. Packages/ 와 Library/PackageCache/ 는 읽기 전용이며
        /// 이 컴퓨터의 모든 프로젝트가 공유하므로, 거기를 편집하면 이 기계에 있는 모든 프로젝트의 SDK가 망가진다 -
        /// 그쪽은 이 검사보다 먼저 IsPackageOrPackageCachePath가 걸러낸다.
        /// 여기서 넓히는 것은 자격(어떤 clip을 읽을 수 있는가)뿐이다. 두 경로 상수를 합치지 않으며, 편집 결과를
        /// 어디에 쓰는지도 바뀌지 않는다 - 저장은 여전히 ClipEditRoot로 간다.
        /// Workflow.cs의 BuildWorkflowStatus도 같은 상태를 읽지만 그쪽은 메시지가 아니라 bool을 만든다. 두 판정이
        /// 갈라지지 않도록 "두 루트" 규칙의 유일한 정의를 이 함수 하나에 두고 양쪽이 같이 호출한다.
        /// </remarks>
        private static bool IsClipEditEligiblePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            return assetPath.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith(CustomMotionRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private void ValidateAnimatorController()
        {
            if (animatorControllerProperty == null || !TryUpdateSerializedObject(animatorControllerObject))
            {
                return;
            }

            UnityEngine.Object controller = animatorControllerProperty.objectReferenceValue;
            if (controller == null)
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController is empty.", MessageType.Warning));
                return;
            }

            string path = AssetDatabase.GetAssetPath(controller);
            if (IsPackageOrPackageCachePath(path))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController points to package cache. Use Local Controller Fix before assigning clips: " + path, MessageType.Warning));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController is project-local: " + path, MessageType.Info));
            }

            // 아바타가 실제로 연기하는 것은 override 슬롯이지 LOADER의 AnimationClip 필드가 아니므로, 슬롯을 따로
            // 읽어 보고한다. 여기서 A_pose가 잡히면 workflow가 어떤 동작을 골랐다고 믿든 아바타는 가만히 서 있는다.
            // 아래 세 갈래는 서로 다른 고장이고, 이 창에서 그 셋을 구분할 수 있는 곳은 여기뿐이다:
            // 슬롯이 비었다(2번 적용을 아직 안 했다) / 슬롯 길이가 StaticPoseMaxLength 이하다(정지 포즈가 물려
            // 있다) / 슬롯과 필드가 서로 다른 clip이다(2번 적용이 중간에 실패했거나 그 뒤에 필드만 바뀌었다).
            // 재생 규칙 자체는 Loader.cs의 ApplyClipToOverrideController <summary> 참조.
            AnimationClip playbackClip = GetPlaybackClip();
            AnimationClip assignedClip = animationClipProperty == null
                ? null
                : animationClipProperty.objectReferenceValue as AnimationClip;

            if (playbackClip == null)
            {
                validationMessages.Add(new ValidationMessage("재생 슬롯이 비어 있습니다. 2번에서 동작을 다시 적용하세요.", MessageType.Warning));
            }
            else if (playbackClip.length <= StaticPoseMaxLength)
            {
                validationMessages.Add(new ValidationMessage(
                    "재생 슬롯이 정지 포즈입니다 (" + playbackClip.name + ", " + playbackClip.length.ToString("0.00") + "s). "
                    + "이 상태로 Play하면 아바타가 움직이지 않습니다. 2번에서 동작을 적용하세요.", MessageType.Warning));
            }
            else if (assignedClip != null && playbackClip != assignedClip)
            {
                validationMessages.Add(new ValidationMessage(
                    "선택한 동작(" + assignedClip.name + ")과 실제 재생될 동작(" + playbackClip.name + ")이 다릅니다. "
                    + "2번에서 동작을 다시 적용하세요.", MessageType.Warning));
            }
            else
            {
                validationMessages.Add(new ValidationMessage(
                    "재생될 동작: " + playbackClip.name + " (" + playbackClip.length.ToString("0.00") + "s)", MessageType.Info));
            }
        }

        // bool 하나를 Info / Error 두 단계로만 접는다. 손으로 만든 메시지들이 Warning을 쓰는 것과 대비되는데,
        // 이건 의도된 세 단계 어휘다: Error는 "이 상태로는 다음 단계가 불가능하다", Warning은 "진행은 되지만
        // 결과가 사용자가 기대한 것과 다를 것이다", Info는 "확인됨". 이 함수로 들어오는 검사는 전부 앞의 것이므로
        // 여기에 Warning 갈래가 없는 것이지, 두 단계밖에 없는 것이 아니다.
        private void AddValidation(bool condition, string okMessage, string failMessage)
        {
            validationMessages.Add(new ValidationMessage(condition ? okMessage : failMessage, condition ? MessageType.Info : MessageType.Error));
        }

        private struct ValidationMessage
        {
            public readonly string Text;
            public readonly MessageType Type;

            public ValidationMessage(string text, MessageType type)
            {
                Text = text;
                Type = type;
            }
        }
    }
}
