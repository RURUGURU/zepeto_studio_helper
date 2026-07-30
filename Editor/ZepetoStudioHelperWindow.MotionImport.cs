using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Turning an external animation FBX (Mixamo download, Blender export) into a clip the ZEPETO avatar can play.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// Applies the import settings the official ZEPETO custom-animation guide requires:
        /// Animation Type = Humanoid, and a root transform setup that keeps the avatar in frame.
        /// Getting these wrong is the single most common reason an imported motion does nothing.
        /// </summary>
        private bool TryConfigureMotionFbx(string assetPath, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(assetPath))
            {
                message = "FBX 파일을 Project 창에서 먼저 선택하세요.";
                return false;
            }

            string extension = Path.GetExtension(assetPath);
            if (!".fbx".Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                message = "FBX 파일이 아닙니다: " + assetPath;
                return false;
            }

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                message = "이 파일의 import 설정을 읽지 못했습니다: " + assetPath;
                return false;
            }

            List<string> changes = new List<string>();

            // [AUDIT][Risk:Major][Scope:humanoid_setup]
            // Unity will not accept animationType and sourceAvatar in one pass: the avatar copy is validated
            // against the already-imported rig, so a combined write silently reverts to CreateFromThisModel.
            // Each stage therefore reimports before the next one reads the importer back.
            if (importer.animationType != ModelImporterAnimationType.Human || !importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimation = true;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changes.Add("Animation Type = Humanoid");

                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                {
                    message = "재임포트 후 import 설정을 다시 읽지 못했습니다: " + assetPath;
                    return false;
                }
            }

            // [AUDIT][Scope:retarget_source]
            // Copying the exported rig's Avatar is attempted, but Unity roots every imported model at the FILE
            // name, so the target skeleton always differs from the source avatar by that root entry and Unity
            // silently reverts to CreateFromThisModel. Measured: rig root 'ZepetoBaseModel' (106 transforms) vs
            // animation root 'ZepetoRig_Wave'. The bone names themselves match exactly, all 103 of them.
            //
            // The fallback is acceptable, but not for the reason an earlier version of this comment claimed.
            // A Humanoid clip stores normalised muscle ANGLES, one world-space body transform and hand/foot IK
            // goals - it cannot carry bone lengths at all (hasTranslationDoF is 0 and the rotation/position/scale
            // curve lists come out empty). So the source avatar does not make proportions "correct"; it only
            // decides how the authored angles are read back. Whichever avatar is used, proportion mismatch shows
            // up at PLAYBACK as foot slide and twist collapse, never as an import error. See DrawPublishGuide.
            Avatar rigAvatar = FindExportedRigAvatar();
            if (rigAvatar != null && importer.sourceAvatar != rigAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = rigAvatar;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null && importer.sourceAvatar == rigAvatar)
                {
                    changes.Add("Avatar = ZEPETO 리그에서 복사 (" + rigAvatar.name + ")");
                }
                else
                {
                    changes.Add("Avatar는 이 FBX에서 생성 (모션 자체의 뼈대를 기준으로 읽습니다)");
                }
            }

            if (importer == null)
            {
                message = "import 설정을 다시 읽지 못했습니다: " + assetPath;
                return false;
            }

            // Bake root motion into the pose so the preview avatar stays where the booth camera is pointing
            // instead of walking out of frame.
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].lockRootRotation = true;
                    clips[i].keepOriginalOrientation = true;
                    clips[i].lockRootHeightY = true;
                    clips[i].keepOriginalPositionY = true;
                    clips[i].lockRootPositionXZ = true;
                    clips[i].keepOriginalPositionXZ = false;
                }

                importer.clipAnimations = clips;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changes.Add("Root Transform 고정 (" + clips.Length + "개 클립)");
            }
            else
            {
                changes.Add("경고: 이 FBX에 애니메이션 클립이 없습니다");
            }

            if (changes.Count == 0)
            {
                message = "이미 ZEPETO용 설정입니다: " + Path.GetFileName(assetPath);
                return true;
            }

            message = Path.GetFileName(assetPath) + " 설정 완료 — " + string.Join(", ", changes.ToArray());
            return true;
        }

        /// <summary>
        /// Copies the clip that lives inside an imported FBX into a standalone .anim under the custom motion
        /// folder. A clip embedded in a model asset is read-only, so speed / trim / loop editing needs a copy.
        /// </summary>
        private bool TryExtractMotionFromFbx(string assetPath, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(assetPath))
            {
                message = "FBX 파일을 Project 창에서 먼저 선택하세요.";
                return false;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            AnimationClip source = null;
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip candidate = assets[i] as AnimationClip;
                // Model importers add a hidden __preview__ clip; ignore it.
                if (candidate != null && (candidate.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    source = candidate;
                    break;
                }
            }

            if (source == null)
            {
                message = "이 FBX 안에서 애니메이션 클립을 찾지 못했습니다. Mixamo에서 'With Skin' 대신 애니메이션이 포함된 상태로 받았는지 확인하세요.";
                return false;
            }

            if (!source.isHumanMotion)
            {
                message = "클립이 Humanoid가 아닙니다. 먼저 'FBX를 ZEPETO용으로 설정'을 누르세요.";
                return false;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Motions");

            string safeName = MakeExportSafeFileName(Path.GetFileNameWithoutExtension(assetPath));
            string destination = AssetDatabase.GenerateUniqueAssetPath(CustomMotionRoot + "/" + safeName + ".anim");

            AnimationClip copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(destination);
            AssetDatabase.CreateAsset(copy, destination);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(destination);

            AnimationClip created = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (created == null)
            {
                message = "클립을 저장하지 못했습니다: " + destination;
                return false;
            }

            LoadPackageAnimations();
            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (motionEntries[i].Clip == created)
                {
                    selectedAnimationIndex = i;
                    break;
                }
            }

            SelectAndPing(created);
            message = "내 모션으로 추가했습니다: " + destination
                + " (" + created.length.ToString("0.00") + "초). 2번 목록에서 바로 고를 수 있습니다.";
            return true;
        }

        private static string GetSelectedFbxPath()
        {
            UnityEngine.Object selection = Selection.activeObject;
            if (selection == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(selection);
            return ".fbx".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase) ? path : string.Empty;
        }

        /// <summary>
        /// Registering an fbx that did not come through the live loop - a Mixamo download, or a Blender export
        /// made before live preview was set up. The live path in step 5 does both of these automatically.
        /// </summary>
        private void DrawManualMotionImportBody()
        {
            string fbxPath = GetSelectedFbxPath();
            DrawStatusRow("선택된 FBX", string.IsNullOrEmpty(fbxPath) ? "없음 - Project 창에서 FBX 선택" : fbxPath);

            bool hasFbx = !string.IsNullOrEmpty(fbxPath) && !EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!hasFbx))
            {
                if (DrawBlueActionButton("1. FBX를 ZEPETO용으로 설정", hasFbx, GUILayout.Height(26f)))
                {
                    string message;
                    TryConfigureMotionFbx(fbxPath, out message);
                    statusMessage = message;
                    ValidateState();
                }

                if (DrawBlueActionButton("2. 내 모션으로 추가", hasFbx, GUILayout.Height(26f)))
                {
                    string message;
                    TryExtractMotionFromFbx(fbxPath, out message);
                    statusMessage = message;
                    ValidateState();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawMiniHelp(
                "Project 창에서 FBX를 클릭한 뒤 1번 → 2번 순서로 누릅니다. "
                + "ZEPETO는 Humanoid 애니메이션만 재생합니다.",
                MessageType.None);
        }
    }
}
