using System;
using System.IO;
using System.Reflection;
using Easy.ZepetoHelper.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Takes a generated animation FBX all the way to a moving avatar and proves it moved, by sampling the
    /// Humanoid hand bone during Play instead of trusting that "the clip is assigned" means "the avatar animates".
    /// </summary>
    public static class ZepetoCustomMotionRun
    {
        private const string TriggerPath = "zepeto-custom-motion.trigger";
        private const string ReportPath = "zepeto-custom-motion.report.txt";
        private const string RunningKey = "Easy.ZepetoHelper.CustomMotion.Running";
        private const string PhaseKey = "Easy.ZepetoHelper.CustomMotion.Phase";
        private const string StartKey = "Easy.ZepetoHelper.CustomMotion.Start";
        private const string MinKey = "Easy.ZepetoHelper.CustomMotion.HandMin";
        private const string MaxKey = "Easy.ZepetoHelper.CustomMotion.HandMax";
        private const double PlaySeconds = 18d;
        private const int Serial = 20;

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

                EditorSceneManager.OpenScene("Assets/Playground.unity", OpenSceneMode.Single);
                helperType.GetMethod("FindLoaderAndSerializedFields", Inst).Invoke(helper, null);

                object[] ctrlArgs = new object[] { null };
                Append("local controller -> " + helperType.GetMethod("EnsureLocalAnimatorController", Inst).Invoke(helper, ctrlArgs)
                    + " : " + ctrlArgs[0]);

                bool assigned = (bool)helperType.GetMethod("AssignAnimationClip", Inst).Invoke(helper, new object[] { customClip, false });
                Append("step 3 assign -> " + assigned);

                AnimationClip playback = helperType.GetMethod("GetPlaybackClip", Inst).Invoke(helper, null) as AnimationClip;
                Append("playback slot = " + (playback == null ? "NULL" : playback.name));

                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(helper);
            }

            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(PhaseKey, "entering");
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
            EditorApplication.update -= Tick;
            hooked = false;
            Debug.Log("ZEPETO custom motion run finished: " + reason);
        }

        private static void Append(string line)
        {
            try { File.AppendAllText(ReportPath, line + Environment.NewLine); } catch { }
        }
    }
}
