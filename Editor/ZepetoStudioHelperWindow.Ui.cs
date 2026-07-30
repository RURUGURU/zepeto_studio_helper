using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Reusable drawing primitives and step-state visuals.
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

        /// <summary>The state chip on the right of each step header. Used by BeginFlowStep.</summary>
        private static void DrawColoredBadge(string label, Color color, float width)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(label, EditorStyles.miniButton, GUILayout.Width(width));
            GUI.backgroundColor = previousBackground;
        }

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

        private static string GetStepStateLabel(StepState state)
        {
            if (state == StepState.Ready)
            {
                return "준비됨";
            }

            if (state == StepState.InProgress)
            {
                return "진행 필요";
            }

            if (state == StepState.Waiting)
            {
                return "대기중";
            }

            return state == StepState.Blocked ? "차단" : "필요";
        }

        private static MessageType GetStepStateMessageType(StepState state)
        {
            if (state == StepState.Ready)
            {
                return MessageType.Info;
            }

            if (state == StepState.InProgress)
            {
                return MessageType.Info;
            }

            if (state == StepState.Waiting)
            {
                return MessageType.None;
            }

            return state == StepState.Blocked ? MessageType.Error : MessageType.Warning;
        }

        private static Color GetStepStateColor(StepState state)
        {
            if (state == StepState.Ready)
            {
                return ReadyGreen;
            }

            if (state == StepState.InProgress)
            {
                return ActionBlue;
            }

            if (state == StepState.Blocked)
            {
                return BlockedRed;
            }

            if (state == StepState.Waiting)
            {
                return WaitingGray;
            }

            return NeededAmber;
        }

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
            // EditorGUILayout.Popup treats '/' as a submenu separator, which would hide entries.
            return string.IsNullOrEmpty(value) ? " " : value.Replace('/', '∕');
        }
    }
}
