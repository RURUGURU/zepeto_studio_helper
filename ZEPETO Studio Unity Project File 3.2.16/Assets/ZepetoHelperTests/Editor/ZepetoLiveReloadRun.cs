using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Easy.ZepetoHelper.Editor;
using Easy.ZepetoHelper.SelfTestEditor;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Tests
{
    /// <summary>
    /// 라이브 프리뷰 왕복을 진짜 Play 세션 안에서 끝에서 끝까지 증명한다.
    ///
    /// 자기 테스트는 에셋 수준 불변식만 다룬다(CopySerialized가 GUID를 유지하고, 클립 내용이 바뀌고,
    /// 루프 플래그가 붙는다). 정작 중요한 질문은 다르다. 이미 "돌고 있는" Animator가 rebind 없이 새
    /// 모션으로 갈아타는가? 그건 재생 중에만 답할 수 있어서, 이 드라이버는 Play 도중에 두 번째 fbx를
    /// 밀어 넣고 - Blender에서 'Unity로 보내기'를 누르는 것과 똑같은 일이다 - 에셋이 아니라 아바타를
    /// 측정한다.
    ///
    /// 프로젝트 루트에 "zepeto-livereload.trigger"를 놓으면 실행된다. 아래 FIXTURE SPECIFICATION이
    /// 설명하는 픽스처 두 개가 트리거 옆, 프로젝트 루트에 있어야 한다.
    /// </summary>
    // ---------------------------------------------------------------------------------------------------------
    // FIXTURE SPECIFICATION - zepeto-live-a.fbx / zepeto-live-b.fbx 를 다시 만드는 방법
    //
    // 두 픽스처는 저장소에 넣지 않는다(fbx는 손으로 쓸 수 없는 바이너리이고 합쳐서 3.4MB다). 대신
    // 생성 스크립트를 추적한다. 필요한 내용은 전부 여기 적어 둔다.
    // 픽스처가 없을 때 AppendFixtureSpecification()이 같은 내용을 결과 파일에 다시 적는다 - 둘을 함께
    // 고쳐야 한다.
    //
    // WHERE          <project root>/zepeto-live-a.fbx        (zepeto-livereload.trigger 옆)
    //                <project root>/zepeto-live-b.fbx
    //                Assets/CustomMotions가 아니다. 그 폴더는 라이브 감시자의 폴링 루트라서, 픽스처를
    //                거기 두면 감시자가 이 실행이 Assets/CustomMotions/LiveRoundTrip.fbx에 놓는 복사본이
    //                아니라 픽스처 자체에 반응한다. 그러면 측정값이 거짓말이 된다.
    //
    // CONTENT        A: 48 frames  (frame_start 1 -> frame_end 48 at 24 fps, ~1.96s)
    //                   오른팔만 움직인다. upperArm_R을 회전(원하면 lowerArm_R / hand_R도).
    //                   다리 뼈는 전부 rest 상태 그대로.
    //                B: 96 frames  (frame_start 1 -> frame_end 96 at 24 fps, ~3.96s)
    //                   왼다리만 움직인다. upperLeg_L을 Z축으로 회전(lowerLeg_L / foot_L은 선택).
    //                   팔 뼈는 전부 rest 상태 그대로.
    //
    // WHY DIFFERENT BONES
    //                이 실행이 무언가를 결론지을 수 있는 이유 전부가 여기 있다. 프로브는 두 페이즈
    //                모두에서 오른손과 왼무릎을 함께 샘플링한다:
    //                  phase A: 팔이 움직이고 다리는 정지  -> 재생 자체가 되고 있다
    //                  phase B: 다리가 움직이고 팔은 정지  -> 교체가 이미 돌고 있던 Animator까지 닿았다
    //                두 픽스처가 같은 뼈를 움직이면 "핫 리로드가 아예 일어나지 않았다"와 "아무것도
    //                재생되고 있지 않다"가 똑같이 '멈춘 뼈'로 읽혀 구분할 수 없다. Report()의 실패
    //                메시지 두 개가 바로 이 구분에 기대고 있다.
    //
    // WHY DIFFERENT FRAME COUNTS
    //                clip-swapped 단언은 라이브 클립의 길이를 교체 전후로 비교하고 그 차이가 0.5초를
    //                넘어야 한다. 48 대 96 프레임이면 ~2.0초의 여유가 생긴다. 길이가 같은 픽스처 둘로는
    //                그 단언이 실패할 수 없게 된다.
    //
    // HOW TO PRODUCE 스크립트가 있다. BlenderMotion/make_live_fixtures.py가 이 명세를 그대로 구현한다
    //                (자기 docstring에 'a : 48 frames, ONLY upperArm_R ... b : 96 frames, ONLY
    //                upperLeg_L'). 루트 .gitignore에도 같은 두 명령이 적혀 있다:
    //                  set ZEPETO_FIXTURE=a   (그다음 b로 한 번 더)
    //                  blender.exe --background --factory-startup --python BlenderMotion/make_live_fixtures.py
    //                픽스처마다 새 Blender로 한 번씩 돌린다. 키프레임이 서로 새지 않게 하기 위해서다.
    //
    // 손으로 만드는 경우 (BlenderMotion/zepeto_motion_helper.py, 라이브 루프가 기대는 애드온)
    //                1. 리그부터 내보낸다: 헬퍼 창 -> "ZEPETO 리그 내보내기" (또는
    //                   zepeto-rig-export.trigger를 놓는다). Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx가
    //                   만들어진다.
    //                2. Blender에서 zepeto_motion_helper.py를 설치/활성화하고 그 fbx를 고른 채
    //                   "ZEPETO 몸 불러오기"를 누른다. 씬 fps는 반드시 24 - 아니면 애드온이 내보내기를
    //                   거부한다.
    //                3. 프레임 범위를 1..48로. upperArm_R만 회전하고(뼈 선택, R, Z, 마우스 이동, 좌클릭)
    //                   최소 두 프레임에서 "현재 포즈 저장"을 누른다 - 예: 1, 24, 48. 두 개가 애드온의
    //                   최소치이고, 키가 하나면 정지 포즈로 나가서 아바타가 전혀 움직이지 않는다.
    //                4. "처음과 끝 맞추기"를 눌러 한 바퀴 뒤에 얼어붙지 않고 루프하게 만든다.
    //                5. 이름 = zepeto-live-a, 폴더 = 프로젝트 ROOT(Assets/CustomMotions가 아니다, 위
    //                   WHERE 참조)로 두고 "Unity로 보내기". 애드온은 zepeto-live-a.fbx.part를 쓴 뒤
    //                   원자적으로 이름을 바꾼다. Unity 감시자가 건너뛰는 그 .part 계약의 반대편이다.
    //                6. "포즈 전부 되돌리기"를 누르고 프레임 범위를 1..96으로 바꾼 뒤, upperLeg_L만
    //                   회전하고 1 / 48 / 96에 키를 넣어 이름 = zepeto-live-b로 3-5를 반복한다.
    //                두 내보내기 모두 스킨드 메시를 포함해야 한다(애드온이 ARMATURE + MESH를 함께
    //                고르는 이유가 정확히 이것이다). 없으면 Unity가 제네릭 아바타를 만들어 isHuman이
    //                false가 되고 Humanoid 클립이 하나도 생성되지 않는다.
    // ---------------------------------------------------------------------------------------------------------
    [InitializeOnLoad]
    public static class ZepetoLiveReloadRun
    {
        private const string TriggerPath = "zepeto-livereload.trigger";
        private const string ResultPath = "zepeto-livereload.result.txt";
        private const string SkipPath = "zepeto-livereload.skipped.txt";
        private const string FixtureA = "zepeto-live-a.fbx";
        private const string FixtureB = "zepeto-live-b.fbx";
        private const string WatchedAsset = "Assets/CustomMotions/LiveRoundTrip.fbx";
        private const string RunnerLabel = "ZEPETO live reload run";

        private const string RunningKey = "Easy.ZepetoHelper.LiveRun.Running";
        private const string PhaseKey = "Easy.ZepetoHelper.LiveRun.Phase";
        private const string ClockKey = "Easy.ZepetoHelper.LiveRun.Clock";
        private const string LenBeforeKey = "Easy.ZepetoHelper.LiveRun.LenBefore";
        private const string CountBeforeKey = "Easy.ZepetoHelper.LiveRun.CountBefore";
        // 이 실행이 WatchedAsset을 실제로 놓았는지. 정리 시점은 Play를 나온 뒤라 도메인 리로드 건너편이고,
        // 그래서 정적 필드가 아니라 SessionState여야 한다. 사용자가 원래 갖고 있던 파일을 지우는 일이
        // 없도록, 놓은 실행만 치운다.
        private const string PlacedKey = "Easy.ZepetoHelper.LiveRun.PlacedWatchedAsset";
        private const string MinKey = "Easy.ZepetoHelper.LiveRun.Min";
        private const string MaxKey = "Easy.ZepetoHelper.LiveRun.Max";

        // 두 픽스처의 프레임 수. 리포트와 픽스처 명세 출력에 그대로 인용된다.
        private const int FixtureAFrames = 48;
        private const int FixtureBFrames = 96;

        // [QC][Invariant:travel_key_table_is_the_reset_list]
        // 이 실행이 건드리는 min/max 키는 전부 MinKey|MaxKey + ".<phase>.<bone>.<axis>"이고, 그 조합은
        // 아래 세 표에서만 나온다. SessionState에는 열거 API가 없어서 와일드카드 삭제도, 지난 실행의
        // 키를 런타임에 알아낼 방법도 없다. 샘플러(Track), 판독기(Travel), 실행별 초기화(ResetPerRunKeys)가
        // 모두 같은 배열을 도는 것이 초기화가 빠짐없음을 증명해 준다. 페이즈나 뼈나 축을 늘리는
        // 유일하게 지원되는 방법은 그래서 여기에 추가하는 것이다.
        private const string PhaseA = "a";
        private const string PhaseB = "b";
        private const string BoneArm = "arm";
        private const string BoneLeg = "leg";
        private static readonly string[] Phases = { PhaseA, PhaseB };
        private static readonly string[] Bones = { BoneArm, BoneLeg };

        // Vector3의 인덱서와 같은 순서(x=0, y=1, z=2). Track()이 이 인덱스로 벡터를 읽는다.
        private static readonly string[] Axes = { "x", "y", "z" };

        private const float SettleSeconds = 12f;   // 아바타 다운로드 + 첫 프레임들
        private const float ObserveSeconds = 8f;   // 교체된 모션이 재생되는 것을 지켜보는 시간

        private static readonly BindingFlags Inst =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags Stat =
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static bool hooked;

        /// <summary>
        /// 이 실행이 만든 PASS/FAIL 줄과 집계. Play 진입과 종료가 각각 도메인 리로드라서 이 정적 상태는
        /// Play 세션 하나 안에서만 산다 - 세 검사가 전부 그 안에서 결정되고 Report()가 그 안에서 집계를
        /// 쓰기 때문에 그것으로 충분하다. Finish()는 Play를 나온 뒤라 여기에 아무것도 없다.
        /// </summary>
        private static readonly List<string> results = new List<string>();
        private static int passCount;
        private static int failCount;

        private static string liveReloadCountKey;
        private static string liveArmedKey;
        private static string liveMessageKey;

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
            try { File.AppendAllText(ResultPath, line + Environment.NewLine); } catch { }
        }

        /// <summary>
        /// 되돌린 것이 있을 때만 결과 파일에 남긴다. 잡아 둔 것이 없으면 한 줄도 쓰지 않는다 - 결과
        /// 파일은 마지막 실제 실행의 기록이어야 한다.
        /// </summary>
        private static void ReportStageFlagRestore()
        {
            string detail = ZepetoSelfTestSceneGuard.RestoreWorkflowStageFlags();
            if (!string.IsNullOrEmpty(detail))
            {
                Append("stage flags restored: " + detail);
            }
        }

        /// <summary>
        /// 픽스처가 없다는 실패와, 그 픽스처를 만드는 데 필요한 전부를 함께 쓴다. "FATAL: fixtures
        /// missing" 한 줄은 읽는 사람에게 아무것도 알려 주지 않았다. 파일을 어디 둬야 하는지도, 안에
        /// 무엇이 들어 있어야 하는지도, 두 파일이 서로 "다른" 뼈를 움직여야 결과가 의미를 갖는다는
        /// 사실도.
        /// 이 파일 맨 위의 FIXTURE SPECIFICATION 주석과 같은 내용이다 - 둘을 함께 고쳐야 한다.
        /// </summary>
        private static void AppendFixtureSpecification()
        {
            Append("FATAL: fixtures missing - nothing was measured.");
            Append("  " + Path.GetFullPath(FixtureA) + " : " + (File.Exists(FixtureA) ? "present" : "MISSING"));
            Append("  " + Path.GetFullPath(FixtureB) + " : " + (File.Exists(FixtureB) ? "present" : "MISSING"));
            Append(string.Empty);
            Append("the short way: BlenderMotion/make_live_fixtures.py implements this specification");
            Append("  set ZEPETO_FIXTURE=a   (then once more with b)");
            Append("  blender.exe --background --factory-startup --python BlenderMotion/make_live_fixtures.py");
            Append("  One fresh Blender per fixture, so no keyframes leak between them.");
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
            Append("by hand, if the script cannot be used (BlenderMotion/zepeto_motion_helper.py)");
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
        /// 이 실행이 쓰는 SessionState 키를 전부 지운다. 같은 에디터 세션의 두 번째 실행이 첫 실행의
        /// 숫자를 읽지 못하게 하기 위해서다. RunningKey / PhaseKey / ClockKey는 대신 StartIfRequested의
        /// 끝, 실행을 무장하는 코드 바로 옆에서 다시 심는다.
        ///
        /// [AUDIT][Risk:Critical][Scope:live_roundtrip_measurement]
        /// 이 실행이 찍는 팔/다리 이동 거리 수치는 "핫 리로드가 돌고 있는 Animator에 닿았다"는 측정
        /// 증거로 다른 문서에 인용된다. Finish()는 RunningKey만 지우기 때문에, 이 초기화가 생기기 전에는
        /// 같은 세션의 두 번째 실행에서 Travel()이 "첫" 실행의 극값을 읽었다. 일어나지도 않은 움직임을
        /// 보고할 수 있었고, 아바타가 전혀 움직이지 않는 빌드에서도 PASS가 났다.
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

                        // TrackAxis와 Travel이 이미 합의하고 있는 것과 같은 센티널로 심는다.
                        // float.MaxValue에 대한 Mathf.Min과 float.MinValue에 대한 Mathf.Max는 첫 실제
                        // 샘플을 그대로 받아들이고, Travel은 하나라도 들어오기 전까지 그 쌍을 "샘플된 적
                        // 없음"으로 읽는다. 키를 지우는 대신 센티널을 넣는 쪽이 그 규약을 한곳에 모아 둔다.
                        SessionState.SetFloat(MinKey + suffix, float.MaxValue);
                        SessionState.SetFloat(MaxKey + suffix, float.MinValue);
                    }
                }
            }
        }

        /// <summary>
        /// 헬퍼가 라이브 상태를 담아 두는 SessionState 키 문자열. 상수 "이름"으로 리플렉션해서 값을
        /// 읽어 온다.
        ///
        /// 예전에는 키 문자열을 여기에 그대로 적어 두었다. 그러면 헬퍼가 이름을 바꿨을 때 컴파일은
        /// 통과하고 ReloadCount()가 영원히 -1을 돌려줘서, 멀쩡한 빌드를 상대로 '감시자가 25초 안에
        /// 반응하지 않았다'는 거짓 FAIL이 났다. null이면 아래에서 실행을 아예 시작하지 않는다.
        /// </summary>
        private static string ResolveHelperSessionKey(ref string cache, string constName)
        {
            if (!string.IsNullOrEmpty(cache))
            {
                return cache;
            }

            FieldInfo field = typeof(ZepetoStudioHelperWindow).GetField(constName, Stat);
            cache = field == null ? null : field.GetValue(null) as string;
            return cache;
        }

        private static void StartIfRequested()
        {
            if (!File.Exists(TriggerPath))
            {
                return;
            }

            // 이 드라이버는 임포터를 설정하고 스스로 Play에 들어가므로 편집 모드에서 시작해야 한다.
            // 이미 돌고 있는 Play 세션 중에 발동하면 Play 도중에 임포터 설정을 쓰면서 그 세션과 싸우게
            // 된다. 트리거는 그대로 두고 사용자가 Stop을 누르면 시작한다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ZepetoSelfTestSceneGuard.WaitForEditMode(RunnerLabel, StartIfRequested);
                return;
            }

            File.Delete(TriggerPath);

            // [QC][Invariant:never_touch_an_unsaved_open_scene]
            // 넷 중 가장 침습적인 러너인데 이 거부가 없었다. 아래는 Assets/CustomMotions에 fbx를 복사하고
            // 폴더 전체의 임포터를 다시 쓰고 EditorApplication.isPlaying = true로 들어간다. 저장하지 않은
            // 씬 편집이 있는 상태로 Play에 들어가면 Unity는 묻지도 않고 그것을 버린다.
            // report 콜백을 넘기지 않는 이유: 여기서는 ResultPath가 아직 지난 실행의 기록이고, 거부가
            // 거기에 덧붙으면 그 파일이 더 이상 "마지막 실제 실행"이 아니게 된다. 거부는 SkipPath에만
            // 남는다. 트리거는 형제 러너들과 같게 이미 소비했다 - 그래야 다음 리로드에서 이 실행이
            // 예고 없이 Play에 들어가지 않는다.
            if (ZepetoSelfTestSceneGuard.RefuseIfAnyOpenSceneIsDirty(
                    RunnerLabel,
                    "이 실행은 " + WatchedAsset + "에 FBX를 복사하고 Play에 들어가기 때문에 편집 내용이 사라집니다.",
                    SkipPath,
                    null) != null)
            {
                return;
            }

            ZepetoSelfTestSceneGuard.ClearSkipRecord(SkipPath);

            File.WriteAllText(ResultPath, "ZEPETO live reload round trip" + Environment.NewLine);

            // Play 진입 직전이 아니라 무엇이든 샘플되기 전에. 같은 에디터 세션의 앞선 실행이 남긴
            // phase x bone x axis 극값이 하나라도 남아 있으면 이 실행이 그 숫자를 자기 것으로 보고한다.
            ResetPerRunKeys();

            if (!File.Exists(FixtureA) || !File.Exists(FixtureB))
            {
                AppendFixtureSpecification();
                Debug.LogError("ZEPETO live reload run: 측정용 FBX 두 개가 없어서 실행하지 못했습니다. "
                    + "만드는 방법을 " + ResultPath + " 에 적어두었습니다.");
                return;
            }

            // 헬퍼의 키 상수를 먼저 확인한다. 여기서 막으면 25초를 기다렸다가 나오는 거짓 FAIL 대신
            // 이유가 적힌 즉시 중단이 된다.
            if (string.IsNullOrEmpty(ResolveHelperSessionKey(ref liveReloadCountKey, "LiveReloadCountSessionKey"))
                || string.IsNullOrEmpty(ResolveHelperSessionKey(ref liveArmedKey, "LiveArmedSessionKey"))
                || string.IsNullOrEmpty(ResolveHelperSessionKey(ref liveMessageKey, "LiveMessageSessionKey")))
            {
                Append("FATAL: the helper's live SessionState key constants were not found "
                    + "(LiveReloadCountSessionKey / LiveArmedSessionKey / LiveMessageSessionKey). "
                    + "Nothing was measured - a renamed constant would otherwise read as 'the watcher never fired'.");
                Debug.LogError(RunnerLabel + ": 헬퍼의 라이브 SessionState 키 상수를 찾지 못해 실행하지 않았습니다.");
                return;
            }

            Type helperType = typeof(ZepetoStudioHelperWindow);

            // 사용자의 작업 단계 진행 상황은 이 실행의 것이 아니다. 헬퍼가 클립을 다시 연결하면서
            // 완료 플래그를 내리므로, 실행이 끝나는 Finish()에서 되돌려 준다.
            Append("stage flags: " + ZepetoSelfTestSceneGuard.CaptureWorkflowStageFlags());

            // 폴더 이름을 다시 적지 않고 WatchedAsset에서 뽑는다. 이 경로가 헬퍼의 LiveWatchRoot와
            // 같아야 감시자가 이 복사본에 반응한다.
            Directory.CreateDirectory(Path.GetDirectoryName(WatchedAsset));
            File.Copy(FixtureA, WatchedAsset, true);
            SessionState.SetBool(PlacedKey, true);
            AssetDatabase.ImportAsset(WatchedAsset, ImportAssetOptions.ForceUpdate);
            Append("placed fixture A at " + WatchedAsset);

            // 진짜로 도킹된 창이어야 한다. CreateInstance/DestroyImmediate가 아니다. PumpLiveReload는
            // OnEnable에서 구독하고 OnDisable에서 해제하므로, 일회용 인스턴스는 감시자를 즉시 걷어치우고
            // 아무 일도 일어나지 않는다. 헬퍼를 열어 둔 실제 사용 상황과도 이쪽이 같다.
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
                // Play에 들어가지 못했으므로 Finish()는 영영 불리지 않는다. 여기서 치우지 않으면 픽스처
                // 복사본과 단계 플래그가 그대로 남는다.
                CleanUpAfterRun();
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
            PropertyInfo p = typeof(ZepetoStudioHelperWindow).GetProperty("LiveClipAssetPath", Stat);
            string path = p == null ? null : p.GetValue(null, null) as string;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static int ReloadCount()
        {
            string key = ResolveHelperSessionKey(ref liveReloadCountKey, "LiveReloadCountSessionKey");
            return string.IsNullOrEmpty(key) ? -1 : SessionState.GetInt(key, -1);
        }

        private static bool LiveArmed()
        {
            string key = ResolveHelperSessionKey(ref liveArmedKey, "LiveArmedSessionKey");
            return !string.IsNullOrEmpty(key) && SessionState.GetBool(key, false);
        }

        private static string LiveMessage()
        {
            string key = ResolveHelperSessionKey(ref liveMessageKey, "LiveMessageSessionKey");
            return string.IsNullOrEmpty(key) ? "(none)" : SessionState.GetString(key, "(none)");
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
                Append("armed = " + LiveArmed());
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

                // 정착 구간의 뒷부분만 샘플링한다. 아바타 다운로드가 끝난 뒤여야 페이즈 A가 "픽스처 A가
                // 재생되고 있다"는 진짜 기준값을 갖는다.
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

                // 실제 사용에서 Blender의 내보내기가 일어나는 순간이 여기다.
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
                    Check("live-run:reload-fired", true, string.Empty);
                    Append("reload fired after " + Elapsed().ToString("0.0") + "s, count = " + ReloadCount());
                    Append("after swap: live clip length = "
                        + (after == null ? -1f : after.length).ToString("0.00") + "s");

                    SessionState.SetString(PhaseKey, "observing");
                    ResetClock();
                    return;
                }

                if (Elapsed() > 25f)
                {
                    Check("live-run:reload-fired", false,
                        "the watcher never fired within 25s :: message = " + LiveMessage());
                    WriteResults();
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
        /// 픽스처 A는 오른팔을 흔들고 다리는 건드리지 않는다. 픽스처 B는 왼다리를 흔들고 팔은 건드리지
        /// 않는다. 두 페이즈 모두에서 두 뼈를 다 재는 것이 결과를 모호하지 않게 만든다:
        ///   phase A: 팔이 움직이고 다리는 정지   -> 아바타가 재생되기는 한다
        ///   phase B: 다리가 움직이고 팔은 정지   -> 교체가 정말로 돌고 있는 Animator까지 닿았다
        /// 뼈 하나짜리 프로브로는 "핫 리로드 실패"와 "아무것도 재생 안 됨"을 구분할 수 없다.
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

            // 축마다 따로 추적하고 가장 넓은 것을 보고한다. 축 하나만 재던 첫 버전은 틀렸다. 픽스처 B는
            // 허벅지를 Z축으로 돌려 무릎을 옆으로 흔드는데, 앞뒤 방향만 보는 프로브는 실제로 흔들리고
            // 있는 다리를 "움직임 없음"으로 보고했다.
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

        /// <summary>
        /// 축 하나가 훑은 가장 넓은 범위, 단위는 미터.
        ///
        /// 이동 거리는 저장하지 않고 언제나 min/max 쌍에서 계산한다. 그래서 이 값을 담는 SessionState
        /// 키는 없고, ResetPerRunKeys가 지워야 할 것도 min/max 표뿐이다.
        /// </summary>
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
            Check("live-run:clip-swapped", Mathf.Abs(lenAfter - lenBefore) > 0.5f,
                "the live clip did not change length (" + lenBefore.ToString("0.00") + "s -> "
                + lenAfter.ToString("0.00") + "s), so it never became the new motion");

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
                // 페이즈 A에서 이미 아무것도 움직이지 않았다면 교체에 대해서는 할 말이 없다. 이건 핫
                // 리로드 문제가 아니라 재생 자체가 죽은 것이다.
                Check("live-run:avatar-animating", false,
                    "the avatar never moved at all, even before the swap. This is not a hot-reload problem - "
                    + "playback itself is broken.");
                WriteResults();
                return;
            }

            Check("live-run:avatar-animating", legB > Moved,
                "the leg never moved after the swap (" + legB.ToString("0.000") + "m), so the Animator is still "
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

            WriteResults();
        }

        /// <summary>
        /// 형제 러너들과 같은 모양의 집계를 결과 파일에 덧붙인다. 예전에는 PASS/FAIL이 자유 문장으로만
        /// 있어서 이 실행 결과는 grep으로도, 게이트로도 걸 수 없었다.
        /// 반드시 Play 안에서 불러야 한다. Play를 나오는 것이 도메인 리로드라 그 뒤에는 아래 목록이
        /// 비어 있다.
        /// </summary>
        private static void WriteResults()
        {
            Append("---- results ----");
            Append("pass=" + passCount + " fail=" + failCount);
            for (int i = 0; i < results.Count; i++)
            {
                Append(results[i]);
            }
        }

        private static void Check(string name, bool condition, string failDetail)
        {
            if (condition)
            {
                passCount++;
                results.Add("PASS " + name);
            }
            else
            {
                failCount++;
                results.Add("FAIL " + name + " :: " + failDetail);
            }
        }

        private static void Finish(string reason)
        {
            Append("--- finished: " + reason + " ---");
            SessionState.SetBool(RunningKey, false);
            CleanUpAfterRun();

            EditorApplication.update -= Tick;
            hooked = false;
        }

        /// <summary>
        /// 이 실행이 프로젝트에 남긴 것을 치운다. Finish에서 불릴 때는 Play를 나온 뒤라 에디터가 편집
        /// 모드이고, 시작 중에 중단된 경로에서는 애초에 Play에 들어가지도 않았다.
        /// </summary>
        private static void CleanUpAfterRun()
        {
            // 파일 하나가 1.66MB이고 .meta까지 따라오므로 그대로 두면 다음 커밋에 그대로 실려 간다.
            // 이 실행이 놓은 복사본만 지운다 - PlacedKey가 그 조건이다. AssetDatabase.DeleteAsset이
            // .meta도 함께 치운다.
            if (SessionState.GetBool(PlacedKey, false))
            {
                SessionState.SetBool(PlacedKey, false);
                if (AssetDatabase.DeleteAsset(WatchedAsset))
                {
                    Append("removed this run's fixture copy at " + WatchedAsset);
                }
                else
                {
                    Append("NOTE: could not remove " + WatchedAsset + " - delete it by hand, it is a 1.66MB "
                        + "fixture copy this run placed and nothing else uses it.");
                }
            }

            // 되돌리지 않는 부작용이 하나 남는다. RequestLivePreviewPlay -> PrepareLivePreview ->
            // ConfigureMotionFolderForLivePreview가 감시 폴더 안의 "모든" fbx에 TryConfigureMotionFbx를
            // 돌린다. 즉 그 폴더 다른 fbx들의 .meta(Animation Type, Avatar 설정)도 다시 쓰인다. 그것이
            // 라이브 프리뷰가 실제로 하는 일이라 되돌리면 오히려 실제 동작에서 멀어지므로, 되돌리지 않고
            // 여기 적어만 둔다.
            Append("side effect not undone: every fbx under "
                + Path.GetDirectoryName(WatchedAsset).Replace('\\', '/')
                + " had its importer settings rewritten by the live preview preparation, exactly as a real "
                + "live-preview run does.");

            ReportStageFlagRestore();
        }
    }
}
