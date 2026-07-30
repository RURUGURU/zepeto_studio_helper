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
    /// Drop "zepeto-livereload.trigger" at the project root to run. It needs the two fixtures described in the
    /// FIXTURE SPECIFICATION below, which must sit beside the trigger at the project root.
    /// </summary>
    // ---------------------------------------------------------------------------------------------------------
    // FIXTURE SPECIFICATION - how to recreate zepeto-live-a.fbx / zepeto-live-b.fbx
    //
    // Neither fixture is checked in (an fbx cannot be authored by hand), so this run is unreproducible until
    // someone rebuilds them. Everything needed to do that is written down here on purpose.
    // AppendFixtureSpecification() repeats this text into the result file when a fixture is missing - keep the
    // two in sync.
    //
    // WHERE          <project root>/zepeto-live-a.fbx        (beside zepeto-livereload.trigger)
    //                <project root>/zepeto-live-b.fbx
    //                NOT in Assets/CustomMotions. That folder is the live watcher's polling root, so a fixture
    //                parked there would make the watcher fire on the fixture instead of on the copy this run
    //                places at Assets/CustomMotions/LiveRoundTrip.fbx, and the measurement would be a lie.
    //
    // CONTENT        A: 48 frames  (frame_start 1 -> frame_end 48 at 24 fps, ~1.96s)
    //                   ONLY the RIGHT ARM moves. Rotate upperArm_R (and optionally lowerArm_R / hand_R).
    //                   Every leg bone stays at rest.
    //                B: 96 frames  (frame_start 1 -> frame_end 96 at 24 fps, ~3.96s)
    //                   ONLY the LEFT LEG moves. Rotate upperLeg_L about Z (lowerLeg_L / foot_L optional).
    //                   Every arm bone stays at rest.
    //
    // WHY DIFFERENT BONES
    //                This is the whole reason the run can conclude anything. The probe samples the right hand
    //                AND the left knee in both phases:
    //                  phase A: arm moves, leg still  -> playback works at all
    //                  phase B: leg moves, arm still  -> the swap reached the ALREADY RUNNING Animator
    //                With the same bone in both fixtures, "the hot reload never happened" and "nothing is
    //                animating at all" produce the identical reading - a still bone - and the run could not
    //                tell them apart. The two failure messages in Report() depend on this split.
    //
    // WHY DIFFERENT FRAME COUNTS
    //                The clip-swapped assertion compares the live clip's length before and after the swap and
    //                needs the difference to exceed 0.5s. 48 vs 96 frames gives ~2.0s of margin. Two fixtures
    //                of equal length would leave that assertion unable to fail.
    //
    // HOW TO PRODUCE (BlenderMotion/zepeto_motion_helper.py, the add-on the live loop is built around)
    //                1. Export the rig first: helper window -> "ZEPETO 리그 내보내기" (or drop
    //                   zepeto-rig-export.trigger). That writes Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx.
    //                2. In Blender, install/enable zepeto_motion_helper.py and press "ZEPETO 몸 불러오기" with
    //                   that fbx selected. Scene fps must be 24 - the add-on refuses to export otherwise.
    //                3. Set the frame range to 1..48. Rotate ONLY upperArm_R (select the bone, R, Z, move the
    //                   mouse, left click) and press "현재 포즈 저장" on at least two frames - e.g. 1, 24, 48.
    //                   Two keyframes is the add-on's minimum; a single key exports a static pose and the
    //                   avatar would not move at all.
    //                4. Press "처음과 끝 맞추기" so the motion loops instead of freezing after one pass.
    //                5. Set 이름 = zepeto-live-a and 폴더 = the project ROOT (not Assets/CustomMotions, see
    //                   WHERE above), then press "Unity로 보내기". The add-on writes zepeto-live-a.fbx.part and
    //                   renames it atomically, which is the same two-sided .part contract Unity's watcher skips.
    //                6. Press "포즈 전부 되돌리기", set the frame range to 1..96, and repeat steps 3-5 rotating
    //                   ONLY upperLeg_L, keying frames 1 / 48 / 96, with 이름 = zepeto-live-b.
    //                Both exports must include the skinned mesh (the add-on selects ARMATURE + MESH for exactly
    //                this reason): without it Unity builds a generic avatar, isHuman is false, and no Humanoid
    //                clip is generated at all.
    // ---------------------------------------------------------------------------------------------------------
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
        // Never written by this run; deliberately absent from ResetPerRunKeys() for that reason.
        private const string TravelKey = "Easy.ZepetoHelper.LiveRun.Travel";
        private const string MinKey = "Easy.ZepetoHelper.LiveRun.Min";
        private const string MaxKey = "Easy.ZepetoHelper.LiveRun.Max";

        // Frame counts of the two fixtures, quoted in the report and in the missing-fixture specification.
        private const int FixtureAFrames = 48;
        private const int FixtureBFrames = 96;

        // [QC][Invariant:travel_key_table_is_the_reset_list]
        // Every min/max key this run touches is MinKey|MaxKey + ".<phase>.<bone>.<axis>", assembled from these
        // three tables and nothing else. SessionState has no enumeration API, so there is no wildcard erase and
        // no way to discover last run's keys at runtime - the sampler (Track), the reader (Travel) and the
        // per-run reset (ResetPerRunKeys) all walk these same arrays, which is what makes the reset provably
        // complete. Adding a phase, a bone or an axis here is therefore the ONLY supported way to add one.
        private const string PhaseA = "a";
        private const string PhaseB = "b";
        private const string BoneArm = "arm";
        private const string BoneLeg = "leg";
        private static readonly string[] Phases = { PhaseA, PhaseB };
        private static readonly string[] Bones = { BoneArm, BoneLeg };

        // Listed in the same order as Vector3's own indexer (x=0, y=1, z=2); Track() indexes the vector with it.
        private static readonly string[] Axes = { "x", "y", "z" };

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

        /// <summary>
        /// Writes the missing-fixture failure AND everything needed to create the fixtures. "FATAL: fixtures
        /// missing" on its own told the reader nothing: not where the files go, not what has to be inside them,
        /// not that the two must move DIFFERENT bones for the result to mean anything.
        /// Mirrors the FIXTURE SPECIFICATION comment block at the top of this file - keep the two in sync.
        /// </summary>
        private static void AppendFixtureSpecification()
        {
            Append("FATAL: fixtures missing - nothing was measured.");
            Append("  " + Path.GetFullPath(FixtureA) + " : " + (File.Exists(FixtureA) ? "present" : "MISSING"));
            Append("  " + Path.GetFullPath(FixtureB) + " : " + (File.Exists(FixtureB) ? "present" : "MISSING"));
            Append(string.Empty);
            Append("required content");
            Append("  " + FixtureA + " : " + FixtureAFrames + " frames (1.." + FixtureAFrames
                + " at 24 fps). ONLY the RIGHT ARM moves - rotate upperArm_R; every leg bone stays at rest.");
            Append("  " + FixtureB + " : " + FixtureBFrames + " frames (1.." + FixtureBFrames
                + " at 24 fps). ONLY the LEFT LEG moves - rotate upperLeg_L about Z; every arm bone stays at rest.");
            Append("  Both must contain the skinned mesh as well as the armature, or Unity builds a generic");
            Append("  avatar (isHuman=false) and generates no Humanoid clip at all.");
            Append(string.Empty);
            Append("why the two fixtures must move DIFFERENT bones");
            Append("  The probe samples the right hand AND the left knee in both phases:");
            Append("    phase A: arm moves, leg still -> playback works at all");
            Append("    phase B: leg moves, arm still -> the swap reached the ALREADY RUNNING Animator");
            Append("  With the same bone in both fixtures, 'the hot reload never happened' and 'nothing is");
            Append("  animating at all' both read as a still bone and cannot be told apart - which is exactly");
            Append("  the distinction this run exists to make.");
            Append("  The frame counts must differ too: the clip-swapped assertion needs the live clip's length");
            Append("  to change by more than 0.5s, and " + FixtureAFrames + " vs " + FixtureBFrames
                + " frames gives ~2.0s of margin.");
            Append(string.Empty);
            Append("where they go");
            Append("  Beside " + TriggerPath + " at the project root - NOT in Assets/CustomMotions. That folder");
            Append("  is the live watcher's polling root, so a fixture parked there would make the watcher fire");
            Append("  on the fixture instead of on this run's own copy at " + WatchedAsset + ".");
            Append(string.Empty);
            Append("how to produce them (BlenderMotion/zepeto_motion_helper.py)");
            Append("  1. Export the rig: helper window -> 'ZEPETO 리그 내보내기' (or drop");
            Append("     zepeto-rig-export.trigger). Writes Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx.");
            Append("  2. In Blender enable zepeto_motion_helper.py, press 'ZEPETO 몸 불러오기' with that fbx.");
            Append("     Scene fps must be 24 - the add-on refuses to export otherwise.");
            Append("  3. Frame range 1.." + FixtureAFrames + ". Rotate ONLY upperArm_R (select bone, R, Z, move,");
            Append("     left click) and press '현재 포즈 저장' on at least two frames, e.g. 1 / 24 / "
                + FixtureAFrames + ".");
            Append("  4. Press '처음과 끝 맞추기' so the motion loops instead of freezing after one pass.");
            Append("  5. Set 이름 = zepeto-live-a and 폴더 = the project ROOT, then 'Unity로 보내기'.");
            Append("  6. Press '포즈 전부 되돌리기', set the frame range to 1.." + FixtureBFrames + ", and repeat");
            Append("     steps 3-5 rotating ONLY upperLeg_L, keying 1 / " + (FixtureBFrames / 2) + " / "
                + FixtureBFrames + ", 이름 = zepeto-live-b.");
        }

        /// <summary>
        /// Clears every SessionState key this run writes, so a second run in the same editor session cannot read
        /// the first run's numbers. RunningKey, PhaseKey and ClockKey are re-seeded at the end of
        /// StartIfRequested instead, next to the code that arms the run.
        ///
        /// [AUDIT][Risk:Critical][Scope:live_roundtrip_measurement]
        /// The arm/leg travel figures this run prints are quoted elsewhere as measured evidence that a hot
        /// reload reached a running Animator. Finish() clears only RunningKey, so before this reset existed a
        /// second run in the same session had Travel() read the FIRST run's extremes: it could report movement
        /// that never happened, and PASS on a build where the avatar never moved.
        /// </summary>
        private static void ResetPerRunKeys()
        {
            SessionState.SetFloat(LenBeforeKey, -1f);
            SessionState.SetInt(CountBeforeKey, -1);

            for (int p = 0; p < Phases.Length; p++)
            {
                for (int b = 0; b < Bones.Length; b++)
                {
                    for (int a = 0; a < Axes.Length; a++)
                    {
                        string suffix = "." + Phases[p] + "." + Bones[b] + "." + Axes[a];

                        // Seeded to the same sentinels TrackAxis and Travel already agree on: Mathf.Min against
                        // float.MaxValue and Mathf.Max against float.MinValue both take the first real sample,
                        // and Travel reads the pair as "never sampled" until one arrives. Assigning the
                        // sentinels rather than erasing the keys keeps that one convention in one place.
                        SessionState.SetFloat(MinKey + suffix, float.MaxValue);
                        SessionState.SetFloat(MaxKey + suffix, float.MinValue);
                    }
                }
            }
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

            // Before anything can be sampled, not just before Play: every phase x bone x axis extreme left by an
            // earlier run in this editor session has to be gone, or this run reports that run's numbers as its own.
            ResetPerRunKeys();

            if (!File.Exists(FixtureA) || !File.Exists(FixtureB))
            {
                AppendFixtureSpecification();
                Debug.LogError("ZEPETO live reload run: 측정용 FBX 두 개가 없어서 실행하지 못했습니다. "
                    + "만드는 방법을 " + ResultPath + " 에 적어두었습니다.");
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
                    SampleAvatar(PhaseA);
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
                Append("--- swapped fixture B in (" + FixtureBFrames + " frames) ---");

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

                SampleAvatar(PhaseB);

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
                Track(phasePrefix + "." + BoneLeg, knee.position - hips.position);
            }

            if (hand != null)
            {
                Track(phasePrefix + "." + BoneArm, hand.position - hips.position);
            }
        }

        private static void Track(string key, Vector3 value)
        {
            for (int i = 0; i < Axes.Length; i++)
            {
                TrackAxis(key + "." + Axes[i], value[i]);
            }
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
            for (int i = 0; i < Axes.Length; i++)
            {
                float min = SessionState.GetFloat(MinKey + "." + key + "." + Axes[i], float.MaxValue);
                float max = SessionState.GetFloat(MaxKey + "." + key + "." + Axes[i], float.MinValue);
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

            float armA = Travel(PhaseA + "." + BoneArm);
            float legA = Travel(PhaseA + "." + BoneLeg);
            float armB = Travel(PhaseB + "." + BoneArm);
            float legB = Travel(PhaseB + "." + BoneLeg);

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
