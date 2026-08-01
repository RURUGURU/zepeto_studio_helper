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
    /// 생성된 애니메이션 FBX를 움직이는 아바타까지 끌고 가서, 정말 움직였는지를 증명한다. "클립이
    /// 연결됐다"를 "아바타가 재생한다"로 믿지 않고 Play 도중 Humanoid 손 뼈를 직접 샘플링한다.
    ///
    /// 증명하려면 사용자의 실제 작업 씬이 필요하다. 그래서 이 러너는 그 씬을 가져가는 것이 아니라
    /// 빌린다. 덮어쓰는 참조는 전부 Play 전에 SessionState로 스냅샷하고, 결과를 보고하는 바로 그
    /// Play 이후 경로에서 되돌린다. RestoreSnapshot 참조.
    /// </summary>
    public static class ZepetoCustomMotionRun
    {
        private const string TriggerPath = "zepeto-custom-motion.trigger";
        private const string ReportPath = "zepeto-custom-motion.report.txt";
        private const string SkipPath = "zepeto-custom-motion.skipped.txt";
        private const string WorkScenePath = "Assets/Playground.unity";
        private const string RunnerLabel = "ZEPETO custom motion run";
        // 헬퍼의 CustomMotionRoot와 같은 경로. LiveWatchRoot(Assets/CustomMotions)도 아니고
        // AnimationCopyRoot(Assets/ZepetoHelper/Animations)도 아니다. 셋은 서로 다른 소유자를 가진
        // 서로 다른 루트이고 섞으면 안 된다. 여기는 추출된 .anim이 떨어지는 곳이다.
        private const string MotionRoot = "Assets/ZepetoHelper/Motions";
        private const string RunningKey = "Easy.ZepetoHelper.CustomMotion.Running";
        private const string PhaseKey = "Easy.ZepetoHelper.CustomMotion.Phase";
        private const string StartKey = "Easy.ZepetoHelper.CustomMotion.Start";
        private const string MinKey = "Easy.ZepetoHelper.CustomMotion.HandMin";
        private const string MaxKey = "Easy.ZepetoHelper.CustomMotion.HandMax";
        // 집계도 SessionState에 둔다. 검사 두 개는 Play 진입 전에, 하나는 Play 안에서 결정되는데 그
        // 사이에 도메인 리로드가 있어 정적 카운터는 살아남지 못한다.
        private const string PassCountKey = "Easy.ZepetoHelper.CustomMotion.Pass";
        private const string FailCountKey = "Easy.ZepetoHelper.CustomMotion.Fail";

        // 이 실행이 사용자 프로젝트에 쓰는 모든 것의 되돌리기 스냅샷. 필드도 EditorPrefs도 아닌
        // SessionState인 이유: 복원은 Play 진입이라는 도메인 리로드 건너편에서 일어나고, 이전 에디터
        // 세션에서 물려받은 스냅샷은 이미 열려 있지도 않은 씬을 설명하게 된다.
        // 참조마다 경로와 이름을 함께 적는다. 클립은 fbx의 서브 에셋일 수 있고 LoadAssetAtPath는 그중
        // 먼저 오는 것을 돌려주기 때문이다. 헬퍼의 MotionPreviewRestore* 쌍이 존재하는 이유와 같다.
        private const string SnapshotKey = "Easy.ZepetoHelper.CustomMotion.Snapshot";
        private const string ClipPathKey = "Easy.ZepetoHelper.CustomMotion.ClipBefore.Path";
        private const string ClipNameKey = "Easy.ZepetoHelper.CustomMotion.ClipBefore.Name";
        private const string ControllerPathKey = "Easy.ZepetoHelper.CustomMotion.ControllerBefore.Path";
        private const string ControllerNameKey = "Easy.ZepetoHelper.CustomMotion.ControllerBefore.Name";
        private const string OverrideControllerKey = "Easy.ZepetoHelper.CustomMotion.OverridesBefore.Controller";
        private const string OverrideSlotsKey = "Easy.ZepetoHelper.CustomMotion.OverridesBefore.Slots";
        // 이 실행이 새로 만들어 낸 .anim의 경로. TryExtractMotionFromFbx는
        // AssetDatabase.GenerateUniqueAssetPath를 쓰므로 실행할 때마다 'Wave_Hello 1.anim' 같은 파일이
        // 하나씩 새로 생긴다. 그대로 두면 실행 횟수만큼 쌓이고, 자기 테스트가 추적하는
        // 'NOTE catalog:count :: 13 motions'까지 매번 밀려 올라간다. 추출 전후의 폴더 목록 차이로
        // 구한 경로만 적는다 - 이름으로 고르면 사용자가 원래 갖고 있던 클립을 지울 수 있다.
        private const string ExtractedClipKey = "Easy.ZepetoHelper.CustomMotion.ExtractedClip";
        // 이 실행이 작업 씬 파일을 이미 썼는지. 복원의 마무리 방식을 정한다. 다시 저장하면 그 쓰기가
        // 취소되지만, 한 번도 쓰지 않은 실행이 처음으로 쓰는 쪽이 되어서는 안 된다.
        private const string SceneSavedKey = "Easy.ZepetoHelper.CustomMotion.SceneSaved";

        private const double PlaySeconds = 18d;
        private const int Serial = 21;

        private static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static bool hooked;

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            // 동기적으로 다시 무장한다. delayCall은 플레이 모드 도메인 리로드를 가로질러 안정적으로
            // 펌프되지 않아서, 예전에는 아무도 몰지 않는 Play에 실행이 영원히 갇히곤 했다.
            if (SessionState.GetBool(RunningKey, false)) { Hook(); }
            EditorApplication.delayCall += Bootstrap;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnReload() { EditorApplication.delayCall += Bootstrap; }

        private static void Bootstrap()
        {
            if (SessionState.GetBool(RunningKey, false)) { Hook(); return; }

            // 실행이 끝나는 시점에 못 한 복원 - 에디터가 아직 Play를 빠져나오는 중이었거나 스크립트
            // 리로드가 끼어들었거나 - 은 로드할 때마다 다시 시도한다. 무장된 스냅샷이 자기 실행보다
            // 오래 살아남아 사용자 씬에 이 실행의 클립을 남겨 두는 일이 없게 하려는 것이다.
            if (SessionState.GetBool(SnapshotKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RestoreSnapshot();
                ReportStageFlagRestore();
            }

            if (!File.Exists(TriggerPath)) { return; }
            // 이 드라이버는 스스로 Play에 들어가므로 편집 모드에서 시작해야 한다. 조용히 return하면
            // 사용자에게는 트리거 파일이 아무 일도 하지 않는 것처럼 보이므로 기다린다고 알린다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ZepetoSelfTestSceneGuard.WaitForEditMode(RunnerLabel, Bootstrap);
                return;
            }

            string fbx = File.ReadAllText(TriggerPath).Trim();
            File.Delete(TriggerPath);
            Run(fbx);
        }

        private static void Run(string fbxPath)
        {
            File.WriteAllText(ReportPath, "custom motion end-to-end\nfbx = " + fbxPath + "\n----\n");
            SessionState.SetInt(PassCountKey, 0);
            SessionState.SetInt(FailCountKey, 0);

            if (!File.Exists(fbxPath))
            {
                Append("FATAL: fbx not found");
                return;
            }

            // 이 드라이버는 Assets/Playground.unity를 열려 있는 것 위에 열고 저장까지 한다. 그래서 열린
            // 씬의 저장하지 않은 편집은 한마디 없이 사라진다 - 트리거 파일 하나가 그것을 없애기에
            // 충분했다. 그래서 거부한다. 일부러 대화상자를 띄우지 않고(이 러너는 비대화식이어야 한다)
            // 일부러 사용자 대신 저장도 하지 않는다. 남의 미완성 씬을 저장해 버리는 것도 똑같이 나쁘다.
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
            // 되돌리기 스냅샷은 무엇이든 기록되기 전인 여기서 비운다. 아래에서 중단되는 실행이 "이전"
            // 실행의 스냅샷을 무장된 채로 남겨 다음 Finish가 그것을 "복원"하는 일이 없게 한다.
            ClearSnapshot();

            // 사용자의 작업 단계 진행 상황도 이 실행이 건드리는 것에 포함된다. AssignAnimationClip이
            // 클립 단계 완료를 내리고 그 플래그는 아래 단계로 연쇄한다. 테스트 한 번 돌렸다고 헬퍼가
            // 1번 카드로 돌아가 있으면 안 되므로 실행 전 값을 들고 있다가 끝에서 되돌린다.
            Append("stage flags: " + ZepetoSelfTestSceneGuard.CaptureWorkflowStageFlags());

            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

            Type helperType = typeof(ZepetoStudioHelperWindow);
            ScriptableObject helper = ScriptableObject.CreateInstance(helperType);
            AnimationClip customClip = null;

            try
            {
                // [QC][Deliberate:permanent_importer_rewrite]
                // TryConfigureMotionFbx는 이 fbx의 .meta를 SaveAndReimport로 다시 쓴다(animationType,
                // avatarSetup, clipAnimations, Root Transform 고정). 아래 스냅샷은 LOADER 참조 두 개와
                // 오버라이드 표만 되돌리고 .meta는 되돌리지 않는다. 그것이 의도다. 임포터 설정을
                // 되돌리면 사용자가 이 fbx를 실제로 쓸 때의 상태에서 멀어지고, 이 단계 자체가 헬퍼가
                // 사용자에게 해 주는 일이다. 영구 변경이라는 사실만 여기 적어 둔다.
                object[] args = new object[] { fbxPath, null };
                bool configured = (bool)helperType.GetMethod("TryConfigureMotionFbx", Inst).Invoke(helper, args);
                Check("custom-motion:configured", configured, "TryConfigureMotionFbx failed :: " + args[1]);
                Append("step 1 configure -> " + configured + " : " + args[1]);

                // Unity가 이 리그로 정말 유효한 Humanoid 아바타를 만들었는가?
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                Append("animationType = " + (importer == null ? "?" : importer.animationType.ToString()));
                if (importer != null)
                {
                    // 왕복의 핵심: 진짜 ZEPETO 모델의 아바타를 거쳐 리타깃한다.
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

                // 추출 직전의 폴더 목록. 이 실행이 "새로" 만든 클립을 이름 규칙이 아니라 차집합으로
                // 알아내기 위한 것이고, 정리할 때 사용자 소유 클립을 건드리지 않는 근거이기도 하다.
                HashSet<string> clipsBefore = CollectMotionClipPaths();

                object[] extractArgs = new object[] { fbxPath, null };
                bool extracted = (bool)helperType.GetMethod("TryExtractMotionFromFbx", Inst).Invoke(helper, extractArgs);
                Check("custom-motion:extracted", extracted, "TryExtractMotionFromFbx failed :: " + extractArgs[1]);
                Append("step 2 extract -> " + extracted + " : " + extractArgs[1]);
                if (!extracted) { return; }

                string createdClipPath = FirstNewClipPath(clipsBefore);
                if (!string.IsNullOrEmpty(createdClipPath))
                {
                    SessionState.SetString(ExtractedClipKey, createdClipPath);
                    customClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(createdClipPath);
                    Append("extracted clip = " + createdClipPath + " (removed again when the run restores)");
                }

                if (customClip == null)
                {
                    // 차집합이 비어 있는 경우(추출이 기존 파일을 덮어썼거나 폴더를 읽지 못한 경우)의
                    // 대비책. 이 fbx에서 나온 클립을 이름으로 고른다. 폴더에 굴러다니는 아무 클립이
                    // 아니라는 점이 중요하다.
                    string expected = Path.GetFileNameWithoutExtension(fbxPath);
                    string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { MotionRoot });
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
                }

                if (customClip == null) { Append("FATAL: no extracted clip"); return; }
                Append("clip = " + customClip.name + " length=" + customClip.length.ToString("0.00")
                    + "s humanoid=" + customClip.isHumanMotion);

                EditorSceneManager.OpenScene(WorkScenePath, OpenSceneMode.Single);
                helperType.GetMethod("FindLoaderAndSerializedFields", Inst).Invoke(helper, null);

                // [QC][Invariant:runner_restores_what_it_changed]
                // 여기서부터는 전부 사용자의 작업 씬에 쓴다. AssignAnimationClip ->
                // ApplyClipToOverrideController -> AssetDatabase.SaveAssets를 거쳐 프로젝트 에셋에도
                // 쓰고, 그다음 SaveScene이 그것을 디스크에 올린다. Play를 나온다고 취소되는 것은 하나도
                // 없다. 그러니 먼저 스냅샷한다. 되돌릴 수 없는 것을 바꾸느니 실행을 거부하는 쪽이
                // 낫기 때문에, 스냅샷을 못 뜨면 실행을 중단한다. 어차피 바인딩이 없으면
                // AssignAnimationClip도 실패한다.
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

                // 지금에서야 스냅샷하는 이유: 바로 위의 로컬 복사 단계가 "어느" 컨트롤러의 오버라이드
                // 표가 다시 쓰일지를 정한다. 더 일찍 떴으면 엉뚱한 에셋을 기록하게 된다.
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

                // Play로 넘기는 것은 위 블록의 마지막 문장인 SaveScene 직후다. 그래서 여기서 플래그가
                // false라면 실행이 중단된 것이다 - 예컨대 헬퍼 안에서 예외가 났다. 지금 되돌린다.
                // 다른 누구도 되돌리지 않기 때문이다. Finish()가 유일한 다른 복원 경로인데 거기는 Play를
                // 거쳐야만 닿는다.
                if (!SessionState.GetBool(SceneSavedKey, false))
                {
                    RestoreSnapshot();
                    DeleteExtractedClip(true);
                    ReportStageFlagRestore();
                }
            }

            // [QC][Invariant:every_session_key_reseeded_per_run]
            // SessionState는 에디터 세션 범위라, 여기서 다시 심지 않는 값은 같은 세션의 "이전" 실행에서
            // 물려받은 값이다. StartKey가 예외였고 그래서 이 러너는 한 번밖에 못 도는 물건이었다.
            // 아래 "entering" 분기가 첫 실행의 도장을 읽어 몇 분 전 시계로 since를 계산하고, Play를
            // 관측하기도 전에 Finish("play mode never became active")를 불렀다. 0이 그 분기가 찾는
            // "아직 도장 안 찍힘" 센티널이다.
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
                    // 오지 않을 Play 전환을 영원히 기다리지 않는다.
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

            // Humanoid 오른손의 높이를 샘플링한다. 정지 포즈면 이 값이 그대로고, 진짜 손 흔들기면 아니다.
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
            Check("custom-motion:avatar-animating", travel > 0.05f,
                "the right hand moved " + travel.ToString("0.000") + "m relative to the hips, so the avatar is "
                + "NOT performing the custom motion");

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
            Append("---- results ----");
            Append("pass=" + SessionState.GetInt(PassCountKey, 0) + " fail=" + SessionState.GetInt(FailCountKey, 0));
            SessionState.SetBool(RunningKey, false);
            SessionState.SetString(PhaseKey, string.Empty);
            // 시계도 함께 지운다. 시작만 하고 Run()에 닿지 못한 실행이나, 다시 심는 것을 잊은 미래의
            // 호출자가 이 실행의 만료 시각을 물려받지 못하게.
            SessionState.SetFloat(StartKey, 0f);

            // 위에서 실행 상태를 먼저 지운 덕분에 씬을 쓰는 동안 Tick이 다시 들어올 수 없다.
            // Finish에 닿는 모든 경로는 이미 !EditorApplication.isPlaying을 확인했으므로 여기 쓰기는
            // 전부 편집 모드 쓰기다. 오버라이드 표를 여기서 다시 쓰는 것이 안전하고 Play 도중에는
            // 절대 안전하지 않은 이유가 그것이다.
            RestoreSnapshot();
            ReportStageFlagRestore();

            EditorApplication.update -= Tick;
            hooked = false;
            Debug.Log("ZEPETO custom motion run finished: " + reason);
        }

        private static void Check(string name, bool condition, string failDetail)
        {
            if (condition)
            {
                SessionState.SetInt(PassCountKey, SessionState.GetInt(PassCountKey, 0) + 1);
                Append("PASS " + name);
            }
            else
            {
                SessionState.SetInt(FailCountKey, SessionState.GetInt(FailCountKey, 0) + 1);
                Append("FAIL " + name + " :: " + failDetail);
            }
        }

        // ---------- 되돌리기 스냅샷 ----------

        /// <summary>
        /// 이 실행이 곧 덮어쓸 LOADER 참조 두 개를 기록한다. false는 읽지 못했다는 뜻이고, 그렇다면
        /// 호출자는 그것들을 쓰지도 말아야 한다.
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

            // 마지막 쓰기 뒤가 아니라 첫 쓰기인 EnsureLocalAnimatorController 앞에서 무장한다. 그
            // 사이에서 중단돼도 되돌릴 것이 남아 있게.
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
        /// 지금 LOADER가 가리키고 있는 컨트롤러의 오버라이드 표를 기록한다.
        ///
        /// 슬롯 "값"만 슬롯 순서대로 담는다. 키는 베이스 컨트롤러의 것이고 이 실행이 하는 어떤 일도
        /// 그것을 바꾸지 못하므로, 복원은 현재 키를 다시 읽어 값만 끼워 넣는다. 슬롯 개수가 달라진
        /// 경우가 조용히 잘못 대입되는 대신 잡히는 효과도 있다.
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

            // 컨트롤러가 아직 Packages/ 아래라면 EnsureLocalAnimatorController가 로컬 복사에 실패한
            // 것이고, ApplyClipToOverrideController는 패키지 에셋 쓰기를 거부하므로 되돌릴 것도 없다.
            // 그런데 기록해 두면 쓸모없는 정도가 아니라 해롭다. 복원의 끝은 SetDirty + SaveAssets이고,
            // 그것이 바로 헬퍼가 피하려고 존재하는 "SDK 자기 파일에 쓰기"다.
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
                // "path|name", 오버라이드가 없던 슬롯은 양쪽 다 빈 문자열. Unity가 지원하는 어떤
                // 플랫폼에서도 에셋 경로에 '|'가 들어갈 수 없으므로 디코딩은 "첫" '|'에서 자르고,
                // 이름 쪽에 '|'가 들어 있어도 왕복이 살아남는다.
                encoded.Append(value == null ? string.Empty : AssetDatabase.GetAssetPath(value));
                encoded.Append('|');
                encoded.Append(value == null ? string.Empty : value.name);
            }

            SessionState.SetString(OverrideControllerKey, AssetDatabase.GetAssetPath(controller));
            SessionState.SetString(OverrideSlotsKey, encoded.ToString());
            Append("snapshot override table: " + overrides.Count + " slot(s) on " + controller.name);
        }

        /// <summary>
        /// 작업 씬과 오버라이드 컨트롤러를 이 실행이 발견했던 상태로 되돌린다.
        ///
        /// 오버라이드 표를 먼저 되돌린다. AssetDatabase.SaveAssets 때문에 이미 디스크에 쓰인 부분이기
        /// 때문이다. 그리고 일부러 이전에 매핑돼 있던 것 - 보통 SDK의 A_pose - 을 그대로 되살린다.
        /// 포즈가 바람직해서가 아니라 그것이 실행 전 상태이기 때문이다.
        ///
        /// 지우는 것은 이 실행이 "새로 만든" 추출 클립 하나뿐이다. 이 실행이 만들어야 했던 로컬
        /// AnimatorOverrideController는 그대로 둔다. 참조를 되돌리는 것은 안전하지만, 다른 것이
        /// 가리키고 있을 수 있는 에셋을 지우는 것은 안전하지 않다.
        /// </summary>
        private static void RestoreSnapshot()
        {
            if (!SessionState.GetBool(SnapshotKey, false))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // 여기 있는 것은 무엇도 Play 도중에 돌면 안 된다. 오버라이드 표를 다시 쓰면 살아 있는
                // ZEPETO 아바타가 쓰는 Animator가 rebind되고 그 컨텍스트가 통째로 깨진다. Bootstrap이
                // 다음 로드에서 다시 시도하고 Play를 나오는 것도 로드 중 하나이므로, 스냅샷은 여기서
                // 버려지지 않고 무장된 채로 남는다.
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

            // 이 실행이 만든 클립은 참조가 전부 제자리로 돌아간 뒤에만 지운다. 복원이 실패했다면 씬이
            // 아직 그 클립을 가리키고 있을 수 있으므로 남겨 두고 그 사실을 리포트에 적는다.
            DeleteExtractedClip(failure == null);

            // 일부러 위 블록 안이 아니라 헬퍼가 사라진 뒤다. 헬퍼 창 인스턴스는 씬에 임시 대역 몸을
            // 넣었다가 파괴될 때 도로 빼는데, 그것이 방금 정리를 끝낸 씬에 "저장하지 않은 변경"
            // 플래그를 다시 세울 수 있다.
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
        /// 작업 씬을 활성 씬으로 만든다. 복원 대상인 LOADER가 그 안에 있고, 복원을 마무리하는 저장이
        /// 활성 씬을 쓰기 때문이다. 보통은 이미 활성이다 - Play를 나오면 Play에 들어갈 때의 씬이 그대로
        /// 돌아온다.
        /// </summary>
        private static bool TryFocusWorkSceneForRestore(out string failure)
        {
            failure = null;
            Scene active = EditorSceneManager.GetActiveScene();
            if (string.Equals(active.path, WorkScenePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 다른 씬이 열려 있으므로 복원은 그 위에 작업 씬을 다시 열어야 하는데, 그것이 바로 이
            // 러너가 저장 안 된 것이 있을 때 하지 않기로 한 동작이다.
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
        /// 씬의 저장 상태를 다시 사실로 만든다. 다시 저장하면 이 실행 자신의 SaveScene이 취소되고,
        /// 거기까지 못 간 실행에는 자기 쓰기가 세운 "저장하지 않은 변경" 플래그만 남으므로 파일을
        /// 건드리지 않고 그것만 내린다. 어느 쪽이든 dirty한 씬에서 전부 거부하는 형제 러너들이 막힌 채로
        /// 남지 않는다.
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

        /// <summary>
        /// MotionRoot 안의 AnimationClip 에셋 경로 전부. 추출이 무엇을 새로 만들었는지 차집합으로 알아내는
        /// 데만 쓴다.
        /// </summary>
        private static HashSet<string> CollectMotionClipPaths()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(MotionRoot))
            {
                return paths;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { MotionRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static string FirstNewClipPath(HashSet<string> before)
        {
            foreach (string path in CollectMotionClipPaths())
            {
                if (!before.Contains(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 이 실행이 만들어 낸 .anim을 지운다. 지우는 대상은 추출 전후 목록의 차집합으로 구한 경로
        /// 하나뿐이라, 사용자가 원래 갖고 있던 클립은 절대 후보가 되지 않는다.
        /// safeToDelete가 false면 - 복원이 완전히 끝나지 않아 씬이 아직 그 클립을 가리키고 있을 수 있는
        /// 경우 - 지우지 않고 어디에 남았는지 적는다.
        /// </summary>
        private static void DeleteExtractedClip(bool safeToDelete)
        {
            string path = SessionState.GetString(ExtractedClipKey, string.Empty);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            SessionState.SetString(ExtractedClipKey, string.Empty);

            if (!safeToDelete)
            {
                Append("  kept this run's extracted clip at " + path
                    + " because the restore did not complete - delete it by hand once the scene is back.");
                return;
            }

            if (!path.StartsWith(MotionRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                Append("  did NOT delete " + path + ": it is not under " + MotionRoot);
                return;
            }

            Append(AssetDatabase.DeleteAsset(path)
                ? "  removed this run's extracted clip: " + path
                : "  could not remove this run's extracted clip: " + path);
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
        /// 값을 헬퍼 자신의 SerializedProperty를 통해 그대로 되돌려 쓴다.
        ///
        /// AssignAnimationClip / EnsureLocalAnimatorController를 거치지 않는 것은 의도다. 둘 다 이 복원이
        /// 반드시 쓸 수 있어야 하는 값을 거부한다 - AssignAnimationClip은 null을 거부하는데, 손대지 않은
        /// LOADER가 들고 있는 값이 바로 그 null이다 - 게다가 AssignAnimationClip은 방금 되돌린 오버라이드
        /// 표를 곧바로 다시 써 버린다.
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
        /// 그 경로에 있는, 그 이름을 가진 에셋. 이름이 중요한 이유는 클립이 서브 에셋일 수 있기
        /// 때문이다 - fbx 안의 한 테이크이거나 SDK 에셋 안의 여러 클립 중 하나 - 그런 경우
        /// LoadAssetAtPath는 기록해 둔 그것이 아니라 먼저 오는 것을 돌려준다.
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
        /// 헬퍼 자신의 IsPackageOrPackageCachePath와 같은 규칙. 부분 문자열이 아니라 경로 조각으로 본다.
        /// Assets/MyPackageCache처럼 멀쩡히 쓸 수 있는 폴더가 억울하게 걸리지 않도록.
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
        /// 끝까지 되돌리지 못한 복원은 리포트뿐 아니라 콘솔에도 한국어로 알리고, 손으로 무엇을 되돌려야
        /// 하는지 이름을 대 준다. 침묵하면 사용자 씬이 이 실행의 클립을 든 채로 남고 그 사실을 말해 주는
        /// 것이 아무것도 없다.
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
            // 여기서도 지운다. RestoreSnapshot은 DeleteExtractedClip을 먼저 부르므로 이 줄이 그 삭제를
            // 앞지르지 않고, 실행 맨 앞에서는 이전 실행이 남긴 경로가 이번 실행의 정리 대상이 되는 것을
            // 막아 준다.
            SessionState.SetString(ExtractedClipKey, string.Empty);
        }

        /// <summary>
        /// 되돌린 것이 있을 때만 리포트에 남긴다. 잡아 둔 것이 없으면 한 줄도 쓰지 않는다 - 일어나지도
        /// 않은 실행의 리포트에 줄이 붙는 것을 막으려는 것이다.
        /// </summary>
        private static void ReportStageFlagRestore()
        {
            string detail = ZepetoSelfTestSceneGuard.RestoreWorkflowStageFlags();
            if (!string.IsNullOrEmpty(detail))
            {
                Append("stage flags restored: " + detail);
            }
        }

        private static void Append(string line)
        {
            try { File.AppendAllText(ReportPath, line + Environment.NewLine); } catch { }
        }
    }
}
