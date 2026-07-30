using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// The hand-off point: the part of the flow that happens outside Unity.
    ///
    /// Every other box in this window is something you click here. This one is the opposite - it exists to say
    /// "stop, the next step is in another program", and to open that program for you. Without it the Blender
    /// detour is invisible: the surrounding boxes only show Unity-side buttons, so there is nothing on screen
    /// that marks where you leave.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string BlendFilePrefKey = "com.easy.zepeto-helper.blendFilePath";

        private static string BlendFilePath
        {
            get { return EditorPrefs.GetString(BlendFilePrefKey, string.Empty); }
            set { EditorPrefs.SetString(BlendFilePrefKey, value ?? string.Empty); }
        }

        /// <summary>
        /// Looks for the working .blend next to the Unity project before asking the user to find it.
        /// The add-on's own layout puts it in a BlenderMotion folder beside the project.
        /// </summary>
        private static string GuessBlendFilePath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string parent = Path.GetDirectoryName(projectRoot);
            string[] candidates =
            {
                Path.Combine(projectRoot, "BlenderMotion/zepeto_motion.blend"),
                string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, "BlenderMotion/zepeto_motion.blend")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                {
                    return candidates[i].Replace('\\', '/');
                }
            }

            return string.Empty;
        }

        private static string ResolveBlendFilePath()
        {
            string stored = BlendFilePath;
            if (!string.IsNullOrEmpty(stored) && File.Exists(stored))
            {
                return stored;
            }

            string guessed = GuessBlendFilePath();
            if (!string.IsNullOrEmpty(guessed))
            {
                BlendFilePath = guessed;
            }

            return guessed;
        }

        private void DrawGoToBlenderBody()
        {

            bool rigExported = File.Exists(ToAbsoluteProjectPath(ExportedRigPath));
            string blendPath = ResolveBlendFilePath();
            bool hasBlend = !string.IsNullOrEmpty(blendPath);

            DrawStatusRow("작업 파일", hasBlend ? blendPath : "찾지 못함");

            if (!rigExported)
            {
                DrawMiniHelp(
                    "3번을 먼저 하세요. 기본 몸 FBX가 있어야 Blender에서 ZEPETO 뼈대로 작업할 수 있습니다.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!hasBlend))
            {
                if (DrawColoredActionButton(
                        hasBlend ? "Blender 열기" : "Blender 파일을 찾지 못했습니다",
                        hasBlend,
                        ActionBlue,
                        GUILayout.Height(32f)))
                {
                    OpenBlendFile(blendPath);
                }
            }

            if (DrawSecondaryButton(hasBlend ? "다른 .blend 파일 고르기" : ".blend 파일 찾기", GUILayout.Height(22f)))
            {
                string picked = EditorUtility.OpenFilePanel("작업할 .blend 파일",
                    hasBlend ? Path.GetDirectoryName(blendPath) : Application.dataPath, "blend");
                if (!string.IsNullOrEmpty(picked))
                {
                    BlendFilePath = picked.Replace('\\', '/');
                    statusMessage = "Blender 작업 파일: " + BlendFilePath;
                }
            }

            DrawMiniHelp(
                "Blender에서 할 일 (오른쪽 사이드바 'ZEPETO 모션' 패널, 안 보이면 3D 화면에서 N 키):\n"
                + "  1. 파란 뼈를 클릭 → R → Z → 마우스 → 좌클릭\n"
                + "  2. '현재 포즈 저장' (프레임을 바꿔가며 2번 이상)\n"
                + "  3. '처음과 끝 맞추기'\n"
                + "  4. 'Unity로 보내기'\n\n"
                + "그 다음 아래 5번으로 돌아옵니다.",
                MessageType.None);

        }

        /// <summary>
        /// Opens the .blend with whatever the OS associates with it, which is Blender on a machine that has it.
        /// Process.Start on a document path needs UseShellExecute, which is not the .NET Core default.
        /// </summary>
        private void OpenBlendFile(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                statusMessage = "Blender를 열었습니다: " + Path.GetFileName(path)
                    + ". 포즈를 만든 뒤 'Unity로 보내기'를 누르고 이 창으로 돌아오세요.";
            }
            catch (Exception exception)
            {
                // "Double-click the file yourself" was useless advice: the overwhelmingly likely cause IS that
                // .blend has no association, because Blender is not installed or was installed without one.
                // Double-clicking fails the same way. Point at the actual fix, and open the folder regardless.
                statusMessage = "Blender를 열지 못했습니다. Blender가 설치되어 있는지 먼저 확인하세요 "
                    + "(blender.org, 무료). 설치돼 있는데도 안 되면 .blend 파일을 우클릭 → '연결 프로그램' → "
                    + "Blender를 한 번 지정하면 다음부터 이 버튼이 동작합니다. 파일 위치: " + path
                    + " / 원인: " + exception.Message;
                EditorUtility.RevealInFinder(path);
            }
        }
    }
}
