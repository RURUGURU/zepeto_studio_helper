using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// 눈 깜빡임이 **진짜 아바타의 눈꺼풀을 움직이는지**를 Play 중에 측정한다.
    ///
    /// 커브를 넣었다는 것과 눈이 감긴다는 것은 다른 문장이다. 바인딩 문자열이 한 글자만 달라도
    /// AnimationUtility는 아무 불평 없이 커브를 저장하고, 재생하면 그냥 아무 일도 일어나지 않는다.
    /// 에러도 경고도 없다. 그래서 이 러너는 커브의 존재가 아니라 **SkinnedMeshRenderer의 실제
    /// 블렌드셰이프 가중치**를 매 프레임 읽어서, 0에서 출발해 100 근처까지 갔다 돌아오는지를 본다.
    ///
    /// 트리거: 프로젝트 루트에 zepeto-blink.trigger, 내용은 대상 .anim 경로.
    /// 결과: zepeto-blink.report.txt
    ///
    /// ## ⚠️ 이 러너는 되돌리지 않는다 (다른 넷과 다르다)
    ///
    /// ZepetoHelperSelfTest / ZepetoRigExportRun / ZepetoCustomMotionRun / ZepetoLiveReloadRun은
    /// 자기가 바꾼 것을 전부 스냅샷하고 끝에서 되돌린다. 이 러너는 그렇게 하지 않는다:
    ///
    ///   - 대상 .anim에 깜빡임 커브를 **영구히** 써 넣는다 (그게 이 기능의 목적이다)
    ///   - 그 클립을 재생 슬롯에 물린다 - 씬의 LOADER와 override controller가 바뀐 채로 남는다
    ///
    /// 그래서 아무 클립에나 겨누면 안 된다. 되돌리려면 6번 카드의 `빼기`를 누르고, 씬은
    /// 2번에서 원래 동작을 다시 적용하면 된다. 저장 안 한 씬 편집이 있는 상태에서 돌리지 마세요 -
    /// Play 진입이 그것을 묻지도 않고 버린다.
    ///
    /// [주의] Play 진입은 도메인 리로드를 일으켜 static 필드와 update 구독을 지운다.
    /// 상태는 SessionState에, 구독은 리로드마다 도는 [InitializeOnLoad]에서 다시 건다.
    /// </summary>
    [InitializeOnLoad]
    public static class ZepetoBlinkRun
    {
        private const string TriggerPath = "zepeto-blink.trigger";
        private const string ReportPath = "zepeto-blink.report.txt";
        private const string ClipKey = "ZepetoBlinkRun.Clip";
        private const string ArmedKey = "ZepetoBlinkRun.Armed";
        private const string DeadlineKey = "ZepetoBlinkRun.Deadline";
        private const string MinKey = "ZepetoBlinkRun.Min";
        private const string MaxKey = "ZepetoBlinkRun.Max";
        private const string ShapeKey = "ZepetoBlinkRun.Shape";

        private const string BlinkShapeName = "zepeto.eyeBlinkLeft";
        private const float SampleSeconds = 14f;

        static ZepetoBlinkRun()
        {
            EditorApplication.delayCall += Bootstrap;
        }

        private static void Bootstrap()
        {
            if (SessionState.GetBool(ArmedKey, false))
            {
                if (EditorApplication.isPlaying)
                {
                    if (SessionState.GetFloat(DeadlineKey, 0f) <= 0f)
                    {
                        SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + SampleSeconds);
                        SessionState.SetFloat(MinKey, float.MaxValue);
                        SessionState.SetFloat(MaxKey, float.MinValue);
                    }
                    EditorApplication.update += Sample;
                }
                return;
            }

            if (!File.Exists(TriggerPath))
            {
                return;
            }
            string clipPath = File.ReadAllText(TriggerPath).Trim();
            File.Delete(TriggerPath);

            StringBuilder head = new StringBuilder();
            head.AppendLine("ZEPETO blink run");
            head.AppendLine("clip = " + clipPath);
            head.AppendLine("----");

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                head.AppendLine("FATAL: clip을 찾지 못했습니다");
                File.WriteAllText(ReportPath, head.ToString());
                return;
            }

            // 헬퍼의 기능을 리플렉션으로 부른다. 테스트 어셈블리가 내부 API에 직접 의존하지 않게
            // 하려는 이 프로젝트의 규칙을 따른다.
            Type window = Type.GetType("Easy.ZepetoHelper.Editor.ZepetoStudioHelperWindow, Easy.ZepetoHelper.Editor");
            MethodInfo add = window == null ? null : window.GetMethod("AddBlinkToClip",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (add == null)
            {
                head.AppendLine("FATAL: AddBlinkToClip을 찾지 못했습니다");
                File.WriteAllText(ReportPath, head.ToString());
                return;
            }

            // [QC] 남의 눈 애니메이션을 지키는지 먼저 본다.
            //
            // ZEPETO 공식 클립은 blendShape.zepeto.eyeBlinkLeft를 이미 갖고 있다 - 2번 카드의 기본
            // 동작인 Videobooth_282의 사본에는 키가 95개 들어 있다. 처음 판의 헬퍼는 "커브가 있으면
            // 우리 것"으로 보고 그것을 덮어쓰거나 지웠다. Undo도 없고 UI로 되돌릴 길도 없었다.
            // 그래서 이 검사가 지켜야 하는 것은 "깜빡임이 들어간다"가 아니라 "남의 것은 안 건드린다"다.
            const string authored = "Assets/ZepetoHelper/Animations/Videobooth_282_editable.anim";
            AnimationClip sdkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(authored);
            if (sdkClip == null)
            {
                head.AppendLine("NOTE blink:authored-guard :: " + authored + " 가 없어 건너뜁니다");
            }
            else
            {
                int before = CountBlinkKeys(sdkClip);
                object[] probe = new object[] { sdkClip, null };
                int wrote = (int)add.Invoke(null, probe);
                int after = CountBlinkKeys(sdkClip);
                bool safe = wrote == 0 && after == before && before > 0;
                head.AppendLine((safe ? "PASS" : "FAIL")
                    + " blink:refuses-to-overwrite-authored :: 키 " + before + " -> " + after
                    + ", 반환 " + wrote + " : " + probe[1]);
                if (!safe)
                {
                    File.WriteAllText(ReportPath, head.ToString());
                    return;
                }
            }

            object[] args = new object[] { clip, null };
            int count = (int)add.Invoke(null, args);
            head.AppendLine("AddBlinkToClip -> " + count + " : " + args[1]);
            if (count <= 0)
            {
                head.AppendLine("FAIL blink:curves-added");
                File.WriteAllText(ReportPath, head.ToString());
                return;
            }
            head.AppendLine("PASS blink:curves-added");

            // 재생 슬롯에 이 클립을 물린다. 헬퍼 창의 AssignAnimationClip을 쓰는 이유는, 재생을
            // 정하는 것이 LOADER의 clip 필드가 아니라 AnimatorOverrideController의 슬롯이기 때문이다.
            // 러너가 슬롯을 직접 만지면 '사용자가 버튼을 눌렀을 때와 같은 경로'가 아니게 된다.
            //
            // 이 단계를 빼먹었더니 첫 실행이 정확히 그 함정에 빠졌다: 깜빡임은 BlinkTarget.anim에
            // 넣고 재생된 것은 DanceDemo10s.anim이라, 가중치가 0에서 한 번도 움직이지 않았다.
            // 검사는 FAIL을 냈지만 그것은 기능의 실패가 아니라 하네스의 실패였다.
            try
            {
                Type wt = Type.GetType("Easy.ZepetoHelper.Editor.ZepetoStudioHelperWindow, Easy.ZepetoHelper.Editor");
                UnityEngine.Object helper = EditorWindow.GetWindow(wt, false, "ZEPETO Studio Helper", true);
                MethodInfo assign = wt.GetMethod("AssignAnimationClip",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                bool ok = (bool)assign.Invoke(helper, new object[] { clip, false });
                head.AppendLine((ok ? "PASS" : "FAIL") + " blink:clip-assigned-to-playback-slot");
                if (!ok)
                {
                    File.WriteAllText(ReportPath, head.ToString());
                    return;
                }
            }
            catch (Exception exception)
            {
                head.AppendLine("FAIL blink:clip-assigned-to-playback-slot :: " + exception.Message);
                File.WriteAllText(ReportPath, head.ToString());
                return;
            }

            File.WriteAllText(ReportPath, head.ToString());
            SessionState.SetString(ClipKey, clipPath);
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetFloat(DeadlineKey, 0f);
            EditorApplication.EnterPlaymode();
        }

        private static void Sample()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            SkinnedMeshRenderer body = FindBody();
            if (body != null)
            {
                int index = body.sharedMesh.GetBlendShapeIndex(BlinkShapeName);
                SessionState.SetInt(ShapeKey, index);
                if (index >= 0)
                {
                    float w = body.GetBlendShapeWeight(index);
                    SessionState.SetFloat(MinKey, Mathf.Min(SessionState.GetFloat(MinKey, float.MaxValue), w));
                    SessionState.SetFloat(MaxKey, Mathf.Max(SessionState.GetFloat(MaxKey, float.MinValue), w));
                }
            }

            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(DeadlineKey, 0f))
            {
                return;
            }

            EditorApplication.update -= Sample;
            SessionState.SetBool(ArmedKey, false);
            Finish();
            EditorApplication.ExitPlaymode();
        }

        /// <summary>클립의 eyeBlinkLeft 커브 키 개수. 없으면 0.</summary>
        private static int CountBlinkKeys(AnimationClip clip)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                "body", typeof(SkinnedMeshRenderer), "blendShape.zepeto.eyeBlinkLeft");
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            return curve == null || curve.keys == null ? 0 : curve.keys.Length;
        }

        private static SkinnedMeshRenderer FindBody()
        {
            SkinnedMeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].sharedMesh != null && all[i].sharedMesh.blendShapeCount > 0)
                {
                    return all[i];
                }
            }
            return null;
        }

        private static void Finish()
        {
            float lo = SessionState.GetFloat(MinKey, float.MaxValue);
            float hi = SessionState.GetFloat(MaxKey, float.MinValue);
            int index = SessionState.GetInt(ShapeKey, -1);

            StringBuilder sb = new StringBuilder(File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : "");
            sb.AppendLine("blendShape index of " + BlinkShapeName + " = " + index);
            if (index < 0)
            {
                sb.AppendLine("FAIL blink:shape-present :: 아바타에 그 블렌드셰이프가 없습니다");
            }
            else
            {
                sb.AppendLine("PASS blink:shape-present");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "weight range: {0:0.0} ~ {1:0.0}", lo, hi));
                // 눈이 실제로 감기려면 가중치가 크게 올라갔다 내려와야 한다. 절반만 넘어도
                // '움직인다'는 증명은 되지만, 커브가 100을 목표로 하므로 넉넉히 60으로 잡는다.
                bool closed = hi >= 60f;
                bool opened = lo <= 5f;
                sb.AppendLine((closed ? "PASS" : "FAIL") + " blink:eye-closes :: 최대 " + hi.ToString("0.0"));
                sb.AppendLine((opened ? "PASS" : "FAIL") + " blink:eye-opens :: 최소 " + lo.ToString("0.0"));
            }
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("blink run finished");
        }
    }
}
