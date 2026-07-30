using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The checks shown in the Diagnostics list.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
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
            else if (path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip is ready for clip adjust: " + path, MessageType.Info));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip points to a project asset. Clip adjust only supports " + AnimationCopyRoot + ": " + path, MessageType.Warning));
            }
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

            // The override slot is what the avatar actually performs, so report it explicitly. A_pose here means
            // the avatar will stand still no matter which motion the workflow thinks is selected.
            AnimationClip playbackClip = GetPlaybackClip();
            AnimationClip assignedClip = animationClipProperty == null
                ? null
                : animationClipProperty.objectReferenceValue as AnimationClip;

            if (playbackClip == null)
            {
                validationMessages.Add(new ValidationMessage("재생 슬롯이 비어 있습니다. 2번에서 동작을 다시 적용하세요.", MessageType.Warning));
            }
            else if (playbackClip.length <= 0.1f)
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
