using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Speed / trim / loop editing, written to new .anim copies.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // [AUDIT][Risk:Major][Scope:clip_loop_save]
        // The baseline the Loop toggle is compared against: the SOURCE clip's real m_LoopTime at the moment
        // step 6 first saw that clip. HasClipAdjustInput used to ask "is clipLoop false?" against the hardcoded
        // clipLoop=true default instead, so turning Loop ON for a clip that does not loop counted as "no input",
        // Steps.cs took its no-op branch and painted the step 완료 without ever writing a file.
        //
        // SessionState scope, matching the other ClipAdjust.* keys: the baseline has to survive the domain
        // reload that entering Play is, but a value left over from a previous editor session would describe a
        // clip that is no longer assigned and would make a real change look like none.
        private const string ClipAdjustLoopOriginalSessionKey = "Easy.ZepetoHelper.ClipAdjust.LoopOriginal";

        // Seeded by EnsureClipAdjustDefaults. Initialised to the same value as clipLoop so that before any clip
        // is assigned the two agree and HasClipAdjustInput reports no pending loop change.
        private bool clipLoopOriginal = true;

        private bool PrepareClipAdjustPreviewBeforePlay()
        {
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
            // Preview Play must never mutate the working clip. A temporary asset is assigned to LOADER,
            // and OnPlayModeStateChanged restores restorePath after Play exits.
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

        private void SaveClipAdjustToCurrentClip()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 clip 파일을 저장하지 않습니다. 먼저 Stop을 눌러주세요.";
                ValidateState();
                return;
            }

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
            // Clip adjustment is copy-on-write: package/cache clips and existing working clips are not edited in place.
            // Verification target: new .anim under ClipEditRoot, then LOADER.AnimationClip points to that new asset.
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
            // Seed the toggle from what the source clip actually does, not from a constant. clipLoopOriginal is
            // the baseline HasClipAdjustInput compares against, so it has to be captured from the same clip and
            // at the same moment as the visible clipLoop value.
            clipLoopOriginal = GetClipLoopTime(clip);
            clipLoop = clipLoopOriginal;
            clipTrimStart = 0f;
            clipTrimEnd = Mathf.Max(0.01f, clip.length);
            SaveClipAdjustSessionState(sourcePath);
        }

        /// <summary>
        /// The clip's real Loop Time, read from the imported asset. This is the same m_LoopTime field that
        /// ApplyClipLoopSetting writes, so a read-modify-write round trip cannot drift.
        /// </summary>
        private static bool GetClipLoopTime(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            // AnimationClipSettings is a class, not a struct, so the result is a reference that has to be checked.
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            return settings != null && settings.loopTime;
        }

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
            // The baseline is restored before the UI value, and the clip itself is the fallback for both, so a
            // session that predates the baseline key still gets a truthful "nothing changed yet" answer instead
            // of an assumed default.
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

        private bool HasClipAdjustInput(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            float clipLength = Mathf.Max(0.01f, clip.length);
            float rangeStart = Mathf.Clamp(clipTrimStart, 0f, clipLength);
            float rangeEnd = Mathf.Clamp(clipTrimEnd <= 0f ? clipLength : clipTrimEnd, rangeStart + 0.01f, clipLength);
            bool speedChanged = Mathf.Abs(Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f) - 1f) > 0.001f;
            bool rangeChanged = rangeStart > 0.001f || Mathf.Abs(rangeEnd - clipLength) > 0.001f;
            // [QC][Invariant:loop_baseline]
            // Compared against the source clip's own loop setting (clipLoopOriginal), never against a constant.
            // Comparing against `true` reported "no change" for the ON direction, which let Steps.cs mark step 6
            // 완료 without writing the clip, and reported a phantom change for an already-non-looping clip.
            bool loopChanged = clipLoop != clipLoopOriginal;
            return speedChanged || rangeChanged || loopChanged;
        }

        private ClipEditSettings BuildClipEditSettings(AnimationClip sourceClip)
        {
            float clipLength = sourceClip == null ? 0.01f : Mathf.Max(0.01f, sourceClip.length);
            float rangeStart = Mathf.Clamp(clipTrimStart, 0f, clipLength);
            float rangeEnd = Mathf.Clamp(clipTrimEnd <= 0f ? clipLength : clipTrimEnd, rangeStart + 0.01f, clipLength);
            return new ClipEditSettings(
                Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f),
                rangeStart,
                rangeEnd,
                clipLoop);
        }

        /// <summary>
        /// Whether the currently assigned clip may act as the SOURCE of a clip edit. The DESTINATION is always
        /// ClipEditRoot and is not negotiated here - see SaveClipAdjustToCurrentClip.
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

            // SOURCE check: both helper-owned roots are eligible, so Blender / import motions under
            // CustomMotionRoot are editable too. The saved result still goes to ClipEditRoot only.
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
        /// Lives on the outer class rather than inside ClipEditUtility because the live-preview loop needs it
        /// too, and two implementations of "make this clip loop" would inevitably drift apart.
        /// </summary>
        private static void ApplyClipLoopSetting(AnimationClip clip, bool loop)
        {
            if (clip == null)
            {
                return;
            }

            // [QC][UnitySerialization]
            // wrapMode alone is not enough for imported .anim loop state; m_LoopTime is the Project setting Unity reads.
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Default;

            // m_AnimationClipSettings/m_LoopTime is exactly the field GetClipLoopTime reads back through
            // AnimationUtility.GetAnimationClipSettings().loopTime, so seed -> compare -> save round trips
            // consistently. Written one property at a time on purpose rather than through
            // AnimationUtility.SetAnimationClipSettings, which would push the whole settings object back -
            // including m_StartTime / m_StopTime - over a clip whose curves ApplyClipTiming has just retimed.
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
                // Silently leaving the loop flag alone is what made step 6 report success on an unchanged file,
                // so an unexpected serialization shape has to be visible rather than swallowed.
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
                // All generated preview/edit clips stay under Assets/ZepetoHelper so package/cache assets remain immutable.
                EnsureFolder("Assets", "ZepetoHelper");
                EnsureFolder("Assets/ZepetoHelper", "Animations");
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

                EnsureFolder("Assets", "ZepetoHelper");
                EnsureFolder("Assets/ZepetoHelper", "Animations");
                if (!EnsureOutputFolder(outputRoot))
                {
                    return ClipEditResult.Fail("Could not create the clip edit output folder: " + outputRoot);
                }

                // [QC][Invariant:copy_before_retime]
                // Saving creates a new clip path first; retiming is applied only after the copy is imported.
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
                // Numeric and object-reference curves are both retimed. If no curves are found, the caller surfaces
                // a warning instead of silently claiming the saved clip changed.
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
                // The saved clip is normalized to start at t=0 and uses speed-adjusted duration.
                // Boundary keys are inserted so trimmed clips keep deterministic first/last poses.
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

                // Source keys are not guaranteed to be time-ordered, and an unsorted key array evaluates incorrectly.
                keys.Sort(CompareKeyframeTime);

                AnimationCurve retimedCurve = new AnimationCurve(keys.ToArray());
                retimedCurve.preWrapMode = sourceCurve.preWrapMode;
                retimedCurve.postWrapMode = settings.Loop ? WrapMode.Loop : sourceCurve.postWrapMode;
                return retimedCurve;
            }

            private static ObjectReferenceKeyframe[] RetimingObjectKeys(ObjectReferenceKeyframe[] sourceKeys, ClipEditSettings settings)
            {
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
            /// Creates the whole missing folder chain under Assets and reports whether the folder is really there
            /// afterwards.
            /// </summary>
            /// <remarks>
            /// ClipPreviewRoot ("Assets/ZepetoHelper/Animations/Preview") is normally EMPTY, so it exists in the
            /// repository only as a .meta file - git does not track empty directories and package export drops
            /// them. A fresh clone therefore has no Preview folder, and the old version of this method both
            /// stopped after a single level and returned no result, so the failure surfaced later as the generic
            /// "Could not copy animation to ..." from CopyAsset. Callers now fail with the real reason instead.
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
                // Recurse first: AssetDatabase.CreateFolder silently fails when the parent does not exist yet.
                // "Assets" itself is always a valid folder, so the recursion terminates there.
                if (!EnsureOutputFolder(parent))
                {
                    return false;
                }

                EnsureFolder(parent, child);
                return AssetDatabase.IsValidFolder(outputRoot);
            }

            private static string CreateNextEditPath(string sourceClipName, string outputRoot, string suffix)
            {
                string safeName = MakeSafeFileName(sourceClipName);
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

            private static string MakeSafeFileName(string value)
            {
                string safeName = string.IsNullOrEmpty(value) ? "clip_edit" : value.Trim();
                char[] invalidChars = Path.GetInvalidFileNameChars();
                for (int i = 0; i < invalidChars.Length; i++)
                {
                    safeName = safeName.Replace(invalidChars[i], '_');
                }

                return string.IsNullOrEmpty(safeName) ? "clip_edit" : safeName;
            }
        }
    }
}
