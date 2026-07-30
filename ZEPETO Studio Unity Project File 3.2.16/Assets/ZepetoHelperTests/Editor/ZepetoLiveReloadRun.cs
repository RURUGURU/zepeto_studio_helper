using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Tests
{
    /// <summary>
    /// Proves the live-preview round trip end to end, inside a real Play session.
    ///
    /// The self test only covers the asset-level invariant (CopySerialized keeps the GUID, the clip contents
    /// change, the loop flag sticks). The question that actually matters is different: does a RUNNING Animator
    /// switch to the new motion without a rebind? That can only be answered while playing, so this driver
    /// swaps a second fbx in mid-Play - exactly what pressing 'Unity로 보내기' in Blender does - and then
    /// measures the avatar, not the asset.
    ///
    /// Drop "zepeto-livereload.trigger" at the project root to run. Needs two fixtures beside it:
    ///   zepeto-live-a.fbx   (48 frames)
    ///   zepeto-live-b.fbx   (96 frames, a different bone posed)
    /// </summary>
    [InitializeOnLoad]
    public static class ZepetoLiveReloadRun
    {
        private const string TriggerPath = "zepeto-livereload.trigger";
        private const string ResultPath = "zepeto-livereload.result.txt";
        private const string FixtureA = "zepeto-live-a.fbx";
        private const string FixtureB = "zepeto-live-b.fbx";
        private const string WatchedAsset = "Assets/CustomMotions/LiveRoundTrip.fbx";

        private const string RunningKey = "Easy.ZepetoHelper.LiveRun.Running";
        private const string PhaseKey = "Easy.ZepetoHelper.LiveRun.Phase";
        private const string ClockKey = "Easy.ZepetoHelper.LiveRun.Clock";
        private const string LenBeforeKey = "Easy.ZepetoHelper.LiveRun.LenBefore";
        private const string CountBeforeKey = "Easy.ZepetoHelper.LiveRun.CountBefore";
        private const string TravelKey = "Easy.ZepetoHelper.LiveRun.Travel";
        private const string MinKey = "Easy.ZepetoHelper.LiveRun.Min";
        private const string MaxKey = "Easy.ZepetoHelper.LiveRun.Max";

        private const float SettleSeconds = 12f;   // avatar download + first frames
        private const float ObserveSeconds = 8f;   // watch the swapped motion play

        private static readonly BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags Stat =
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static bool hooked;
        private static bool waitingForEditMode;

        private static void StartAfterPlayEnds(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= StartAfterPlayEnds;
            waitingForEditMode = false;
            EditorApplication.delayCall += StartIfRequested;
        }

        static ZepetoLiveReloadRun()
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                Hook();
            }
            else
            {
                EditorApplication.delayCall += StartIfRequested;
            }
        }

        private static void Append(string line)
        {
            File.AppendAllText(ResultPath, line + Environment.NewLine);
        }

        private static Type HelperType()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("Easy.ZepetoHelper.Editor.ZepetoStudioHelperWindow");
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static void StartIfRequested()
        {
            if (!File.Exists(TriggerPath))
            {
                return;
            }

            // This driver configures importers and enters Play itself, so it has to start from edit mode.
            // Firing it during an existing Play session would write importer settings mid-Play and fight the
            // session already running. Keep the trigger and start once the user stops.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Not delayCall: that re-fires every editor tick, so it would spam the console for the whole
                // Play session. Wait for the one event that matters instead.
                if (!waitingForEditMode)
                {
                    waitingForEditMode = true;
                    EditorApplication.playModeStateChanged += StartAfterPlayEnds;
                    Debug.Log("ZEPETO live reload run: Play 중이라 대기합니다. Stop을 누르면 시작합니다.");
                }

                return;
            }

            File.Delete(TriggerPath);
            File.WriteAllText(ResultPath, "ZEPETO live reload round trip" + Environment.NewLine);

            if (!File.Exists(FixtureA) || !File.Exists(FixtureB))
            {
                Append("FATAL: fixtures missing (" + FixtureA + ", " + FixtureB + ")");
                return;
            }

            Type helperType = HelperType();
            if (helperType == null)
            {
                Append("FATAL: helper type not found");
                return;
            }

            Directory.CreateDirectory("Assets/CustomMotions");
            File.Copy(FixtureA, WatchedAsset, true);
            AssetDatabase.ImportAsset(WatchedAsset, ImportAssetOptions.ForceUpdate);
            Append("placed fixture A at " + WatchedAsset);

            // A real, docked window - not CreateInstance/DestroyImmediate. PumpLiveReload is subscribed in
            // OnEnable and unsubscribed in OnDisable, so a throwaway instance would tear the watcher down
            // immediately and nothing would ever fire. This also mirrors real use, where the helper is open.
            EditorWindow helper = EditorWindow.GetWindow(helperType, false, "ZEPETO Helper", true);
            helper.Show();

            helperType.GetMethod("FindLoaderAndSerializedFields", Inst).Invoke(helper, null);

            object[] cfg = new object[] { WatchedAsset, null };
            bool configured = (bool)helperType.GetMethod("TryConfigureMotionFbx", Inst).Invoke(helper, cfg);
            Append("configure fixture A -> " + configured + " : " + cfg[1]);

            MethodInfo request = helperType.GetMethod("RequestLivePreviewPlay", Inst);
            if (request == null)
            {
                Append("FATAL: RequestLivePreviewPlay not found");
                return;
            }

            request.Invoke(helper, null);
            Append("RequestLivePreviewPlay invoked");
            Append("runInBackground = " + PlayerSettings.runInBackground);

            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(PhaseKey, "entering");
            SessionState.SetFloat(ClockKey, 0f);
            Hook();
        }

        private static void Hook()
        {
            if (hooked)
            {
                return;
            }

            hooked = true;
            EditorApplication.update += Tick;
        }

        private static float Elapsed()
        {
            float start = SessionState.GetFloat(ClockKey, 0f);
            if (start <= 0f)
            {
                SessionState.SetFloat(ClockKey, (float)EditorApplication.timeSinceStartup);
                return 0f;
            }

            return (float)EditorApplication.timeSinceStartup - start;
        }

        private static void ResetClock()
        {
            SessionState.SetFloat(ClockKey, (float)EditorApplication.timeSinceStartup);
        }

        private static AnimationClip LiveClip()
        {
            Type t = HelperType();
            PropertyInfo p = t == null ? null : t.GetProperty("LiveClipAssetPath", Stat);
            string path = p == null ? null : p.GetValue(null, null) as string;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static int ReloadCount()
        {
            return SessionState.GetInt("Easy.ZepetoHelper.LiveReloadCount", -1);
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            string phase = SessionState.GetString(PhaseKey, string.Empty);

            if (phase == "entering")
            {
                if (!EditorApplication.isPlaying)
                {
                    if (Elapsed() > 60f)
                    {
                        Finish("play mode never became active");
                    }

                    return;
                }

                Append("--- Play active ---");
                Append("armed = " + SessionState.GetBool("Easy.ZepetoHelper.LiveReloadArmed", false));
                SessionState.SetString(PhaseKey, "settling");
                ResetClock();
                return;
            }

            if (phase == "settling")
            {
                if (!EditorApplication.isPlaying)
                {
                    Finish("play exited during settle");
                    return;
                }

                // Sample the last stretch of the settle window, once the avatar has finished downloading, so
                // phase A has a real baseline of "fixture A is playing".
                if (Elapsed() > SettleSeconds * 0.5f)
                {
                    SampleAvatar("a");
                }

                if (Elapsed() < SettleSeconds)
                {
                    return;
                }

                AnimationClip before = LiveClip();
                float lenBefore = before == null ? -1f : before.length;
                SessionState.SetFloat(LenBeforeKey, lenBefore);
                SessionState.SetInt(CountBeforeKey, ReloadCount());
                Append("before swap: live clip length = " + lenBefore.ToString("0.00")
                    + "s, reload count = " + ReloadCount());

                // This is the moment Blender's export happens in real use.
                File.Copy(FixtureB, WatchedAsset, true);
                Append("--- swapped fixture B in (96 frames) ---");

                SessionState.SetString(PhaseKey, "waiting");
                ResetClock();
                return;
            }

            if (phase == "waiting")
            {
                if (!EditorApplication.isPlaying)
                {
                    Finish("play exited while waiting for reload");
                    return;
                }

                int before = SessionState.GetInt(CountBeforeKey, -1);
                if (ReloadCount() > before)
                {
                    AnimationClip after = LiveClip();
                    Append("reload fired after " + Elapsed().ToString("0.0") + "s, count = " + ReloadCount());
                    Append("after swap: live clip length = "
                        + (after == null ? -1f : after.length).ToString("0.00") + "s");

                    SessionState.SetString(PhaseKey, "observing");
                    ResetClock();
                    return;
                }

                if (Elapsed() > 25f)
                {
                    Append("FAIL: the watcher never fired within 25s");
                    Append("  message = " + SessionState.GetString("Easy.ZepetoHelper.LiveMessage", "(none)"));
                    SessionState.SetString(PhaseKey, "stopping");
                    EditorApplication.isPlaying = false;
                }

                return;
            }

            if (phase == "observing")
            {
                if (!EditorApplication.isPlaying)
                {
                    Finish("play exited while observing");
                    return;
                }

                SampleAvatar("b");

                if (Elapsed() < ObserveSeconds)
                {
                    return;
                }

                Report();
                SessionState.SetString(PhaseKey, "stopping");
                EditorApplication.isPlaying = false;
                return;
            }

            if (phase == "stopping" && !EditorApplication.isPlaying)
            {
                Finish("done");
            }
        }

        /// <summary>
        /// Fixture A swings the RIGHT ARM and never touches the legs; fixture B swings the LEFT LEG and never
        /// touches the arms. Measuring both bones in both phases is what makes the result unambiguous:
        ///   phase A: arm moves, leg still   -> the avatar animates at all
        ///   phase B: leg moves, arm still   -> the swap really reached the running Animator
        /// A single-bone probe cannot tell "hot reload failed" apart from "nothing is animating".
        /// </summary>
        private static void SampleAvatar(string phasePrefix)
        {
            GameObject loader = GameObject.Find("LOADER");
            if (loader == null)
            {
                return;
            }

            Animator animator = loader.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null)
            {
                return;
            }

            // Track each axis separately and report the widest. Measuring a single axis was wrong the first
            // time: fixture B rotates the thigh about Z, which swings the knee sideways, so a forward/back
            // probe reported "no movement" for a leg that was in fact swinging.
            if (knee != null)
            {
                Track(phasePrefix + ".leg", knee.position - hips.position);
            }

            if (hand != null)
            {
                Track(phasePrefix + ".arm", hand.position - hips.position);
            }
        }

        private static void Track(string key, Vector3 value)
        {
            TrackAxis(key + ".x", value.x);
            TrackAxis(key + ".y", value.y);
            TrackAxis(key + ".z", value.z);
        }

        private static void TrackAxis(string key, float value)
        {
            string minKey = MinKey + "." + key;
            string maxKey = MaxKey + "." + key;
            SessionState.SetFloat(minKey, Mathf.Min(SessionState.GetFloat(minKey, float.MaxValue), value));
            SessionState.SetFloat(maxKey, Mathf.Max(SessionState.GetFloat(maxKey, float.MinValue), value));
        }

        /// <summary>Widest range any single axis covered, in metres.</summary>
        private static float Travel(string key)
        {
            float widest = 0f;
            foreach (string axis in new[] { "x", "y", "z" })
            {
                float min = SessionState.GetFloat(MinKey + "." + key + "." + axis, float.MaxValue);
                float max = SessionState.GetFloat(MaxKey + "." + key + "." + axis, float.MinValue);
                if (min < float.MaxValue && max > float.MinValue)
                {
                    widest = Mathf.Max(widest, max - min);
                }
            }

            return widest;
        }

        private static void Report()
        {
            float lenBefore = SessionState.GetFloat(LenBeforeKey, -1f);
            AnimationClip after = LiveClip();
            float lenAfter = after == null ? -1f : after.length;

            Append("--- results ---");
            Append("clip length: " + lenBefore.ToString("0.00") + "s -> " + lenAfter.ToString("0.00") + "s");
            Append(Mathf.Abs(lenAfter - lenBefore) > 0.5f
                ? "PASS clip-swapped: the live clip really became the new motion"
                : "FAIL clip-swapped: the live clip did not change length");

            float armA = Travel("a.arm");
            float legA = Travel("a.leg");
            float armB = Travel("b.arm");
            float legB = Travel("b.leg");

            Append("phase A (fixture A, arm motion): arm=" + armA.ToString("0.000")
                + "m leg=" + legA.ToString("0.000") + "m");
            Append("phase B (fixture B, leg motion): arm=" + armB.ToString("0.000")
                + "m leg=" + legB.ToString("0.000") + "m");

            const float Moved = 0.02f;
            if (armA <= Moved && legA <= Moved)
            {
                Append("FAIL avatar-animating: the avatar never moved at all, even before the swap. "
                    + "This is not a hot-reload problem - playback itself is broken.");
                return;
            }

            Append(legB > Moved
                ? "PASS avatar-animating: after the swap the RUNNING avatar swung the LEG, "
                    + "which only fixture B does - the hot reload reached the Animator with no rebind"
                : "FAIL avatar-animating: the leg never moved after the swap, so the Animator is still "
                    + "playing the old motion - Animator.Rebind() is needed");

            GameObject loader = GameObject.Find("LOADER");
            Animator animator = loader == null ? null : loader.GetComponentInChildren<Animator>();
            if (animator != null && animator.layerCount > 0)
            {
                AnimatorClipInfo[] infos = animator.GetCurrentAnimatorClipInfo(0);
                for (int i = 0; i < infos.Length; i++)
                {
                    Append("animator is playing: " + (infos[i].clip == null ? "NULL" : infos[i].clip.name)
                        + " (" + (infos[i].clip == null ? 0f : infos[i].clip.length).ToString("0.00") + "s)");
                }
            }
        }

        private static void Finish(string reason)
        {
            Append("--- finished: " + reason + " ---");
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            hooked = false;
        }
    }
}
