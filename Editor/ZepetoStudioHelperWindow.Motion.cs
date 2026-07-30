using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Browsing SDK motions and making an editable working copy.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void SelectPackageAnimation(int animationIndex)
        {
            if (animationIndex < 0 || animationIndex >= packageAnimations.Count)
            {
                return;
            }

            selectedAnimationIndex = animationIndex;
            copiedAnimationClip = FindCopiedAnimationForPackage(packageAnimations[animationIndex]);
            SetMotionSelectStageComplete(false);
        }

        private AnimationClip FindCopiedAnimationForPackage(AnimationClip packageClip)
        {
            if (packageClip == null || !AssetDatabase.IsValidFolder(AnimationCopyRoot))
            {
                return null;
            }

            string expectedName = packageClip.name + "_editable";
            string[] guids = AssetDatabase.FindAssets(expectedName + " t:AnimationClip", new[] { AnimationCopyRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(fileName)
                    && fileName.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private bool UseSelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작을 복사하거나 연결하지 않습니다. 먼저 Stop을 누르세요.";
                return false;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "사용할 동작을 먼저 선택하세요.";
                return false;
            }

            // A non-Humanoid clip or a single-frame pose produces an avatar that stands still. Refuse here so
            // the failure is explained at the click instead of discovered in Play.
            string blockReason = GetSelectedMotionBlockReason();
            if (!string.IsNullOrEmpty(blockReason))
            {
                statusMessage = blockReason;
                ValidateState();
                return false;
            }

            AnimationClip existingCopy = FindCopiedAnimationForPackage(selected);
            if (existingCopy != null)
            {
                copiedAnimationClip = existingCopy;
                if (AssignAnimationClip(existingCopy))
                {
                    if (completeStageAfterAssign)
                    {
                        SetMotionSelectStageComplete(true);
                    }

                    SelectAndPing(existingCopy);
                    statusMessage = "복사된 동작을 사용합니다: " + existingCopy.name;
                    return true;
                }

                return false;
            }

            CopySelectedAnimation(completeStageAfterAssign);
            AnimationClip assignedClip = GetAssignedAnimationClip();
            return assignedClip != null && IsClipDerivedFromPackage(assignedClip, selected);
        }

        private void PlaySelectedMotionPreview()
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "Play할 동작을 먼저 선택하세요.";
                return;
            }

            AnimationClip assignedClip = GetAssignedAnimationClip();
            if (!IsClipDerivedFromPackage(assignedClip, selected))
            {
                motionPreviewRestoreClip = assignedClip;
                isTemporarySelectedMotionPreview = true;
                if (!UseSelectedAnimation(false))
                {
                    isTemporarySelectedMotionPreview = false;
                    motionPreviewRestoreClip = null;
                    statusMessage = "선택한 동작을 LOADER에 연결하지 못했습니다. Console과 Validation을 확인하세요.";
                    ValidateState();
                    return;
                }
            }
            else
            {
                isTemporarySelectedMotionPreview = false;
                motionPreviewRestoreClip = null;
            }

            // Internal stage number, not a card number: PreviewStageMotion is 2, and card 2's preview shares it
            // with card 5's live preview because both are "a Play session that borrowed the playback slot".
            RequestPlayMode(PreviewStageMotion);
            if (!EditorApplication.isPlayingOrWillChangePlaymode && isTemporarySelectedMotionPreview)
            {
                RestoreTemporarySelectedMotionPreview();
            }
        }

        private void RestoreTemporarySelectedMotionPreview()
        {
            if (!isTemporarySelectedMotionPreview)
            {
                return;
            }

            AnimationClip restoreClip = motionPreviewRestoreClip;
            isTemporarySelectedMotionPreview = false;
            motionPreviewRestoreClip = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode
                || animationClipProperty == null
                || !TryUpdateSerializedObject(animationClipObject))
            {
                return;
            }

            Undo.RecordObject(animationClipObject.targetObject, "Restore ZEPETO Preview Animation");
            animationClipProperty.objectReferenceValue = restoreClip;
            animationClipObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animationClipObject.targetObject);

            // The playback slot must follow the restored clip, otherwise the avatar keeps performing the preview.
            if (restoreClip != null)
            {
                string overrideMessage;
                ApplyClipToOverrideController(restoreClip, out overrideMessage);
            }

            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            statusMessage = restoreClip == null
                ? "미리보기 종료: 이전 작업 동작이 없어 연결을 비웠습니다."
                : "미리보기 종료: 이전 작업 동작으로 되돌렸습니다. (" + restoreClip.name + ")";
            ValidateState();
        }

        private static bool IsClipDerivedFromPackage(AnimationClip clip, AnimationClip packageClip)
        {
            if (clip == null || packageClip == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase)
                && clip.name.StartsWith(packageClip.name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Describes one selectable motion, including the facts that decide whether it will actually play:
        /// where it came from, whether it is Humanoid, and whether it is a real motion or a single-frame pose.
        /// </summary>
        private struct MotionEntry
        {
            public AnimationClip Clip;
            public bool IsCustom;

            public bool IsHumanoid
            {
                // ZEPETO drives a Humanoid avatar. A generic/legacy clip binds to bone paths that do not exist
                // on the ZEPETO rig, so it silently does nothing.
                get { return Clip != null && Clip.isHumanMotion; }
            }

            public bool IsStaticPose
            {
                get { return Clip != null && Clip.length <= StaticPoseMaxLength; }
            }

            public string BuildLabel()
            {
                if (Clip == null)
                {
                    return " ";
                }

                string label = Clip.name + "  " + Clip.length.ToString("0.0") + "s";
                if (IsCustom)
                {
                    label += "  [내 모션]";
                }

                if (IsStaticPose)
                {
                    label += "  (포즈)";
                }
                else if (!IsHumanoid)
                {
                    label += "  (Humanoid 아님)";
                }

                return MakePopupSafeLabel(label);
            }
        }

        private void LoadPackageAnimations()
        {
            packageAnimations.Clear();
            motionEntries.Clear();

            // SDK motions first, then anything the user authored. Both feed the same picker so a Mixamo or
            // Blender clip is used exactly like a built-in one.
            CollectMotionsFrom(PackageAnimationFolder, false);
            CollectMotionsFrom(CustomMotionRoot, true);

            packageAnimationNames = new string[motionEntries.Count];
            for (int i = 0; i < motionEntries.Count; i++)
            {
                packageAnimations.Add(motionEntries[i].Clip);
                packageAnimationNames[i] = motionEntries[i].BuildLabel();
            }

            if (selectedAnimationIndex < 0)
            {
                selectedAnimationIndex = FindPreferredDefaultMotionIndex();
            }

            if (selectedAnimationIndex >= motionEntries.Count)
            {
                selectedAnimationIndex = motionEntries.Count - 1;
            }
        }

        private void CollectMotionsFrom(string folder, bool isCustom)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder });
            Array.Sort(guids, CompareAnimationGuidByName);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    continue;
                }

                motionEntries.Add(new MotionEntry { Clip = clip, IsCustom = isCustom });
            }
        }

        /// <summary>
        /// Lands on a clip that actually moves. The alphabetically first SDK clip is A_pose, a single frame,
        /// which is the exact state that looks like a broken helper.
        /// </summary>
        private int FindPreferredDefaultMotionIndex()
        {
            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (motionEntries[i].Clip != null
                    && motionEntries[i].Clip.name.Equals(PreferredDefaultAnimationName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (!motionEntries[i].IsStaticPose && motionEntries[i].IsHumanoid)
                {
                    return i;
                }
            }

            return motionEntries.Count > 0 ? 0 : -1;
        }

        private bool TryGetSelectedMotionEntry(out MotionEntry entry)
        {
            if (selectedAnimationIndex >= 0 && selectedAnimationIndex < motionEntries.Count)
            {
                entry = motionEntries[selectedAnimationIndex];
                return true;
            }

            entry = default(MotionEntry);
            return false;
        }

        /// <summary>
        /// Reason the selected clip cannot be used as a working motion, or empty when it is fine.
        /// </summary>
        private string GetSelectedMotionBlockReason()
        {
            MotionEntry entry;
            if (!TryGetSelectedMotionEntry(out entry) || entry.Clip == null)
            {
                return "사용할 동작을 먼저 선택하세요.";
            }

            if (!entry.IsHumanoid)
            {
                return "이 클립은 Humanoid가 아닙니다: " + entry.Clip.name
                    + ". FBX를 고른 뒤 Inspector에서 Rig > Animation Type을 Humanoid로 바꾸고 Apply 하세요. "
                    + "아래 'FBX를 ZEPETO용으로 설정' 버튼으로도 처리할 수 있습니다.";
            }

            if (entry.IsStaticPose)
            {
                return "이 클립은 " + entry.Clip.length.ToString("0.00")
                    + "초짜리 정지 포즈입니다. 그대로 쓰면 아바타가 움직이지 않습니다.";
            }

            return string.Empty;
        }

        private static int CompareAnimationGuidByName(string leftGuid, string rightGuid)
        {
            string left = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(leftGuid));
            string right = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(rightGuid));
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private void FindExistingCopiedAnimation()
        {
            if (!AssetDatabase.IsValidFolder(AnimationCopyRoot))
            {
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                return;
            }

            string expectedName = selected.name + "_editable";
            string[] guids = AssetDatabase.FindAssets(expectedName + " t:AnimationClip", new[] { AnimationCopyRoot });
            if (guids.Length == 0)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            copiedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private AnimationClip GetSelectedPackageAnimation()
        {
            if (selectedAnimationIndex < 0 || selectedAnimationIndex >= packageAnimations.Count)
            {
                return null;
            }

            return packageAnimations[selectedAnimationIndex];
        }

        private void CopySelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작 파일을 복사하거나 LOADER에 연결하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not resolve selected animation path.");
                return;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Animations");

            string destinationPath = AssetDatabase.GenerateUniqueAssetPath(AnimationCopyRoot + "/" + selected.name + "_editable.anim");
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not copy animation from " + sourcePath + " to " + destinationPath);
                return;
            }

            AssetDatabase.ImportAsset(destinationPath);
            copiedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
            bool didAssign = AssignAnimationClip(copiedAnimationClip);
            if (didAssign)
            {
                if (completeStageAfterAssign)
                {
                    SetMotionSelectStageComplete(true);
                }
            }
            SelectAndPing(copiedAnimationClip);
            statusMessage = didAssign
                ? "동작을 복사하고 LOADER에 연결했습니다: " + destinationPath
                : "동작 복사본은 만들었지만 LOADER에 연결하지 못했습니다: " + destinationPath;
            ValidateState();
        }
    }
}
