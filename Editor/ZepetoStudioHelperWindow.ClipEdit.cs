using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 배속 / 자르기 / 반복 편집. 결과는 언제나 새 .anim 사본에 쓴다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // [AUDIT][Risk:Major][Scope:clip_loop_save]
        // Loop 토글을 비교할 기준선: 6번이 그 clip을 처음 봤을 때 SOURCE clip이 실제로 가지고 있던 m_LoopTime.
        // 예전 HasClipAdjustInput은 그 대신 하드코딩된 clipLoop=true 기본값을 놓고 "clipLoop이 false인가?"를
        // 물었다. 그래서 반복하지 않는 clip의 Loop를 ON으로 켜는 것이 "입력 없음"으로 세어졌고, Steps.cs는
        // 아무것도 하지 않는 갈래로 빠져 파일을 단 한 번도 쓰지 않은 채 그 단계를 완료로 칠했다.
        //
        // 다른 ClipAdjust.* 키들과 같은 SessionState 범위를 쓴다: 기준선은 Play 진입이라는 도메인 리로드를
        // 넘겨야 하지만, 이전 에디터 세션에서 남은 값은 지금 물려 있지도 않은 clip을 설명하게 되고 실제 변경을
        // 변경 없음으로 보이게 만든다.
        private const string ClipAdjustLoopOriginalSessionKey = "Easy.ZepetoHelper.ClipAdjust.LoopOriginal";

        // EnsureClipAdjustDefaults가 채운다. clipLoop과 같은 값으로 초기화하는 것은, clip이 하나도 물려 있지
        // 않은 상태에서 둘이 일치해 HasClipAdjustInput이 "대기 중인 loop 변경 없음"을 답하게 하기 위해서다.
        private bool clipLoopOriginal = true;

        private bool PrepareClipAdjustPreviewBeforePlay()
        {
            // 첫 질문은 복구 '전'의 clip에 대고 한다. 이 순서는 의도된 것이다: HasClipAdjustInput은 clip.length로
            // 자르기 범위를 클램프하므로, 이미 되타이밍된 임시 미리보기 clip을 기준으로 물으면 같은 UI 값이 다른
            // 답을 낸다. 그래서 복구는 두 갈래 모두에서 불리고(결과적으로 무조건 실행된다), 복구한 뒤에는 clip을
            // 다시 읽는다 - 그 시점에 LOADER는 원래 작업 clip으로 돌아와 있다.
            // 호출이 하나뿐이 되도록 if 위로 끌어올려 "정리"하면 동작이 바뀐다.
            AnimationClip sourceClip = GetAssignedAnimationClip();
            if (!HasClipAdjustInput(sourceClip))
            {
                RestoreTemporaryClipAdjustPreview();
                return true;
            }

            RestoreTemporaryClipAdjustPreview();
            sourceClip = GetAssignedAnimationClip();

            string reason;
            if (!CanEditAnimationClip(sourceClip, out reason))
            {
                statusMessage = reason;
                ValidateState();
                return false;
            }

            string restorePath = AssetDatabase.GetAssetPath(sourceClip);
            SaveClipAdjustSessionState(restorePath);
            ClipEditSettings settings = BuildClipEditSettings(sourceClip);
            // [AUDIT][Risk:Major][Scope:play_preview]
            // 미리보기 Play는 작업 중인 clip을 절대 건드리면 안 된다. 임시 asset을 만들어 LOADER에 물리고,
            // Play가 끝나면 OnPlayModeStateChanged가 restorePath로 되돌린다.
            ClipEditResult result = ClipEditUtility.CreateClipAdjustedPreviewClip(sourceClip, settings, ClipAdjustPreviewPath);
            if (!result.Success || result.Clip == null)
            {
                statusMessage = "배속 미리보기 clip을 만들지 못했습니다: " + result.Message;
                ValidateState();
                return false;
            }

            if (!AssignAnimationClip(result.Clip, true))
            {
                AssetDatabase.DeleteAsset(ClipAdjustPreviewPath);
                statusMessage = "배속 미리보기 clip을 LOADER에 연결하지 못했습니다.";
                ValidateState();
                return false;
            }

            SessionState.SetBool(ClipAdjustPreviewActiveSessionKey, true);
            SessionState.SetString(ClipAdjustPreviewRestorePathSessionKey, restorePath ?? string.Empty);
            statusMessage = "배속 미리보기 clip을 임시 연결했습니다. Stop 후 원래 작업 clip으로 돌아갑니다.";
            return true;
        }

        private void RestoreTemporaryClipAdjustPreview()
        {
            // 세션 플래그와 실제 asset을 둘 다 본다. 한쪽만 남는 경우가 실제로 생기기 때문이다: 플래그는
            // 세션과 함께 사라지지만 asset은 디스크에 남고, 반대로 사용자가 Project 창에서 asset을 지워도
            // 플래그는 남는다.
            bool isPreviewActive = SessionState.GetBool(ClipAdjustPreviewActiveSessionKey, false);
            if (!isPreviewActive && AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipAdjustPreviewPath) == null)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string restorePath = SessionState.GetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
            AnimationClip restoreClip = string.IsNullOrEmpty(restorePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<AnimationClip>(restorePath);

            if (restoreClip != null)
            {
                FindLoaderAndSerializedFields();
                AssignAnimationClip(restoreClip, true);
            }

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipAdjustPreviewPath) != null)
            {
                AssetDatabase.DeleteAsset(ClipAdjustPreviewPath);
            }

            SessionState.SetBool(ClipAdjustPreviewActiveSessionKey, false);
            SessionState.SetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
            statusMessage = restoreClip == null
                ? "배속 미리보기 종료: 복구할 원래 clip을 찾지 못했습니다."
                : "배속 미리보기 종료: 원래 작업 clip으로 되돌렸습니다. (" + restoreClip.name + ")";
            ValidateState();
        }

        /// <summary>
        /// 6번의 저장 경로. 여섯 개의 거절 조건을 모두 통과한 뒤에야 clipStageComplete를 세운다:
        /// Play 중, 안전 스냅샷 차단, CanEditAnimationClip(다섯 가지 세부 사유), 조정값 없음,
        /// ClipEditUtility 실패, AssignAnimationClip 실패.
        /// 마지막 두 개가 중요하다 - 파일은 만들었지만 LOADER에 연결하지 못한 경우는 성공이 아니고,
        /// 그때 단계를 완료로 칠하면 사용자는 반영되지 않은 clip을 들고 7번으로 넘어간다.
        /// </summary>
        private void SaveClipAdjustToCurrentClip()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 clip 파일을 저장하지 않습니다. 먼저 Stop을 눌러주세요.";
                ValidateState();
                return;
            }

            // 여기서는 2초 캐시를 쓰지 않고 강제로 새로 만든다. 이 아래가 파일을 쓰는 자리라서, 방금 들어온
            // 오류까지 반영된 상태로 판단해야 하기 때문이다.
            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (snapshot.HasBlockingRisk)
            {
                statusMessage = "Clip edit save is blocked by Safety. Press Recover first. / 안전 상태 때문에 저장을 막았습니다.";
                ValidateState();
                return;
            }

            AnimationClip sourceClip = GetAssignedAnimationClip();
            string reason;
            if (!CanEditAnimationClip(sourceClip, out reason))
            {
                statusMessage = reason;
                ValidateState();
                return;
            }

            if (!HasClipAdjustInput(sourceClip))
            {
                statusMessage = "저장할 클립 조정값이 없습니다. 배속, 시작/끝 시간, 반복 옵션 중 하나를 바꾼 뒤 저장하세요.";
                ValidateState();
                return;
            }

            ClipEditSettings settings = BuildClipEditSettings(sourceClip);
            // [AUDIT][Risk:High][Scope:file_io]
            // clip 조정은 copy-on-write다: package/cache clip도, 기존 작업 clip도 제자리에서 편집하지 않는다.
            // 검증 대상: ClipEditRoot 아래에 새 .anim이 생기고, 그 다음 LOADER.AnimationClip이 그 새 asset을 가리킨다.
            ClipEditResult result = ClipEditUtility.CreateClipAdjustedClip(sourceClip, settings, ClipEditRoot);
            if (!result.Success)
            {
                statusMessage = result.Message;
                ValidateState();
                return;
            }

            lastClipEditedClip = result.Clip;
            copiedAnimationClip = result.Clip;
            if (!AssignAnimationClip(result.Clip))
            {
                SelectAndPing(result.Clip);
                statusMessage = "clip edit 파일은 저장했지만 LOADER에 연결하지 못했습니다: " + result.Path;
                ValidateState();
                return;
            }

            SetClipStageComplete(true);
            SelectAndPing(result.Clip);
            statusMessage = "clip 조정을 저장하고 LOADER에 연결했습니다. 이제 7번 내보내기로 넘어가세요: " + result.Path + " / Retimed curves: " + result.ModifiedCurveCount;
            if (!string.IsNullOrEmpty(result.WarningSummary))
            {
                Debug.LogWarning("ZEPETO Studio Helper clip edit warning: " + result.WarningSummary);
                statusMessage += " / Warnings: " + result.WarningSummary;
            }
        }

        private void EnsureClipAdjustDefaults(AnimationClip clip)
        {
            if (clip == null)
            {
                clipAdjustSourcePath = string.Empty;
                return;
            }

            string sourcePath = GetClipAdjustStatePath(clip);
            if (!string.IsNullOrEmpty(clipAdjustSourcePath)
                && string.Equals(clipAdjustSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            clipAdjustSourcePath = sourcePath;
            if (TryRestoreClipAdjustSessionState(sourcePath, clip))
            {
                return;
            }

            motionPreviewSpeed = 1f;
            // [QC][Invariant:loop_baseline]
            // 토글의 초기값은 상수가 아니라 소스 clip이 실제로 하는 동작에서 가져온다. clipLoopOriginal은
            // HasClipAdjustInput이 비교하는 기준선이므로, 화면에 보이는 clipLoop 값과 같은 clip에서 같은
            // 시점에 잡혀야 한다.
            clipLoopOriginal = GetClipLoopTime(clip);
            clipLoop = clipLoopOriginal;
            clipTrimStart = 0f;
            clipTrimEnd = Mathf.Max(0.01f, clip.length);
            SaveClipAdjustSessionState(sourcePath);
        }

        /// <summary>
        /// 임포트된 asset에서 읽은 clip의 진짜 Loop Time. ApplyClipLoopSetting이 쓰는 것과 같은 m_LoopTime
        /// 필드이므로, 읽고-고치고-쓰는 왕복에서 값이 어긋날 수 없다.
        /// </summary>
        private static bool GetClipLoopTime(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            // AnimationClipSettings는 struct가 아니라 class라서, 결과가 참조이고 null 검사를 해야 한다.
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            return settings != null && settings.loopTime;
        }

        /// <summary>
        /// 이 clip의 조정 상태를 어떤 키로 기억할지. 경로를 그대로 쓰지 않고 한 단계 우회하는 이유가 둘이다.
        ///
        /// 배속 미리보기 중에 LOADER에 물려 있는 것은 임시 clip(ClipAdjustPreviewPath)인데, 그것을 키로 쓰면
        /// Play에 들어갔다 나오는 것만으로 사용자의 조정값이 "다른 clip의 것"이 되어 초기화된다. 그래서 미리보기
        /// 경로가 들어오면 복구 대상 경로로 바꿔 답한다.
        ///
        /// 프로젝트에 저장되지 않은 clip은 경로가 없어서 서로 구분할 수 없으므로 "instance:&lt;id&gt;"를 키로 쓴다.
        /// </summary>
        private string GetClipAdjustStatePath(AnimationClip clip)
        {
            if (clip == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.Equals(path, ClipAdjustPreviewPath, StringComparison.OrdinalIgnoreCase))
            {
                string restorePath = SessionState.GetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
                if (!string.IsNullOrEmpty(restorePath))
                {
                    return restorePath;
                }
            }

            return string.IsNullOrEmpty(path)
                ? "instance:" + clip.GetInstanceID().ToString()
                : path;
        }

        private bool TryRestoreClipAdjustSessionState(string sourcePath, AnimationClip clip)
        {
            string savedSourcePath = SessionState.GetString(ClipAdjustSourcePathSessionKey, string.Empty);
            if (string.IsNullOrEmpty(sourcePath)
                || !string.Equals(savedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            float clipLength = clip == null ? 0.01f : Mathf.Max(0.01f, clip.length);
            motionPreviewSpeed = Mathf.Clamp(SessionState.GetFloat(ClipAdjustSpeedSessionKey, 1f), 0.25f, 2f);
            clipTrimStart = Mathf.Clamp(SessionState.GetFloat(ClipAdjustStartSessionKey, 0f), 0f, clipLength);
            clipTrimEnd = Mathf.Clamp(SessionState.GetFloat(ClipAdjustEndSessionKey, clipLength), clipTrimStart + 0.01f, clipLength);
            // 기준선을 UI 값보다 먼저 복원하고, 둘 다의 폴백을 clip 자신으로 두었다. 그래서 기준선 키가 생기기
            // 전부터 살아 있던 세션도 가정된 기본값이 아니라 "아직 아무것도 바뀌지 않았다"는 참인 답을 받는다.
            clipLoopOriginal = SessionState.GetBool(ClipAdjustLoopOriginalSessionKey, GetClipLoopTime(clip));
            clipLoop = SessionState.GetBool(ClipAdjustLoopSessionKey, clipLoopOriginal);
            return true;
        }

        private void SaveClipAdjustSessionState(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            SessionState.SetString(ClipAdjustSourcePathSessionKey, sourcePath);
            SessionState.SetFloat(ClipAdjustSpeedSessionKey, Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f));
            SessionState.SetFloat(ClipAdjustStartSessionKey, Mathf.Max(0f, clipTrimStart));
            SessionState.SetFloat(ClipAdjustEndSessionKey, Mathf.Max(0.01f, clipTrimEnd));
            SessionState.SetBool(ClipAdjustLoopSessionKey, clipLoop);
            SessionState.SetBool(ClipAdjustLoopOriginalSessionKey, clipLoopOriginal);
        }

        /// <summary>
        /// 자르기 UI 값에서 실제로 쓰이는 범위를 계산한다.
        /// </summary>
        /// <remarks>
        /// HasClipAdjustInput("바뀐 것이 있는가")과 BuildClipEditSettings("무엇을 저장할 것인가")는 반드시 같은
        /// 범위를 봐야 하므로 계산은 여기 한 곳에만 둔다. 두 벌로 두었다가 갈라지면 6번은 저장은 하면서
        /// "바뀐 것 없음"이라고 말하거나 그 반대가 되는데, 그것이 이 파일 머리의
        /// [AUDIT][Risk:Major][Scope:clip_loop_save] 블록이 기록하고 있는 바로 그 부류의 버그다.
        /// </remarks>
        private void GetClipAdjustRange(AnimationClip clip, out float clipLength, out float rangeStart, out float rangeEnd)
        {
            clipLength = clip == null ? 0.01f : Mathf.Max(0.01f, clip.length);
            rangeStart = Mathf.Clamp(clipTrimStart, 0f, clipLength);
            rangeEnd = Mathf.Clamp(clipTrimEnd <= 0f ? clipLength : clipTrimEnd, rangeStart + 0.01f, clipLength);
        }

        private bool HasClipAdjustInput(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            float clipLength;
            float rangeStart;
            float rangeEnd;
            GetClipAdjustRange(clip, out clipLength, out rangeStart, out rangeEnd);

            bool speedChanged = Mathf.Abs(Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f) - 1f) > 0.001f;
            bool rangeChanged = rangeStart > 0.001f || Mathf.Abs(rangeEnd - clipLength) > 0.001f;
            // [QC][Invariant:loop_baseline]
            // 비교 대상은 상수가 아니라 소스 clip 자신의 loop 설정(clipLoopOriginal)이다. `true`와 비교하던 때에는
            // ON 방향에 대해 "변경 없음"을 보고했고 그래서 Steps.cs가 clip을 쓰지 않은 채 6번을 완료로 칠했으며,
            // 원래 반복하지 않는 clip에 대해서는 있지도 않은 변경을 보고했다.
            bool loopChanged = clipLoop != clipLoopOriginal;
            return speedChanged || rangeChanged || loopChanged;
        }

        private ClipEditSettings BuildClipEditSettings(AnimationClip sourceClip)
        {
            float clipLength;
            float rangeStart;
            float rangeEnd;
            GetClipAdjustRange(sourceClip, out clipLength, out rangeStart, out rangeEnd);

            return new ClipEditSettings(
                Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f),
                rangeStart,
                rangeEnd,
                clipLoop);
        }

        /// <summary>
        /// 지금 물려 있는 clip을 clip 편집의 SOURCE로 쓸 수 있는지. 결과를 쓰는 DESTINATION은 언제나
        /// ClipEditRoot이고 여기서 협상하지 않는다 - SaveClipAdjustToCurrentClip 참조.
        /// </summary>
        private bool CanEditAnimationClip(AnimationClip clip, out string reason)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "Play 중에는 .anim 파일을 만들거나 LOADER에 연결하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return false;
            }

            if (clip == null)
            {
                reason = "LOADER 동작이 비어 있습니다. 먼저 2번에서 동작을 고르고 '2번 적용 / 이 동작 쓰기'를 누르세요.";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                reason = "현재 동작은 프로젝트 안의 .anim 파일이 아닙니다.";
                return false;
            }

            if (IsPackageOrPackageCachePath(path))
            {
                reason = "현재 동작은 원본 package 동작입니다. 먼저 2번에서 '2번 적용 / 이 동작 쓰기'로 복사본을 만드세요.";
                return false;
            }

            // SOURCE 검사: 헬퍼가 소유한 두 루트 모두 자격이 있으므로 CustomMotionRoot 아래의 Blender / 임포트
            // 모션도 편집할 수 있다. 저장 결과는 그래도 ClipEditRoot로만 간다.
            if (!IsClipEditEligiblePath(path))
            {
                reason = "편집할 동작은 " + AnimationCopyRoot + " 또는 " + CustomMotionRoot
                    + " 아래에 있어야 합니다. 먼저 2번에서 '2번 적용 / 이 동작 쓰기'로 복사본을 만드세요. (저장 결과는 "
                    + ClipEditRoot + " 아래에 새 파일로 만들어집니다.)";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private struct ClipEditSettings
        {
            public readonly float Speed;
            public readonly float RangeStart;
            public readonly float RangeEnd;
            public readonly bool Loop;

            public ClipEditSettings(float speed, float rangeStart, float rangeEnd, bool loop)
            {
                Speed = Mathf.Clamp(speed, 0.25f, 2f);
                RangeStart = Mathf.Max(0f, rangeStart);
                RangeEnd = Mathf.Max(RangeStart + 0.01f, rangeEnd);
                Loop = loop;
            }
        }

        private struct ClipEditResult
        {
            public readonly bool Success;
            public readonly AnimationClip Clip;
            public readonly string Path;
            public readonly string Message;
            public readonly string WarningSummary;
            public readonly int ModifiedCurveCount;

            private ClipEditResult(bool success, AnimationClip clip, string path, string message, string warningSummary, int modifiedCurveCount)
            {
                Success = success;
                Clip = clip;
                Path = path;
                Message = message;
                WarningSummary = warningSummary;
                ModifiedCurveCount = modifiedCurveCount;
            }

            public static ClipEditResult Fail(string message)
            {
                return new ClipEditResult(false, null, string.Empty, message, string.Empty, 0);
            }

            public static ClipEditResult Ok(AnimationClip clip, string path, string warningSummary, int modifiedCurveCount)
            {
                return new ClipEditResult(true, clip, path, string.Empty, warningSummary, modifiedCurveCount);
            }
        }

        /// <summary>
        /// ClipEditUtility 안이 아니라 바깥 클래스에 두는 이유는 라이브 미리보기 루프도 이 함수가 필요하기
        /// 때문이다. "이 clip을 반복하게 만든다"의 구현이 둘이 되면 언젠가 반드시 서로 어긋난다.
        /// </summary>
        private static void ApplyClipLoopSetting(AnimationClip clip, bool loop)
        {
            if (clip == null)
            {
                return;
            }

            // [QC][UnitySerialization]
            // 임포트된 .anim의 loop 상태에는 wrapMode만으로 충분하지 않다. Unity가 읽는 Project 설정은 m_LoopTime이다.
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Default;

            // m_AnimationClipSettings/m_LoopTime은 GetClipLoopTime이
            // AnimationUtility.GetAnimationClipSettings().loopTime으로 되읽는 바로 그 필드라서, 씨앗 → 비교 →
            // 저장의 왕복이 일관되게 돈다. AnimationUtility.SetAnimationClipSettings를 쓰지 않고 프로퍼티를 하나씩
            // 쓰는 것은 의도된 것이다. 그 API는 설정 객체 전체를 - m_StartTime / m_StopTime 까지 - 되밀어 넣는데,
            // 지금 이 clip은 ApplyClipTiming이 방금 커브를 되타이밍한 상태다.
            SerializedObject serializedClip = new SerializedObject(clip);
            SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
            SerializedProperty loopTime = settings == null ? null : settings.FindPropertyRelative("m_LoopTime");
            if (loopTime != null)
            {
                loopTime.boolValue = loop;
                serializedClip.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                // loop 플래그를 말없이 그냥 두는 것이 바로 6번이 바뀌지 않은 파일을 성공으로 보고하게 만든
                // 원인이었다. 그래서 예상 밖의 직렬화 형태는 삼키지 말고 눈에 보이게 해야 한다.
                Debug.LogWarning("ZEPETO Studio Helper: m_AnimationClipSettings/m_LoopTime was not found on "
                    + clip.name + ", so its Loop Time was not saved.");
            }
        }

        private static class ClipEditUtility
        {
            public static ClipEditResult CreateClipAdjustedPreviewClip(AnimationClip sourceClip, ClipEditSettings settings, string destinationPath)
            {
                if (sourceClip == null)
                {
                    return ClipEditResult.Fail("Source AnimationClip is empty.");
                }

                string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return ClipEditResult.Fail("Could not resolve source AnimationClip path.");
                }

                string outputRoot = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrEmpty(outputRoot))
                {
                    return ClipEditResult.Fail("Could not resolve preview output folder.");
                }

                outputRoot = outputRoot.Replace('\\', '/');
                // [QC][Invariant:asset_root]
                // 생성되는 미리보기/편집 clip은 전부 Assets/ZepetoHelper 아래에 머문다. package/cache asset은
                // 그대로 두어야 하기 때문이다. EnsureOutputFolder가 "Assets"까지 거슬러 올라가며 없는 단계를
                // 모두 만들므로 상위 폴더를 따로 만들어 둘 필요가 없다.
                if (!EnsureOutputFolder(outputRoot))
                {
                    return ClipEditResult.Fail("Could not create the preview output folder: " + outputRoot);
                }

                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath) != null)
                {
                    AssetDatabase.DeleteAsset(destinationPath);
                }

                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    return ClipEditResult.Fail("Could not copy animation to " + destinationPath + ".");
                }

                AssetDatabase.ImportAsset(destinationPath);
                AnimationClip previewClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
                if (previewClip == null)
                {
                    return ClipEditResult.Fail("Preview clip could not be loaded: " + destinationPath);
                }

                string warningSummary;
                int modifiedCurveCount = ApplyClipTiming(previewClip, settings, out warningSummary);
                ApplyClipLoopSetting(previewClip, settings.Loop);
                EditorUtility.SetDirty(previewClip);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(destinationPath);
                return ClipEditResult.Ok(previewClip, destinationPath, warningSummary, modifiedCurveCount);
            }

            public static ClipEditResult CreateClipAdjustedClip(AnimationClip sourceClip, ClipEditSettings settings, string outputRoot)
            {
                if (sourceClip == null)
                {
                    return ClipEditResult.Fail("Source AnimationClip is empty.");
                }

                string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return ClipEditResult.Fail("Could not resolve source AnimationClip path.");
                }

                if (!EnsureOutputFolder(outputRoot))
                {
                    return ClipEditResult.Fail("Could not create the clip edit output folder: " + outputRoot);
                }

                // [QC][Invariant:copy_before_retime]
                // 저장은 새 clip 경로를 먼저 만든다. 되타이밍은 그 사본이 임포트된 뒤에만 적용한다.
                string destinationPath = CreateNextEditPath(sourceClip.name, outputRoot, "clipedit");
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    return ClipEditResult.Fail("Could not copy animation to " + destinationPath + ".");
                }

                AssetDatabase.ImportAsset(destinationPath);
                AnimationClip editedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
                if (editedClip == null)
                {
                    return ClipEditResult.Fail("Copied clip edit could not be loaded: " + destinationPath);
                }

                string warningSummary;
                int modifiedCurveCount = ApplyClipTiming(editedClip, settings, out warningSummary);
                ApplyClipLoopSetting(editedClip, settings.Loop);
                EditorUtility.SetDirty(editedClip);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(destinationPath);

                return ClipEditResult.Ok(editedClip, destinationPath, warningSummary, modifiedCurveCount);
            }

            private static int ApplyClipTiming(AnimationClip clip, ClipEditSettings settings, out string warningSummary)
            {
                // [QA][Expected]
                // 숫자 커브와 오브젝트 참조 커브를 모두 되타이밍한다. 커브를 하나도 찾지 못하면 호출자가 경고를
                // 띄운다. 저장된 clip이 바뀌었다고 말없이 주장하지 않기 위해서다.
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);
                int modifiedCurveCount = 0;

                for (int i = 0; i < curveBindings.Length; i++)
                {
                    EditorCurveBinding binding = curveBindings[i];
                    AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (sourceCurve == null)
                    {
                        continue;
                    }

                    AnimationCurve retimedCurve = RetimingCurve(sourceCurve, settings);
                    AnimationUtility.SetEditorCurve(clip, binding, retimedCurve);
                    modifiedCurveCount++;
                }

                EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                for (int i = 0; i < objectBindings.Length; i++)
                {
                    EditorCurveBinding binding = objectBindings[i];
                    ObjectReferenceKeyframe[] sourceKeys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (sourceKeys == null || sourceKeys.Length == 0)
                    {
                        continue;
                    }

                    AnimationUtility.SetObjectReferenceCurve(clip, binding, RetimingObjectKeys(sourceKeys, settings));
                    modifiedCurveCount++;
                }

                warningSummary = modifiedCurveCount == 0
                    ? "No animation curves were found to retime."
                    : string.Empty;
                return modifiedCurveCount;
            }

            private static AnimationCurve RetimingCurve(AnimationCurve sourceCurve, ClipEditSettings settings)
            {
                // [QC][Invariant:time_range]
                // 저장되는 clip은 t=0에서 시작하도록 정규화되고 배속이 반영된 길이를 갖는다.
                // 잘라낸 clip이 결정적인 첫/마지막 포즈를 갖도록 경계 키를 끼워 넣는다.
                //
                // 되타이밍은 네 가지를 동시에 지켜야 한다:
                //   · rangeStart를 t=0으로 옮긴다 - 저장된 clip은 언제나 0에서 시작한다
                //   · 시간을 speed로 나눈다 - 2배속이면 길이가 절반이 된다
                //   · in/out 탄젠트에는 speed를 곱한다. 탄젠트는 값을 시간으로 나눈 기울기라서, 시간만 줄이고
                //     그대로 두면 곡선의 모양 자체가 바뀐다(가속·감속이 뭉개진다)
                //   · 양 끝 키는 Evaluate()로 합성해 넣는다. 잘라낸 지점에 원래 키가 있으리라는 보장이 없고,
                //     없으면 첫/마지막 포즈가 무엇인지 정해지지 않는다
                float rangeStart = settings.RangeStart;
                float rangeEnd = Mathf.Max(settings.RangeStart + 0.01f, settings.RangeEnd);
                float speed = Mathf.Max(0.01f, settings.Speed);
                float outputDuration = Mathf.Max(0.01f, (rangeEnd - rangeStart) / speed);
                List<Keyframe> keys = new List<Keyframe>();

                AddRetimedKey(keys, new Keyframe(0f, sourceCurve.Evaluate(rangeStart)));
                Keyframe[] sourceKeys = sourceCurve.keys;
                for (int i = 0; i < sourceKeys.Length; i++)
                {
                    Keyframe sourceKey = sourceKeys[i];
                    if (sourceKey.time <= rangeStart || sourceKey.time >= rangeEnd)
                    {
                        continue;
                    }

                    Keyframe key = sourceKey;
                    key.time = (sourceKey.time - rangeStart) / speed;
                    key.inTangent *= speed;
                    key.outTangent *= speed;
                    AddRetimedKey(keys, key);
                }

                AddRetimedKey(keys, new Keyframe(outputDuration, sourceCurve.Evaluate(rangeEnd)));

                // 소스 키가 시간순이라는 보장이 없고, 정렬되지 않은 키 배열은 잘못 평가된다.
                keys.Sort(CompareKeyframeTime);

                AnimationCurve retimedCurve = new AnimationCurve(keys.ToArray());
                retimedCurve.preWrapMode = sourceCurve.preWrapMode;
                retimedCurve.postWrapMode = settings.Loop ? WrapMode.Loop : sourceCurve.postWrapMode;
                return retimedCurve;
            }

            private static ObjectReferenceKeyframe[] RetimingObjectKeys(ObjectReferenceKeyframe[] sourceKeys, ClipEditSettings settings)
            {
                // 오브젝트 참조 키에는 탄젠트도 보간도 없다. 그래서 시간만 옮기면 되고, 경계에서 합성할 값도
                // 없으므로 범위 밖의 키는 그냥 버린다.
                float rangeStart = settings.RangeStart;
                float rangeEnd = Mathf.Max(settings.RangeStart + 0.01f, settings.RangeEnd);
                float speed = Mathf.Max(0.01f, settings.Speed);
                List<ObjectReferenceKeyframe> keys = new List<ObjectReferenceKeyframe>();

                for (int i = 0; i < sourceKeys.Length; i++)
                {
                    ObjectReferenceKeyframe sourceKey = sourceKeys[i];
                    if (sourceKey.time < rangeStart || sourceKey.time > rangeEnd)
                    {
                        continue;
                    }

                    ObjectReferenceKeyframe key = sourceKey;
                    key.time = (sourceKey.time - rangeStart) / speed;
                    keys.Add(key);
                }

                return keys.ToArray();
            }

            private static int CompareKeyframeTime(Keyframe left, Keyframe right)
            {
                return left.time.CompareTo(right.time);
            }

            private static void AddRetimedKey(List<Keyframe> keys, Keyframe key)
            {
                // 0.0001f 안에 들어오는 키는 같은 키로 보고 덮어쓴다. 합성한 경계 키가 원래 있던 키와 겹쳐서
                // 같은 시각에 키가 둘 생기는 것을 막는 장치다.
                for (int i = 0; i < keys.Count; i++)
                {
                    if (Mathf.Abs(keys[i].time - key.time) <= 0.0001f)
                    {
                        keys[i] = key;
                        return;
                    }
                }

                keys.Add(key);
            }

            /// <summary>
            /// Assets 아래에서 빠진 폴더 사슬을 전부 만들고, 만든 뒤 그 폴더가 정말 있는지를 보고한다.
            /// </summary>
            /// <remarks>
            /// ClipPreviewRoot("Assets/ZepetoHelper/Animations/Preview")는 평소 비어 있어서, 저장소에는 .meta
            /// 파일로만 존재한다 - git은 빈 디렉터리를 추적하지 않고 패키지 export도 그것을 떨어뜨린다. 그래서
            /// 새로 clone한 사본에는 Preview 폴더가 없는데, 이 메서드의 예전 판은 한 단계만 만들고 멈춘 데다
            /// 결과를 돌려주지도 않아서, 그 실패가 한참 뒤에 CopyAsset의 뭉뚱그린 "Could not copy animation to …"
            /// 로만 드러났다. 이제는 호출자가 진짜 이유를 들고 실패한다.
            /// </remarks>
            private static bool EnsureOutputFolder(string outputRoot)
            {
                if (string.IsNullOrEmpty(outputRoot))
                {
                    return false;
                }

                outputRoot = outputRoot.Replace('\\', '/').TrimEnd('/');
                if (AssetDatabase.IsValidFolder(outputRoot))
                {
                    return true;
                }

                string parent = Path.GetDirectoryName(outputRoot);
                string child = Path.GetFileName(outputRoot);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                {
                    return false;
                }

                parent = parent.Replace('\\', '/');
                // 먼저 재귀한다: AssetDatabase.CreateFolder는 부모가 아직 없으면 말없이 실패한다.
                // "Assets" 자체는 언제나 유효한 폴더이므로 재귀는 거기서 끝난다.
                if (!EnsureOutputFolder(parent))
                {
                    return false;
                }

                EnsureFolder(parent, child);
                return AssetDatabase.IsValidFolder(outputRoot);
            }

            /// <summary>
            /// 다음 clip 편집 결과를 쓸 경로. 1부터 비어 있는 자리를 찾아 새 파일을 만든다.
            /// </summary>
            /// <remarks>
            /// 절대 덮어쓰지 않는 것은 의도된 것이다 - 여기 있는 것은 전부 사용자가 저장을 눌러 만든 결과물이고,
            /// 헬퍼가 말없이 지워도 되는 것이 아니다. 대신 폴더는 한없이 늘어난다: 저장 한 번마다 소스 clip과
            /// 같은 크기의 .anim이 하나 생기고(지금 들어 있는 하나가 14,821,998 바이트), 999개가 다 차면
            /// ClipEdits/ 하나가 ~14.8 GB가 된다. 999를 넘어가면 GenerateUniqueAssetPath가 뒤를 받아 이름 충돌만
            /// 피한다. 정리는 사용자가 폴더를 지우는 방식으로 하며, 결과물은 소스 clip과 6번의 설정으로 언제든
            /// 다시 만들 수 있다.
            /// </remarks>
            private static string CreateNextEditPath(string sourceClipName, string outputRoot, string suffix)
            {
                string safeName = MakeExportSafeFileName(sourceClipName, "clip_edit");
                for (int i = 1; i <= 999; i++)
                {
                    string candidate = outputRoot + "/" + safeName + "_" + suffix + "_" + i.ToString("000") + ".anim";
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(candidate) == null)
                    {
                        return candidate;
                    }
                }

                return AssetDatabase.GenerateUniqueAssetPath(outputRoot + "/" + safeName + "_" + suffix + ".anim");
            }
        }
    }
}
