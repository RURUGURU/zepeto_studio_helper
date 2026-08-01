using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 창 전체가 돌려 쓰는 그리기 도구들. 이 파일은 상태를 갖지 않고, 카드의 겉모습은 전부 여기를 지나간다.
    ///
    /// 도구들이 하나같이 "그릴지 말지"가 아니라 "어떻게 그릴지"만 인자로 받는 데는 이유가 있다. 한 번의 OnGUI
    /// 안에서 컨트롤의 개수와 순서는 Layout 패스와 Repaint 패스가 같아야 하고, 프레임마다 달라져도 되는 것은
    /// enabled 여부, 라벨과 문구, MessageType 뿐이다. 그래서 버튼 도구는 모두 enabled를 받되, 어느 것도
    /// "꺼져 있으면 아무것도 그리지 않는" 길을 두지 않는다. 규칙의 전문과 그 대가를 치른 사연은
    /// ZepetoStudioHelperWindow.cs의 OnGUI 주석에 있다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Easy ZEPETO Studio Helper", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }
        }

        private static string FormatClipLength(AnimationClip clip)
        {
            return clip == null ? "0.00s" : clip.length.ToString("0.00") + "s";
        }

        /// <summary>각 step 머리 오른쪽에 붙는 상태 배지. BeginFlowStep이 쓴다.</summary>
        private static void DrawColoredBadge(string label, Color color, float width)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(label, EditorStyles.miniButton, GUILayout.Width(width));
            GUI.backgroundColor = previousBackground;
        }

        // 버튼 도구가 넷인 것은 색 자체가 뜻을 나르기 때문이다. 어느 것을 쓸지는 동작의 무게로 정한다.
        //   DrawPrimaryButton       - 그 카드의 유일한 주 동작. 파랑에 높이 30 고정이라 카드마다 하나만 둔다.
        //   DrawBlueActionButton    - 한 단계 안에서의 "적용". 크기는 호출부가 정한다.
        //   DrawSecondaryButton     - 되돌릴 수 있는 부수 동작(문서 열기 등). 색을 안 입히는 것이 요점이다.
        //   DrawColoredActionButton - 색이 곧 뜻인 경우. Play는 PlayGreen, Stop은 StopRed.
        private static bool DrawPrimaryButton(string label, bool enabled)
        {
            Color previousBackground = GUI.backgroundColor;
            if (enabled)
            {
                GUI.backgroundColor = ActionBlue;
            }

            bool clicked;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                clicked = GUILayout.Button(label, GUILayout.Height(30f));
            }

            GUI.backgroundColor = previousBackground;
            return clicked;
        }

        private static bool DrawSecondaryButton(string label, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, options);
        }

        private static bool DrawBlueActionButton(string label, bool enabled, params GUILayoutOption[] options)
        {
            return DrawColoredActionButton(label, enabled, ActionBlue, options);
        }

        // GUI.backgroundColor는 전역이다. 칠하기 전에 원래 값을 들고 있다가 반드시 되돌려야 하고, 그러지 않으면
        // 이 프레임의 남은 컨트롤 전부가 같은 색으로 물든다.
        //
        // 색을 enabled일 때만 입히는 것이 핵심이다. Steps.cs의 "동작하는 Stop은 언제나 빨갛다" 규칙이 기대는
        // 장치가 바로 이것이라, 여기서 조건을 손대면 그쪽이 조용히 깨진다. 살아 있는 버튼은 색으로 살아 있다고
        // 말하고, 회색은 오직 "지금은 눌리지 않는다"는 뜻으로만 쓴다. Play에서 빠져나오는 유일한 버튼이
        // 회색으로 보이면 사용자는 눌러 볼 생각조차 하지 않는다 - 실제로 그렇게 신고가 들어왔다
        // (Steps.cs의 Stop 버튼 주석).
        private static bool DrawColoredActionButton(string label, bool enabled, Color activeColor, params GUILayoutOption[] options)
        {
            Color previousBackground = GUI.backgroundColor;
            if (enabled)
            {
                GUI.backgroundColor = activeColor;
            }

            bool clicked;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                clicked = GUILayout.Button(label, options);
            }

            GUI.backgroundColor = previousBackground;
            return clicked;
        }

        private static bool DrawAdvancedFoldout(bool value)
        {
            return EditorGUILayout.Foldout(value, "고급 / Advanced", true);
        }

        private static void DrawMiniHelp(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }

        private static void DrawStatusRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150f));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        // 여기에는 StepState를 라벨 / MessageType / 색으로 옮기는 표현 3인방이 있었다(GetStepStateLabel,
        // GetStepStateMessageType, GetStepStateColor). 실제로 출시되는 step 머리 배지는 Flow.cs의
        // FlowStateLabel + FlowStateColor가 DrawColoredBadge로 넘기는 것 하나뿐이라, 이 셋은 호출자가 없는 데다
        // "지금 이 step은 어떻게 보여야 하는가"에 대한 두 번째 답이었고 조용히 서로 갈라지고 있었다.
        //
        // 그 뒤 순차 잠금 자체가 규칙 2(Flow.cs)로 폐기되면서 StepState를 소비하던 상태 기계도 호출자를 잃었고,
        // enum StepState와 GetSequentialStageState도 함께 사라졌다. 그 기록은 Flow.cs의
        // [QC][Invariant:sequential_unlock] 주석에 있다. GetStepStateColor를 지우면서 그 색(BlockedRed)도 같이
        // 없어졌으므로, 지금 이 이름들을 프로젝트에서 찾아도 나오지 않는 것이 정상이다.
        //
        // 이 비석을 남기는 이유는 셋이 다시 들어오는 것을 막기 위해서다. step의 겉모습은 Flow.cs만 정한다.

        // 1024 사다리를 GB에서 멈춘다. 이 함수가 재는 것은 Unity 에디터 로그 파일과 그 증가분이라
        // 그 위 단위는 나올 일이 없다. B 단위에서만 소수점을 버리는 이유는 "512.0 B"가 정밀해 보이지만
        // 바이트에는 뜻이 없는 자리이기 때문이다.
        private static string FormatBytes(long value)
        {
            if (value <= 0L)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = value;
            int unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return size.ToString(unitIndex == 0 ? "0" : "0.0") + " " + units[unitIndex];
        }

        private static string MakePopupSafeLabel(string value)
        {
            // EditorGUILayout.Popup은 '/'를 하위 메뉴 구분자로 해석해서 그 항목을 목록에서 숨겨 버린다.
            // 그래서 생김새가 거의 같은 U+2215 DIVISION SLASH로 바꾼다 - 라벨의 빗금이 이상해 보일 때
            // 찾아야 할 문자가 이것이다. 빈 문자열은 항목 자체가 사라지므로 공백 한 칸으로 바꾼다.
            return string.IsNullOrEmpty(value) ? " " : value.Replace('/', '∕');
        }
    }
}
