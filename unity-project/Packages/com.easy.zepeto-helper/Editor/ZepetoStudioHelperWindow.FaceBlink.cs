using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 클립에 눈 깜빡임을 넣는다.
    ///
    /// ## 왜 여기(Unity)에서 하나
    ///
    /// 깜빡임은 뼈가 아니라 **블렌드셰이프**로 만들어진다. 미리 만들어 둔 '눈 감은 얼굴' 모양을
    /// 0~100%로 섞는 방식이다. 그래서 Blender에서는 만들 수 없다 - 3번이 내보내는
    /// ZepetoBaseModel.fbx의 몸에는 그 모양이 **하나도 없기 때문**이다
    /// (prefab의 m_Shapes가 메시 4개 전부 비어 있다). 섞을 것이 없으니 키를 찍을 수도,
    /// FBX에 실어 보낼 수도 없다.
    ///
    /// 그 모양들은 **Play 중 서버에서 아바타가 내려올 때** 붙는다. 실측하면 body 메시에
    /// 블렌드셰이프가 250개 있고 그중 깜빡임·눈썹·미소 계열이 79개다
    /// (Assets/ZepetoHelperTests/Editor/ZepetoFaceProbeRun.cs가 재는 값).
    ///
    /// ## 어떤 커브를 넣는가
    ///
    /// ZEPETO가 자기 공식 모션에서 쓰는 것과 **똑같은 바인딩**을 쓴다. zepeto.studio@3.2.16의
    /// ANI_039_v03.anim을 열어 확인한 형식이다:
    ///
    ///     path      : body
    ///     type      : SkinnedMeshRenderer   (classID 137)
    ///     attribute : blendShape.zepeto.eyeBlinkLeft / ...Right
    ///     값        : 0 = 뜬 눈, 100 = 감은 눈
    ///
    /// 이름을 지어내지 않는 것이 중요하다. 이 문자열이 한 글자라도 다르면 커브는 아무것도 하지
    /// 않고, 에러도 나지 않는다 - 그냥 안 깜빡인다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>블렌드셰이프가 붙어 있는 렌더러의 경로. 공식 클립도 이 이름을 쓴다.</summary>
        private const string BlinkRendererPath = "body";

        private const string BlinkLeftProperty = "blendShape.zepeto.eyeBlinkLeft";
        private const string BlinkRightProperty = "blendShape.zepeto.eyeBlinkRight";

        /// <summary>눈이 감겼다 뜨는 데 걸리는 시간(초). 사람의 실제 깜빡임이 0.1~0.15초다.</summary>
        private const float BlinkDurationSeconds = 0.13f;

        /// <summary>깜빡임 간격(초). 사람은 보통 3~5초에 한 번 깜빡인다.</summary>
        private const float BlinkIntervalSeconds = 3.4f;

        /// <summary>블렌드셰이프 가중치의 최대값. Unity는 0~100으로 다룬다.</summary>
        private const float BlinkClosedWeight = 100f;

        /// <summary>
        /// 클립 길이에 맞춰 깜빡임 시각을 고른다.
        ///
        /// 시작과 끝을 피하는 데는 이유가 있다. 이 클립들은 반복 재생되므로, 경계에 눈이 반쯤
        /// 감긴 상태가 있으면 한 바퀴가 끝날 때마다 눈이 튄다. 첫 깜빡임은 간격의 절반 뒤에 두고,
        /// 마지막 깜빡임이 끝난 뒤에도 깜빡임 하나 분량은 남겨 둔다.
        ///
        /// 클립이 너무 짧아 한 번도 넣을 수 없으면 빈 목록을 돌려준다. 억지로 하나 끼워 넣으면
        /// 1초짜리 클립 내내 눈을 감고 있게 된다.
        /// </summary>
        internal static List<float> BlinkTimes(float clipLength)
        {
            List<float> times = new List<float>();
            float margin = BlinkDurationSeconds;
            float usableEnd = clipLength - margin;
            for (float t = BlinkIntervalSeconds * 0.5f; t <= usableEnd; t += BlinkIntervalSeconds)
            {
                if (t - margin < 0f)
                {
                    continue;
                }
                times.Add(t);
            }
            return times;
        }

        /// <summary>
        /// 한쪽 눈의 깜빡임 커브. 0에서 시작해 0으로 끝난다.
        ///
        /// 감는 것이 뜨는 것보다 빠르다(40% / 60%). 양쪽을 똑같이 주면 기계적으로 보인다 -
        /// 실제 눈꺼풀은 빠르게 닫히고 조금 천천히 열린다.
        /// </summary>
        internal static AnimationCurve BuildBlinkCurve(float clipLength, IList<float> times)
        {
            AnimationCurve curve = new AnimationCurve();
            // 경계를 0으로 못박는다. 이 두 키가 없으면 Unity가 첫/마지막 깜빡임 값을 바깥으로
            // 연장해서, 반복할 때 눈이 감긴 채로 넘어간다.
            curve.AddKey(new Keyframe(0f, 0f));
            for (int i = 0; i < times.Count; i++)
            {
                float center = times[i];
                float close = BlinkDurationSeconds * 0.4f;
                float open = BlinkDurationSeconds * 0.6f;
                curve.AddKey(new Keyframe(center - close, 0f));
                curve.AddKey(new Keyframe(center, BlinkClosedWeight));
                curve.AddKey(new Keyframe(center + open, 0f));
            }
            curve.AddKey(new Keyframe(clipLength, 0f));
            return curve;
        }

        /// <summary>
        /// 클립에 깜빡임 커브 두 개를 써 넣는다. 넣은 깜빡임 횟수를 돌려주고, 못 하면 0에 이유를 담는다.
        ///
        /// 클립을 새로 만들지 않고 제자리에서 고치는 이유는, 이 기능이 6번 카드의 다른 편집들과
        /// 같은 대상(이미 헬퍼가 소유한 .anim)에 붙기 때문이다. 편집 결과를 또 다른 사본으로
        /// 흩뿌리면 어느 것이 재생되는지 추적할 수 없게 된다.
        /// </summary>
        internal static int AddBlinkToClip(AnimationClip clip, out string message)
        {
            message = string.Empty;

            if (clip == null)
            {
                message = "동작이 비어 있습니다. 먼저 2번에서 동작을 고르고 적용하세요.";
                return 0;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                message = "이 동작은 프로젝트 안의 .anim 파일이 아닙니다.";
                return 0;
            }

            // package 안의 원본은 절대 건드리지 않는다. 되돌릴 방법이 Package Manager 재설치뿐이다.
            if (IsPackageOrPackageCachePath(path))
            {
                message = "원본 package 동작에는 쓸 수 없습니다. 2번에서 '이 동작 쓰기'로 사본을 먼저 만드세요.";
                return 0;
            }

            // .anim이 아니면(예: fbx 안의 서브 에셋) SetEditorCurve가 조용히 아무것도 하지 않는다.
            if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            {
                message = "이 동작은 FBX 안에 들어 있어 직접 고칠 수 없습니다. "
                    + "5번의 '2. 내 모션으로 추가'로 .anim을 먼저 뽑으세요 (" + path + ").";
                return 0;
            }

            List<float> times = BlinkTimes(clip.length);
            if (times.Count == 0)
            {
                message = string.Format(
                    "클립이 {0:0.00}초로 너무 짧아 깜빡임을 넣지 않았습니다 (최소 {1:0.0}초 필요).",
                    clip.length, BlinkIntervalSeconds * 0.5f + BlinkDurationSeconds);
                return 0;
            }

            AnimationCurve curve = BuildBlinkCurve(clip.length, times);
            foreach (string property in new[] { BlinkLeftProperty, BlinkRightProperty })
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    BlinkRendererPath, typeof(SkinnedMeshRenderer), property);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            message = string.Format(
                "눈 깜빡임 {0}번을 넣었습니다 ({1}). Play에서 확인하세요 - 편집 화면의 회색 몸에는 "
                + "표정 모양이 없어서 보이지 않습니다.",
                times.Count, clip.name);
            return times.Count;
        }

        /// <summary>클립에 이미 깜빡임 커브가 들어 있는가. 버튼 문구를 정하는 데 쓴다.</summary>
        internal static bool ClipHasBlink(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].propertyName == BlinkLeftProperty)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 6번 카드 안의 깜빡임 칸.
        ///
        /// 여기 있는 이유: 이건 클립을 고치는 일이고, 6번이 클립을 고치는 자리다.
        /// 그리고 이 편집만 Blender로 되돌아갈 수 없다 - 넣은 커브는 Unity 쪽 .anim에만 있으므로,
        /// Blender에서 다시 내보내면 사라진다. 그 사실을 버튼 옆에 적어 둔다.
        /// </summary>
        private void DrawBlinkControls(AnimationClip assignedClip)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("얼굴 · 눈 깜빡임", EditorStyles.boldLabel);

            bool hasBlink = ClipHasBlink(assignedClip);
            int planned = assignedClip == null ? 0 : BlinkTimes(assignedClip.length).Count;

            DrawStatusRow("현재 상태", assignedClip == null
                ? "동작 없음"
                : (hasBlink ? "깜빡임 있음" : "없음 — 넣으면 " + planned + "번"));

            bool canEdit = assignedClip != null
                && !EditorApplication.isPlayingOrWillChangePlaymode
                && planned > 0;

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton(hasBlink ? "다시 넣기" : "눈 깜빡임 넣기", GUILayout.Height(24f)))
            {
                if (!canEdit)
                {
                    statusMessage = EditorApplication.isPlayingOrWillChangePlaymode
                        ? "Play 중에는 .anim을 고치지 않습니다. 먼저 Stop을 누르세요."
                        : "먼저 2번에서 동작을 고르고 적용하세요.";
                }
                else
                {
                    string message;
                    AddBlinkToClip(assignedClip, out message);
                    statusMessage = message;
                }
                Repaint();
            }

            using (new EditorGUI.DisabledScope(!hasBlink || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("빼기", GUILayout.Width(70f), GUILayout.Height(24f)))
                {
                    RemoveBlinkFromClip(assignedClip);
                    statusMessage = "눈 깜빡임을 뺐습니다.";
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawMiniHelp(
                "깜빡임은 뼈가 아니라 블렌드셰이프(미리 만들어 둔 표정 모양)입니다. 그 모양은 Play 중 "
                + "서버에서 내려오는 진짜 아바타에만 있어서, 편집 화면의 회색 몸으로는 확인할 수 없습니다 "
                + "— 5번 Play에서 보세요.\n\n"
                + "이 편집은 Unity 쪽 .anim에만 남습니다. Blender에서 같은 이름으로 다시 내보내면 "
                + "사라지므로, 동작을 다 다듬은 뒤 마지막에 넣으세요.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        /// <summary>깜빡임 커브를 다시 걷어낸다. 넣기와 짝을 이루지 않으면 되돌릴 방법이 없다.</summary>
        internal static void RemoveBlinkFromClip(AnimationClip clip)
        {
            if (clip == null)
            {
                return;
            }
            foreach (string property in new[] { BlinkLeftProperty, BlinkRightProperty })
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    BlinkRendererPath, typeof(SkinnedMeshRenderer), property);
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
        }
    }
}
