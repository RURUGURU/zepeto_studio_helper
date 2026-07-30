using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Easy.ZepetoHelper.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Takes a generated animation FBX all the way to a moving avatar and proves it moved, by sampling the
    /// Humanoid hand bone during Play instead of trusting that "the clip is assigned" means "the avatar animates".
    ///
    /// Proving it needs the user's own work scene, so this runner borrows it rather than keeps it: every reference
    /// it overwrites is snapshotted into SessionState before Play and put back afterwards, in the same post-Play
    /// path that reports the result. See RestoreSnapshot.
    /// </summary>
    public static class ZepetoCustomMotionRun
    {
        private const string TriggerPath = "zepeto-custom-motion.trigger";
        private const string ReportPath = "zepeto-custom-motion.report.txt";
        private const string SkipPath = "zepeto-custom-motion.skipped.txt";
        private const string WorkScenePath = "Assets/Playground.unity";
        private const string RunnerLabel = "ZEPETO custom motion run";
        private const string RunningKey = "Easy.ZepetoHelper.CustomMotion.Running";
        private const string PhaseKey = "Easy.ZepetoHelper.CustomMotion.Phase";
        private const string StartKey = "Easy.ZepetoHelper.CustomMotion.Start";
        private const string MinKey = "Easy.ZepetoHelper.CustomMotion.HandMin";
        private const string MaxKey = "Easy.ZepetoHelper.CustomMotion.HandMax";

        // Undo snapshot of everything this run writes into the user's project. SessionState, not fields and not
        // EditorPrefs: the restore happens on the far side of the domain reload that entering Play is, and a
        // snapshot inherited from a previous editor session would describe a scene that is no longer open.
        // Path plus name for every reference, because a clip can be a sub-asset of an fbx and LoadAssetAtPath
        // returns whichever one comes first - the same reason the helper's own MotionPreviewRestore* pair exists.
        private const string SnapshotKey = "Easy.ZepetoHelper.CustomMotion.Snapshot";
        private const string ClipPathKey = "Easy.ZepetoHelper.CustomMotion.ClipBefore.Path";
        private const string ClipNameKey = "Easy.ZepetoHelper.CustomMotion.ClipBefore.Name";
        private const string ControllerPathKey = "Easy.ZepetoHelper.CustomMotion.ControllerBefore.Path";
        private const string ControllerNameKey = "Easy.ZepetoHelper.CustomMotion.ControllerBefore.Name";
        private const string OverrideControllerKey = "Easy.ZepetoHelper.CustomMotion.OverridesBefore.Controller";
        private const string OverrideSlotsKey = "Easy.ZepetoHelper.CustomMotion.OverridesBefore.Slots";
        // Whether this run has already written the work scene file. Decides how the restore ends: re-saving the
        // scene undoes that write, but a run that never wrote it must not be the first to do so.
        private const string SceneSavedKey = "Easy.ZepetoHelper.CustomMotion.SceneSaved";

        private const double PlaySeconds = 18d;
        private const int Serial = 21;

        private static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static bool hooked;

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // Re-arm synchronously. delayCall is not reliably pumped across the play-mode domain reload, which
            // previously left a run stuck in Play forever with nothing driving it.
            if (SessionState.GetBool(RunningKey, false)) { Hook(); }
            EditorApplication.delayCall += Bootstrap;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnReload() { EditorApplication.delayCall += Bootstrap; }

        private static void Bootstrap()
        {
            if (SessionState.GetBool(RunningKey, false)) { Hook(); return; }

            // A restore that could not be done when the run ended - the editor was still leaving Play, a script
            // reload cut in - is retried on every load, so an armed snapshot can never quietly outlive its run and
            // leave the user's scene holding this run's clip.
            if (SessionState.GetBool(SnapshotKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RestoreSnapshot();
            }

            if (!File.Exists(TriggerPath)) { return; }
            // This driver enters Play itself, so it must start from edit mode.
            if (EditorApplication.isPlayingOrWillChangePlaymode) { return; }

            string fbx = File.ReadAllText(TriggerPath).Trim();
            File.Delete(TriggerPath);
            Run(fbx);
        }

        private static void Run(string fbxPath)
        {
            File.WriteAllText(ReportPath, "custom motion end-to-end\nfbx = " + fbxPath + "\n----\n");

            if (!File.Exists(fbxPath))
            {
                Append("FATAL: fbx not found");
                return;
            }

            // This driver opens Assets/Playground.unity over whatever is loaded and then SAVES it, so unsaved
            // edits in the open scene would be discarded without a word - a stray trigger file was enough to
            // destroy them. Refuse instead. Deliberately no prompt (this runner must stay non-interactive) and
            // deliberately no save on the user's behalf: saving someone's half-finished scene is just as bad.
            if (ZepetoSelfTestSceneGuard.RefuseIfAnyOpenSceneIsDirty(
                    RunnerLabel,
                    "이 실행은 " + WorkScenePath + "을 열고 저장하기 때문에 편집 내용이 사라집니다.",
                    SkipPath,
                    Append) != null)
            {
                Append("  Save or discard the scene yourself, then drop " + TriggerPath + " again.");
                return;
            }

            ZepetoSelfTestSceneGuard.ClearSkipRecord(SkipPath);

            // [QC][Invariant:every_session_key_reseeded_per_run]
            // The undo snapshot is emptied here, before anything can be recorded into it, so a run that aborts
            // below cannot leave the PREVIOUS run's snapshot armed for the next Finish to "restore".
            ClearSnapshot();

            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

            Type helperType = typeof(ZepetoStudioHelperWindow);
            ScriptableObject helper = ScriptableObject.CreateInstance(helperType);
            AnimationClip customClip = null;

            try
            {
                object[] args = new object[] { fbxPath, null };
                bool configured = (bool)helperType.GetMethod("TryConfigureMotionFbx", Inst).Invoke(helper, args);
                Append("step 1 configure -> " + configured + " : " + args[1]);

                // Did Unity actually build a valid Humanoid avatar from this rig?
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                Append("animationType = " + (importer == null ? "?" : importer.animationType.ToString()));
                if (importer != null)
                {
                    // The whole point of the round trip: retarget through the real ZEPETO model's avatar.
                    Append("avatarSetup = " + importer.avatarSetup
                        + "  sourceAvatar = " + (importer.sourceAvatar == null ? "NULL" : importer.sourceAvatar.name));
                }
                UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                Append("sub-assets in fbx: " + all.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) { continue; }
                    Append("  " + all[i].GetType().Name + " '" + all[i].name + "' hideFlags=" + all[i].hideFlags);

                    Avatar av = all[i] as Avatar;
                    if (av != null)
                    {
                        Append("    avatar isValid=" + av.isValid + " isHuman=" + av.isHuman);
                    }
                }

                if (importer != null)
                {
                    Append("defaultClipAnimations: " + (importer.defaultClipAnimations == null ? 0 : importer.defaultClipAnimations.Length));
                    Append("clipAnimations: " + (importer.clipAnimations == null ? 0 : importer.clipAnimations.Length));
                    Append("importAnimation: " + importer.importAnimation);
                }

                object[] extractArgs = new object[] { fbxPath, null };
                bool extracted = (bool)helperType.GetMethod("TryExtractMotionFromFbx", Inst).Invoke(helper, extractArgs);
                Append("step 2 extract -> " + extracted + " : " + extractArgs[1]);
                if (!extracted) { return; }

                // Pick the clip that came from THIS fbx, not just any clip sitting in the folder.
                string expected = Path.GetFileNameWithoutExtension(fbxPath);
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/ZepetoHelper/Motions" });
                for (int i = 0; i < guids.Length; i++)
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!Path.GetFileNameWithoutExtension(clipPath).StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AnimationClip c = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (c != null) { customClip = c; }
                }

                if (customClip == null) { Append("FATAL: no extracted clip"); return; }
                Append("clip = " + customClip.name + " length=" + customClip.length.ToString("0.00")
                    + "s humanoid=" + customClip.isHumanMotion);

                EditorSceneManager.OpenScene(WorkScenePath, OpenSceneMode.Single);
                helperType.GetMethod("FindLoaderAndSerializedFields", Inst).Invoke(helper, null);

                // [QC][Invariant:runner_restores_what_it_changed]
                // Everything from here on writes into the user's own work scene - and, through
                // AssignAnimationClip -> ApplyClipToOverrideController -> AssetDatabase.SaveAssets, into a project
                // asset - and then SaveScene puts it on disk, so leaving Play undoes none of it. Snapshot first.
                // Refusing to run beats mutating something that cannot be put back, so a snapshot that cannot be
                // taken aborts the run: the same missing binding would make AssignAnimationClip fail anyway.
                if (!TrySnapshotLoaderReferences(helper))
                {
                    Append("FATAL: could not read the LOADER's current references, so nothing was changed.");
                    Debug.LogWarning(RunnerLabel + ": LOADER 참조를 읽지 못해 되돌릴 수 없으므로 "
                        + "아무것도 바꾸지 않고 중단했습니다.");
                    return;
                }

                object[] ctrlArgs = new object[] { null };
                Append("local controller -> " + helperType.GetMethod("EnsureLocalAnimatorController", Inst).Invoke(helper, ctrlArgs)
                    + " : " + ctrlArgs[0]);

                // Snapshotted only now, because the local-copy step above is what decides WHICH controller gets
                // its override table rewritten. Taking it earlier would record the wrong asset.
                SnapshotOverrideTable(helper);

                bool assigned = (bool)helperType.GetMethod("AssignAnimationClip", Inst).Invoke(helper, new object[] { customClip, false });
                Append("step 3 assign -> " + assigned);

                AnimationClip playback = helperType.GetMethod("GetPlaybackClip", Inst).Invoke(helper, null) as AnimationClip;
                Append("playback slot = " + (playback == null ? "NULL" : playback.name));

                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                SessionState.SetBool(SceneSavedKey, true);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(helper);

                // The hand-off to Play happens right after SaveScene, the last statement of the block above, so a
                // false flag here means the run aborted - an exception inside the helper, say. Restore now, because
                // nothing else ever will: Finish() is the only other restore path and it is only reached by way of
                // Play.
                if (!SessionState.GetBool(SceneSavedKey, false))
                {
                    RestoreSnapshot();
                }
            }

            // [QC][Invariant:every_session_key_reseeded_per_run]
            // SessionState is editor-session scoped, so anything not re-seeded here is inherited from the
            // PREVIOUS run in this session. StartKey used to be the exception, which made the runner
            // single-shot: the "entering" branch below read the first run's stamp, computed `since` against a
            // clock reading from minutes earlier, and called Finish("play mode never became active") before
            // Play could ever be observed. 0 is the "not stamped yet" sentinel that branch looks for.
            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(PhaseKey, "entering");
            SessionState.SetFloat(StartKey, 0f);
            SessionState.SetFloat(MinKey, float.MaxValue);
            SessionState.SetFloat(MaxKey, float.MinValue);
            Hook();
            Append("--- entering Play ---");
            EditorApplication.isPlaying = true;
        }

        private static void Hook()
        {
            if (hooked) { return; }
            hooked = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(RunningKey, false)) { return; }
            string phase = SessionState.GetString(PhaseKey, string.Empty);

            if (phase == "entering")
            {
                if (!EditorApplication.isPlaying)
                {
                    // Never wait forever for a play transition that is not coming.
                    float since = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);
                    if (SessionState.GetFloat(StartKey, 0f) <= 0f)
                    {
                        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                    }
                    else if (since > 60f)
                    {
                        Finish("play mode never became active");
                    }

                    return;
                }

                SessionState.SetString(PhaseKey, "playing");
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
                Append("--- Play active ---");
                return;
            }

            if (phase == "stopping")
            {
                if (!EditorApplication.isPlaying) { Finish("done"); }
                return;
            }

            if (phase != "playing") { return; }

            if (!EditorApplication.isPlaying) { Finish("play exited early"); return; }

            // Sample the Humanoid right hand height. A static pose keeps it constant; a real wave does not.
            GameObject loader = GameObject.Find("LOADER");
            if (loader != null)
            {
                Animator animator = loader.GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hand != null && hips != null)
                    {
                        float h = hand.position.y - hips.position.y;
                        SessionState.SetFloat(MinKey, Mathf.Min(SessionState.GetFloat(MinKey, float.MaxValue), h));
                        SessionState.SetFloat(MaxKey, Mathf.Max(SessionState.GetFloat(MaxKey, float.MinValue), h));
                    }
                }
            }

            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) < PlaySeconds) { return; }

            float min = SessionState.GetFloat(MinKey, 0f);
            float max = SessionState.GetFloat(MaxKey, 0f);
            float travel = max - min;

            Append("right hand height relative to hips: min=" + min.ToString("0.000")
                + " max=" + max.ToString("0.000") + " travel=" + travel.ToString("0.000") + "m");
            Append(travel > 0.05f
                ? "RESULT: the avatar IS performing the custom motion"
                : "RESULT: the hand barely moved - the avatar is NOT animating");

            GameObject l2 = GameObject.Find("LOADER");
            if (l2 != null)
            {
                Animator a = l2.GetComponentInChildren<Animator>();
                if (a != null && a.layerCount > 0)
                {
                    AnimatorClipInfo[] infos = a.GetCurrentAnimatorClipInfo(0);
                    for (int i = 0; i < infos.Length; i++)
                    {
                        Append("playing: " + (infos[i].clip == null ? "NULL" : infos[i].clip.name)
                            + " (" + (infos[i].clip == null ? 0f : infos[i].clip.length).ToString("0.00") + "s)");
                    }
                }
            }

            SessionState.SetString(PhaseKey, "stopping");
            EditorApplication.isPlaying = false;
        }

        private static void Finish(string reason)
        {
            Append("--- finished: " + reason + " (serial " + Serial + ") ---");
            SessionState.SetBool(RunningKey, false);
            SessionState.SetString(PhaseKey, string.Empty);
            // Clear the clock as well, so a run that is started and never reaches Run() - or a future caller
            // that forgets to re-seed - cannot inherit this run's timeout deadline.
            SessionState.SetFloat(StartKey, 0f);

            // The run state above is cleared first so a Tick cannot re-enter while the scene is being written.
            // Every path that reaches Finish has already observed !EditorApplication.isPlaying, so these are edit
            // mode writes - which is what makes rewriting the override table safe here and never mid-Play.
            RestoreSnapshot();

            EditorApplication.update -= Tick;
            hooked = false;
            Debug.Log("ZEPETO custom motion run finished: " + reason);
        }

        // ---------- undo snapshot ----------

        /// <summary>
        /// Records the two LOADER references this run is about to overwrite. False means they could not be read, in
        /// which case the caller must not write them either.
        /// </summary>
        private static bool TrySnapshotLoaderReferences(ScriptableObject helper)
        {
            if (!TrySnapshotReference(helper, "animationClipProperty", ClipPathKey, ClipNameKey))
            {
                return false;
            }

            if (!TrySnapshotReference(helper, "animatorControllerProperty", ControllerPathKey, ControllerNameKey))
            {
                return false;
            }

            // Armed before the first write - EnsureLocalAnimatorController - rather than after the last one, so an
            // abort in between still has something to restore from.
            SessionState.SetBool(SnapshotKey, true);
            return true;
        }

        private static bool TrySnapshotReference(ScriptableObject helper, string fieldName, string pathKey, string nameKey)
        {
            SerializedProperty property = GetHelperProperty(helper, fieldName);
            if (property == null || property.serializedObject == null || property.serializedObject.targetObject == null)
            {
                Append("snapshot FAILED: " + fieldName + " is not bound to the LOADER");
                return false;
            }

            UnityEngine.Object value = property.objectReferenceValue;
            string path = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
            SessionState.SetString(pathKey, path);
            SessionState.SetString(nameKey, value == null ? string.Empty : value.name);
            Append("snapshot " + fieldName + " = " + (value == null ? "NULL" : value.name + " @ " + path));
            return true;
        }

        /// <summary>
        /// Records the override table of the controller the LOADER is pointing at right now.
        ///
        /// Only the slot VALUES are stored, in slot order. The keys belong to the base controller and nothing this
        /// run does can change them, so the restore reads the current keys back and only puts the values in - which
        /// also means a slot count that no longer matches is caught instead of silently mis-assigned.
        /// </summary>
        private static void SnapshotOverrideTable(ScriptableObject helper)
        {
            SerializedProperty property = GetHelperProperty(helper, "animatorControllerProperty");
            AnimatorOverrideController controller = property == null
                ? null
                : property.objectReferenceValue as AnimatorOverrideController;

            if (controller == null)
            {
                SessionState.SetString(OverrideControllerKey, string.Empty);
                SessionState.SetString(OverrideSlotsKey, string.Empty);
                Append("snapshot override table: the LOADER controller is not an AnimatorOverrideController");
                return;
            }

            // A controller still under Packages/ means EnsureLocalAnimatorController did not manage to make a
            // local copy, and ApplyClipToOverrideController refuses to write a package asset - so there will be
            // nothing to undo. Recording it would be worse than useless: the restore ends in SetDirty +
            // SaveAssets, which is exactly the write into the SDK's own files that the helper exists to avoid.
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (IsReadOnlyAssetPath(controllerPath))
            {
                SessionState.SetString(OverrideControllerKey, string.Empty);
                SessionState.SetString(OverrideSlotsKey, string.Empty);
                Append("snapshot override table: skipped, " + controllerPath + " is a read-only package asset");
                return;
            }

            List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);
            controller.GetOverrides(overrides);

            StringBuilder encoded = new StringBuilder();
            for (int i = 0; i < overrides.Count; i++)
            {
                if (i > 0)
                {
                    encoded.Append('\n');
                }

                AnimationClip value = overrides[i].Value;
                // "path|name", empty on both sides for a slot that had no override. An asset path cannot contain
                // '|' on any platform Unity supports, so decoding splits at the FIRST one and a name that somehow
                // contains one still survives the round trip.
                encoded.Append(value == null ? string.Empty : AssetDatabase.GetAssetPath(value));
                encoded.Append('|');
                encoded.Append(value == null ? string.Empty : value.name);
            }

            SessionState.SetString(OverrideControllerKey, AssetDatabase.GetAssetPath(controller));
            SessionState.SetString(OverrideSlotsKey, encoded.ToString());
            Append("snapshot override table: " + overrides.Count + " slot(s) on " + controller.name);
        }

        /// <summary>
        /// Puts the work scene and the override controller back the way this run found them.
        ///
        /// The override table goes first because it is the part already written to disk by
        /// AssetDatabase.SaveAssets, and it deliberately brings back whatever was mapped there before - usually the
        /// SDK's A_pose - because that is the pre-run state, not because a pose is desirable.
        ///
        /// It does NOT delete anything. A local AnimatorOverrideController this run had to create stays where it
        /// is: putting a reference back is safe, deleting an asset other things may point at is not.
        /// </summary>
        private static void RestoreSnapshot()
        {
            if (!SessionState.GetBool(SnapshotKey, false))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Nothing here may run mid-Play: rewriting the override table rebinds the Animator the live ZEPETO
                // avatar is using, which breaks its context outright. Bootstrap retries on the next load, and
                // leaving Play is one, so the snapshot stays armed rather than being dropped here.
                Append("RESTORE POSTPONED: Play is still active. It is retried on the next editor load.");
                return;
            }

            string failure = null;
            if (!TryFocusWorkSceneForRestore(out failure))
            {
                ReportRestoreFailure(failure);
                ClearSnapshot();
                return;
            }

            ScriptableObject helper = ScriptableObject.CreateInstance(typeof(ZepetoStudioHelperWindow));
            try
            {
                typeof(ZepetoStudioHelperWindow).GetMethod("FindLoaderAndSerializedFields", Inst).Invoke(helper, null);

                bool restored = RestoreOverrideTable();
                restored &= RestoreReference(helper, "animationClipProperty",
                    LoadSnapshotReference<AnimationClip>(ClipPathKey, ClipNameKey));
                restored &= RestoreReference(helper, "animatorControllerProperty",
                    LoadSnapshotReference<UnityEngine.Object>(ControllerPathKey, ControllerNameKey));

                if (!restored)
                {
                    failure = "one or more values could not be written back";
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(helper);
            }

            // Deliberately after the helper is gone, not inside the block above: a helper window instance puts a
            // temporary stand-in body in the scene and takes it away again when it is destroyed, and that can set
            // the unsaved-changes flag straight back on a scene this has just finished settling.
            FinishSceneAfterRestore();

            if (failure == null)
            {
                Append("--- restored the work scene to its pre-run state ---");
            }
            else
            {
                ReportRestoreFailure(failure);
            }

            ClearSnapshot();
        }

        /// <summary>
        /// Makes sure the work scene is the ACTIVE one: it holds the LOADER being restored, and the save that ends
        /// the restore writes whichever scene is active. Normally it already is - leaving Play brings back the
        /// scene the run entered Play with.
        /// </summary>
        private static bool TryFocusWorkSceneForRestore(out string failure)
        {
            failure = null;
            Scene active = EditorSceneManager.GetActiveScene();
            if (string.Equals(active.path, WorkScenePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Another scene is open, so the restore has to reopen the work scene over it - and that is exactly the
            // move this runner refuses to make while anything is unsaved.
            string dirtyScene = ZepetoSelfTestSceneGuard.FirstDirtyScenePath();
            if (dirtyScene != null)
            {
                failure = "a different scene is open (" + (string.IsNullOrEmpty(active.path) ? "untitled" : active.path)
                    + ") and " + dirtyScene + " has unsaved changes, so " + WorkScenePath
                    + " could not be reopened to restore it";
                return false;
            }

            if (!File.Exists(WorkScenePath))
            {
                failure = WorkScenePath + " no longer exists";
                return false;
            }

            EditorSceneManager.OpenScene(WorkScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>
        /// Leaves the scene's saved state truthful again: re-saving undoes the run's own SaveScene, and a run that
        /// never got that far only has the unsaved-changes flag its writes set, which is dropped without touching
        /// the file. Either way the sibling runners - which all refuse on a dirty scene - are not left blocked.
        /// </summary>
        private static void FinishSceneAfterRestore()
        {
            if (SessionState.GetBool(SceneSavedKey, false))
            {
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                Append("  work scene saved back to its pre-run contents");
                return;
            }

            string detail;
            Append(ZepetoSelfTestSceneGuard.TryClearSceneDirtiness(out detail)
                ? "  work scene left unwritten, unsaved-changes flag dropped (" + detail + ")"
                : "  work scene left unwritten but still marked as modified (" + detail + ")");
        }

        private static bool RestoreOverrideTable()
        {
            string controllerPath = SessionState.GetString(OverrideControllerKey, string.Empty);
            if (string.IsNullOrEmpty(controllerPath))
            {
                return true;
            }

            AnimatorOverrideController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(controllerPath);
            if (controller == null)
            {
                Append("  RESTORE FAILED override table: " + controllerPath + " could not be loaded");
                return false;
            }

            string encoded = SessionState.GetString(OverrideSlotsKey, string.Empty);
            string[] records = string.IsNullOrEmpty(encoded) ? new string[0] : encoded.Split('\n');

            List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);
            controller.GetOverrides(overrides);
            if (overrides.Count != records.Length)
            {
                Append("  RESTORE FAILED override table: " + controller.name + " has " + overrides.Count
                    + " slot(s), the snapshot has " + records.Length);
                return false;
            }

            for (int i = 0; i < records.Length; i++)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, DecodeClip(records[i]));
            }

            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Append("  restored override table: " + records.Length + " slot(s) on " + controller.name);
            return true;
        }

        private static AnimationClip DecodeClip(string record)
        {
            if (string.IsNullOrEmpty(record))
            {
                return null;
            }

            int separator = record.IndexOf('|');
            string path = separator < 0 ? record : record.Substring(0, separator);
            string clipName = separator < 0 ? string.Empty : record.Substring(separator + 1);
            return string.IsNullOrEmpty(path) ? null : FindAssetByName<AnimationClip>(path, clipName);
        }

        /// <summary>
        /// Writes a value straight back through the helper's own SerializedProperty.
        ///
        /// Not through AssignAnimationClip / EnsureLocalAnimatorController on purpose: both refuse values this has
        /// to be able to write - AssignAnimationClip refuses null, which is exactly what an untouched LOADER holds -
        /// and AssignAnimationClip would rewrite the override table again right after it was restored.
        /// </summary>
        private static bool RestoreReference(ScriptableObject helper, string fieldName, UnityEngine.Object value)
        {
            SerializedProperty property = GetHelperProperty(helper, fieldName);
            if (property == null || property.serializedObject == null || property.serializedObject.targetObject == null)
            {
                Append("  RESTORE FAILED " + fieldName + ": could not rebind it to write back '"
                    + (value == null ? "NULL" : value.name) + "'");
                return false;
            }

            property.serializedObject.Update();
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedProperties();
            Append("  restored " + fieldName + " = " + (value == null ? "NULL" : value.name));
            return true;
        }

        private static T LoadSnapshotReference<T>(string pathKey, string nameKey) where T : UnityEngine.Object
        {
            string path = SessionState.GetString(pathKey, string.Empty);
            return string.IsNullOrEmpty(path)
                ? null
                : FindAssetByName<T>(path, SessionState.GetString(nameKey, string.Empty));
        }

        /// <summary>
        /// The asset at that path with that name. The name matters because a clip can be a sub-asset - one take of
        /// an fbx, or one clip among several in an SDK asset - where LoadAssetAtPath hands back whichever comes
        /// first rather than the one that was recorded.
        /// </summary>
        private static T FindAssetByName<T>(string path, string assetName) where T : UnityEngine.Object
        {
            T direct = AssetDatabase.LoadAssetAtPath<T>(path);
            if (direct != null && (string.IsNullOrEmpty(assetName) || direct.name == assetName))
            {
                return direct;
            }

            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                T candidate = all[i] as T;
                if (candidate != null && candidate.name == assetName)
                {
                    return candidate;
                }
            }

            return direct;
        }

        private static SerializedProperty GetHelperProperty(ScriptableObject helper, string fieldName)
        {
            FieldInfo field = typeof(ZepetoStudioHelperWindow).GetField(fieldName, Inst);
            return field == null ? null : field.GetValue(helper) as SerializedProperty;
        }

        /// <summary>
        /// Same rule as the helper's own IsPackageOrPackageCachePath: a path segment, never a substring, so a
        /// perfectly writable folder like Assets/MyPackageCache is not condemned by accident.
        /// </summary>
        private static bool IsReadOnlyAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            return normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase)
                || normalized.IndexOf("/Library/PackageCache/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// A restore that did not fully happen is reported in Korean to the console as well as to the report,
        /// naming what to put back by hand. Silence would leave the user's scene holding this run's clip with
        /// nothing saying so.
        /// </summary>
        private static void ReportRestoreFailure(string failure)
        {
            string clip = SessionState.GetString(ClipNameKey, string.Empty);
            string controller = SessionState.GetString(ControllerNameKey, string.Empty);
            Append("RESTORE INCOMPLETE: " + failure);
            Debug.LogWarning(RunnerLabel + ": 실행 전 상태로 완전히 되돌리지 못했습니다 (" + failure + "). "
                + WorkScenePath + "의 LOADER에서 AnimationClip을 "
                + (string.IsNullOrEmpty(clip) ? "비어 있는 값" : clip) + "으로, AnimatorController를 "
                + (string.IsNullOrEmpty(controller) ? "비어 있는 값" : controller) + "으로 직접 돌려주세요.");
        }

        private static void ClearSnapshot()
        {
            SessionState.SetBool(SnapshotKey, false);
            SessionState.SetBool(SceneSavedKey, false);
            SessionState.SetString(ClipPathKey, string.Empty);
            SessionState.SetString(ClipNameKey, string.Empty);
            SessionState.SetString(ControllerPathKey, string.Empty);
            SessionState.SetString(ControllerNameKey, string.Empty);
            SessionState.SetString(OverrideControllerKey, string.Empty);
            SessionState.SetString(OverrideSlotsKey, string.Empty);
        }

        private static void Append(string line)
        {
            try { File.AppendAllText(ReportPath, line + Environment.NewLine); } catch { }
        }
    }
}
