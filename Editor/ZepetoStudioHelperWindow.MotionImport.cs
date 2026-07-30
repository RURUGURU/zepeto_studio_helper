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
            // silently reverts to CreateFromThisModel. Re-measured from the fbx node tables themselves, because
            // the numbers this comment used to carry (106 vs 103 matching) are not what the files contain:
            // ZepetoBaseModel.fbx has 106 Model nodes topped by 'ZepetoBaseModel'; ZepetoRig_Wave.fbx has two
            // root-level Model nodes - 'body' (Mesh) and 'hips' (Null) - and Unity adds the file-named
            // 'ZepetoRig_Wave' root, so both skeletons are 106 entries and 105 of the 106 names match, the root
            // being the only one that differs. Blender DOES re-export the armature object; that 'hips' Null is
            // the node carrying Lcl Scaling (0.01, 0.01, 0.01), which is the source of every per-bone
            // "position error" warning in ZepetoRig_Wave.fbx.meta (a single global 174752x factor, identical
            // across all 64 warned bones - not a skeletal corruption). Every .fbx.meta under
            // Assets/CustomMotions confirms the revert: avatarSetup: 1 is CreateFromThisModel, never 2.
            //
            // The fallback is acceptable, but not for the reason an earlier version of this comment claimed.
            // A Humanoid clip stores normalised muscle ANGLES, one world-space body transform and hand/foot IK
            // goals - it cannot carry bone lengths at all (hasTranslationDoF is 0 and the rotation/position/scale
            // curve lists come out empty). So the source avatar does not make proportions "correct"; it only
            // decides how the authored angles are read back. Whichever avatar is used, proportion mismatch shows
            // up at PLAYBACK as foot slide and twist collapse, never as an import error. See DrawPublishGuide.
            // [AUDIT][Risk:Critical][Scope:avatar_poisoning]
            // The copy must be GATED, not just attempted. Assigning sourceAvatar writes the ZEPETO
            // humanDescription into the target's .meta, and if the target's skeleton does not carry those bone
            // names the asset is poisoned: every later reimport fails with
            //   "Avatar creation failed: Transform 'hips' for human bone 'Hips' not found"
            // because the bad map is now part of the asset, not of this run. Two files in Assets/CustomMotions
            // (Wave_Hello.fbx, AddonSmokeTest.fbx) were in exactly that state - they were importing fine until
            // this line ran against their 21-bone generic skeletons - and hand-deleting their .meta files was
            // the only way out. Pressing this button on any mismatched skeleton re-creates that state, so
            // gating is only half the job: the leftover map is CLEARED below too, because this button is the
            // only repair path a user has.
            //
            // Skipping the copy is NOT a downgrade. A Humanoid clip carries normalised muscle angles and no bone
            // names at all, so a clip extracted on the FBX's own avatar retargets onto the ZEPETO avatar
            // regardless - Wave_Hello.anim is the proof: 0 of 55 ZEPETO bone names, and still a valid 130-curve
            // Humanoid clip. Generic/Mixamo-style names (Hips, Spine, LeftArm) are exactly Unity's auto-mapper
            // vocabulary, so those FBX files get a working avatar on their own. It is the ZEPETO names that
            // Unity cannot auto-map, which is why the hand-authored humanDescription exists for the rig.
            Avatar rigAvatar = FindExportedRigAvatar();

            // Held outside the block below: when the post-import gate fails, the reason the copy was refused IS
            // the diagnosis, and the gate cannot recompute it.
            string copySkipReason = string.Empty;

            // Deliberately not "&& importer.sourceAvatar != rigAvatar". Unity reverts a rejected copy to
            // CreateFromThisModel and leaves the copied map behind in the .meta, so sourceAvatar reads back null
            // on exactly the assets that need repairing - that condition skipped the block for them and made the
            // damage permanent.
            if (rigAvatar != null)
            {
                if (!CanCopyRigAvatarTo(assetPath, rigAvatar, out copySkipReason))
                {
                    string clearedReason;
                    if (NeedsForeignAvatarMapCleared(importer, assetPath, out clearedReason))
                    {
                        ClearForeignAvatarMap(importer);
                        changes.Add("다른 리그의 Avatar 뼈 매핑을 지웠습니다 - 이제 이 FBX가 자기 뼈대로 Avatar를 "
                            + "만듭니다 (" + clearedReason + ")");

                        importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                    }
                    else
                    {
                        changes.Add("Avatar는 이 FBX에서 생성 - " + copySkipReason);
                    }
                }
                else if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                    || importer.sourceAvatar != rigAvatar)
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

            // [AUDIT][Risk:Major][Scope:humanoid_setup]
            // A Humanoid import that cannot build an Avatar is not an exception and not a false return anywhere -
            // Unity records the reason on the importer and hands back a model with no Avatar at all. Reporting
            // the settings we wrote as success therefore produced motion entries that can never play. Two of the
            // three fbx files in Assets/CustomMotions used to sit in exactly that state: their bones are
            // Mixamo-named (HumanoidRig/Hips/Spine/LeftArm, 22 nodes) while the humanDescription this pipeline
            // put on them named ZEPETO bones, and the .meta recorded
            // rigImportErrors: "Avatar creation failed:\n\tTransform 'hips' for human bone 'Hips' not found".
            // The clear above now repairs that, but the gate stays: plenty of other skeletons build no Avatar and
            // no amount of clearing helps them. Remapping bone names is deliberately out of scope; this check
            // only has to stop lying about the result. Both halves are needed - the recorded text names the
            // cause, the Avatar says whether the clip can actually be retargeted.
            //
            // The importer is read back one more time first: the clip pass above ends in SaveAndReimport, which
            // invalidates the C# wrapper exactly the way the two earlier stages already guard against. A null
            // here only costs the error text, not the check - the Avatar test is what decides.
            importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            string rigImportError = GetRigImportErrorText(importer);
            Avatar producedAvatar = FindImportedModelAvatar(assetPath);
            bool avatarUsable = producedAvatar != null && producedAvatar.isValid && producedAvatar.isHuman;
            if (!string.IsNullOrEmpty(rigImportError) || !avatarUsable)
            {
                string detail = string.IsNullOrEmpty(rigImportError)
                    ? (producedAvatar == null
                        ? "Avatar가 만들어지지 않았습니다"
                        : "Avatar 상태: isValid=" + producedAvatar.isValid + ", isHuman=" + producedAvatar.isHuman)
                    : "Unity가 기록한 원인: " + (rigImportError.Length > 300
                        ? rigImportError.Substring(0, 300) + " …"
                        : rigImportError);

                // [AUDIT][Risk:Major][Scope:humanoid_setup]
                // The cause is read off the state, never assumed. Asserting "뼈 이름이 ZEPETO와 다릅니다" was
                // wrong for the most likely way to get here: with no exported rig there is no source Avatar, so
                // nothing was copied and the FBX was left to build its own - and a ZEPETO-named skeleton cannot,
                // because those names are not in Unity's auto-mapper vocabulary. When a rig DOES exist, the
                // reason the copy was refused plus the settings that were actually written are the diagnosis, and
                // both used to be discarded.
                if (rigAvatar == null)
                {
                    message = Path.GetFileName(assetPath)
                        + ": Humanoid Avatar를 만들지 못했습니다. 3번(ZEPETO 리그 내보내기)을 아직 하지 않아서 "
                        + "리타게팅 원본 Avatar가 없습니다. 이 FBX가 ZEPETO 리그 위에서 만든 동작이라면 "
                        + "뼈 이름(hips/spine/upperArm_L)을 Unity가 스스로 매핑하지 못하므로, 내보낸 리그의 "
                        + "Avatar를 복사하는 것 말고는 Avatar를 만들 방법이 없습니다. "
                        + "3번을 먼저 누른 뒤 이 버튼을 다시 누르세요. " + detail;
                }
                else
                {
                    message = Path.GetFileName(assetPath)
                        + ": Humanoid Avatar를 만들지 못했습니다. 이 FBX는 ZEPETO 아바타로 재생할 수 없습니다. "
                        + "뼈 이름을 자동으로 바꿔주는 기능은 없습니다. 4번의 Blender 애드온으로 ZEPETO 리그 위에 "
                        + "동작을 만들어 내보내는 방법만 지원합니다."
                        + (string.IsNullOrEmpty(copySkipReason)
                            ? string.Empty
                            : " Avatar 복사 판정: " + copySkipReason + ".")
                        + (changes.Count == 0
                            ? string.Empty
                            : " 적용한 설정: " + string.Join(", ", changes.ToArray()) + ".")
                        + " " + detail;
                }

                return false;
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
        /// The rig import error Unity recorded on a model importer, flattened onto one line so it fits a HelpBox.
        /// Empty string when the import reported none.
        ///
        /// ModelImporter does not expose this on its public surface. m_RigImportErrors is the serialized name
        /// Unity's own Rig inspector binds to and is what the .meta writes out as rigImportErrors; the name scan
        /// after it is there so a renamed field degrades to "nothing reported" instead of throwing. Next(true) is
        /// used rather than NextVisible because this property is hidden from the default inspector.
        ///
        /// The propertyType tests are load-bearing: stringValue throws on a property of any other type, and both
        /// lookups are by name. The `using` matters because ConfigureMotionFolderForLivePreview runs this once per
        /// fbx in the folder, and each SerializedObject holds a native handle until it is disposed.
        /// </summary>
        private static string GetRigImportErrorText(ModelImporter importer)
        {
            if (importer == null)
            {
                return string.Empty;
            }

            using (SerializedObject serialized = new SerializedObject(importer))
            {
                SerializedProperty direct = serialized.FindProperty("m_RigImportErrors");
                if (direct != null && direct.propertyType == SerializedPropertyType.String)
                {
                    return FlattenImporterMessage(direct.stringValue);
                }

                SerializedProperty iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.String
                        && iterator.name.IndexOf("RigImportError", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return FlattenImporterMessage(iterator.stringValue);
                    }
                }
            }

            return string.Empty;
        }

        private static string FlattenImporterMessage(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }

        /// <summary>
        /// The Avatar Unity actually produced for a model asset, or null when avatar creation failed.
        ///
        /// Unlike FindExportedRigAvatar this does not filter on isHuman: the caller has to tell "no Avatar at
        /// all" apart from "an Avatar that is not Humanoid", because those two need different words on screen.
        /// </summary>
        private static Avatar FindImportedModelAvatar(string assetPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                Avatar avatar = assets[i] as Avatar;
                if (avatar != null)
                {
                    return avatar;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether the rig Avatar's humanDescription can be copied onto this model without breaking it.
        ///
        /// Unity does not validate this for us: assigning sourceAvatar writes the source humanDescription into
        /// the target's .meta whatever the target's skeleton looks like, and a map naming bones the target does
        /// not have makes avatar creation fail on that asset from then on. So the bone names have to be checked
        /// BEFORE the assignment - afterwards is too late to PREVENT it. Repairing a .meta that already holds a
        /// foreign map is the other half of the job and lives in NeedsForeignAvatarMapCleared.
        ///
        /// Only bone presence is checked, not the hierarchy: Unity roots every imported model at the file name,
        /// so the two skeletons always differ by that one root entry and an exact match is never achievable.
        /// </summary>
        private static bool CanCopyRigAvatarTo(string assetPath, Avatar sourceAvatar, out string reason)
        {
            reason = string.Empty;

            HumanBone[] humanBones = sourceAvatar == null
                ? null
                : sourceAvatar.humanDescription.human;
            if (humanBones == null || humanBones.Length == 0)
            {
                reason = "리그 Avatar에 사람 뼈 매핑이 없습니다";
                return false;
            }

            HashSet<string> modelBoneNames = CollectModelBoneNames(assetPath);
            if (modelBoneNames == null)
            {
                reason = "임포트된 모델을 읽지 못했습니다";
                return false;
            }

            int missingCount;
            string firstMissingName;
            CountMissingHumanBones(humanBones, modelBoneNames, out missingCount, out firstMissingName);

            if (missingCount > 0)
            {
                reason = "이 FBX의 뼈 이름이 ZEPETO 리그와 달라 Avatar를 복사하지 않았습니다 ("
                    + humanBones.Length + "개 중 " + missingCount + "개 없음, 예: " + firstMissingName + ")";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Every transform name in an imported model, or null when the model itself could not be read. Shared by
        /// the copy gate and the un-poison check so "which bones does this FBX actually have" has one definition.
        /// </summary>
        private static HashSet<string> CollectModelBoneNames(string assetPath)
        {
            GameObject model = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            if (model == null)
            {
                return null;
            }

            HashSet<string> modelBoneNames = new HashSet<string>(StringComparer.Ordinal);
            Transform[] modelTransforms = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < modelTransforms.Length; i++)
            {
                modelBoneNames.Add(modelTransforms[i].name);
            }

            return modelBoneNames;
        }

        /// <summary>
        /// How many of a human map's bone names the model does not have, plus the first one for the message.
        /// An empty boneName is an unmapped optional bone, not a missing one.
        /// </summary>
        private static void CountMissingHumanBones(
            HumanBone[] humanBones,
            HashSet<string> modelBoneNames,
            out int missingCount,
            out string firstMissingName)
        {
            missingCount = 0;
            firstMissingName = string.Empty;

            for (int i = 0; i < humanBones.Length; i++)
            {
                string boneName = humanBones[i].boneName;
                if (string.IsNullOrEmpty(boneName) || modelBoneNames.Contains(boneName))
                {
                    continue;
                }

                missingCount++;
                if (firstMissingName.Length == 0)
                {
                    firstMissingName = boneName;
                }
            }
        }

        /// <summary>
        /// Whether this asset is carrying an Avatar setup that has to be wiped before it can import again.
        ///
        /// [AUDIT][Risk:Critical][Scope:avatar_poisoning]
        /// Refusing the copy only protects an FBX that is not poisoned yet. An asset whose .meta ALREADY holds a
        /// foreign humanDescription has to be repaired, and this button is the only repair path a user has -
        /// otherwise the fix is "delete the .meta by hand", which nobody will find.
        ///
        /// Resetting avatarSetup/sourceAvatar is NOT enough on its own, which is why the stored map is inspected
        /// as well. Unity reverts a rejected copy to avatarSetup: 1 (CreateFromThisModel) with
        /// lastHumanDescriptionAvatarSource: {instanceID: 0} and KEEPS the copied humanDescription - measured in
        /// Assets/CustomMotions/ZepetoRig_Wave.fbx.meta: avatarSetup 1, source instanceID 0, and a 55-entry human
        /// list starting 'boneName: hips'. So on a poisoned asset both of those fields already read clean while
        /// the map that breaks avatar creation is still there. The map is what has to go.
        ///
        /// Nothing is rewritten while the asset still produces a usable Humanoid Avatar, and that one
        /// precondition is what keeps the repair from becoming a new bug. A hand-configured mapping is never
        /// wiped, and neither is a model whose bone hierarchy optimizeGameObjects has stripped - its transform
        /// names are gone, so ANY map would look foreign, but its Avatar is fine and it is therefore left alone.
        ///
        /// It also cannot loop. After the clear Unity auto-maps this model's own bones
        /// (autoGenerateAvatarMappingIfUnspecified: 1), so the stored map can only name bones that exist and the
        /// setup is already CreateFromThisModel/null. Both tests then report nothing to do on the next press,
        /// whether the Avatar came out usable or not.
        /// </summary>
        private static bool NeedsForeignAvatarMapCleared(ModelImporter importer, string assetPath, out string reason)
        {
            reason = string.Empty;

            if (importer == null)
            {
                return false;
            }

            Avatar current = FindImportedModelAvatar(assetPath);
            if (current != null && current.isValid && current.isHuman)
            {
                return false;
            }

            HumanBone[] storedBones = importer.humanDescription.human;
            if (storedBones != null && storedBones.Length > 0)
            {
                HashSet<string> modelBoneNames = CollectModelBoneNames(assetPath);
                if (modelBoneNames != null)
                {
                    int missingCount;
                    string firstMissingName;
                    CountMissingHumanBones(storedBones, modelBoneNames, out missingCount, out firstMissingName);
                    if (missingCount > 0)
                    {
                        reason = ".meta에 남아있던 뼈 매핑이 이 FBX에 없는 뼈를 가리킵니다 ("
                            + storedBones.Length + "개 중 " + missingCount + "개 없음, 예: "
                            + firstMissingName + ")";
                        return true;
                    }
                }
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || importer.sourceAvatar != null)
            {
                reason = "다른 리그의 Avatar를 복사하도록 설정돼 있었습니다";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts the importer back to what a never-configured Humanoid import looks like, then reimports.
        ///
        /// Empty human/skeleton lists are the "unspecified" state Unity's
        /// autoGenerateAvatarMappingIfUnspecified flag reacts to, so the reimport maps this model's own bones
        /// instead of reusing the foreign map. Only ever called behind NeedsForeignAvatarMapCleared, which is
        /// where the reasoning about what is safe to wipe lives.
        /// </summary>
        private static void ClearForeignAvatarMap(ModelImporter importer)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;

            HumanDescription cleared = importer.humanDescription;
            cleared.human = new HumanBone[0];
            cleared.skeleton = new SkeletonBone[0];
            importer.humanDescription = cleared;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
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
