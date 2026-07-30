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
        private const string BlenderMotionFolderName = "BlenderMotion";
        private const string PreferredBlendFileName = "zepeto_motion.blend";

        private static string BlendFilePath
        {
            get { return EditorPrefs.GetString(BlendFilePrefKey, string.Empty); }
            set { EditorPrefs.SetString(BlendFilePrefKey, value ?? string.Empty); }
        }

        /// <summary>
        /// File.Exists with the throwing cases folded in. A path remembered on another machine can contain
        /// characters or a root this one rejects, and then Path/File raise ArgumentException or
        /// NotSupportedException instead of answering false.
        /// </summary>
        private static bool BlendFileExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The .blend to use out of one BlenderMotion folder, or empty when there is nothing usable in it.
        /// The preferred name is checked first; after that the most recently written .blend wins, because a
        /// renamed working file is far more likely than two unrelated projects sharing the folder.
        /// </summary>
        private static string FindBlendFileInFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    return string.Empty;
                }

                string preferred = Path.Combine(folder, PreferredBlendFileName);
                if (File.Exists(preferred))
                {
                    return preferred.Replace('\\', '/');
                }

                string[] candidates = Directory.GetFiles(folder, "*.blend", SearchOption.TopDirectoryOnly);
                string newest = string.Empty;
                DateTime newestStamp = DateTime.MinValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    // The same Windows 8.3 short-name trap the two fbx enumerations have to guard against with
                    // ".part": a "*.blend" pattern can also hand back Blender's own zepeto_motion.blend1
                    // rolling backup, and opening a .blend1 silently discards everything saved after it.
                    if (!".blend".Equals(Path.GetExtension(candidates[i]), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DateTime stamp = File.GetLastWriteTimeUtc(candidates[i]);
                    if (string.IsNullOrEmpty(newest) || stamp > newestStamp)
                    {
                        newest = candidates[i];
                        newestStamp = stamp;
                    }
                }

                return string.IsNullOrEmpty(newest) ? string.Empty : newest.Replace('\\', '/');
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Looks for the working .blend next to the Unity project before asking the user to find it.
        ///
        /// The add-on derives its own paths from wherever the .blend sits, so this only has to find the folder,
        /// not a fixed file name: BlenderMotion beside the project (the shipped layout) or inside it. Neither
        /// probe assumes the folder exists, and a folder with no .blend in it is the same as no folder.
        /// </summary>
        private static string GuessBlendFilePath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string parent = Path.GetDirectoryName(projectRoot);
            // Same two locations, same order, as before: inside the project first, then beside it (which is where
            // the shipped layout actually puts it).
            string[] folders =
            {
                Path.Combine(projectRoot, BlenderMotionFolderName),
                string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, BlenderMotionFolderName)
            };

            for (int i = 0; i < folders.Length; i++)
            {
                string found = FindBlendFileInFolder(folders[i]);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// [QC][Invariant:blend_path_revalidated]
        /// A remembered path is never trusted. EditorPrefs is scoped to the machine's editor install, not to the
        /// project, so the stored value can name a file on a machine this project was copied off, or one the user
        /// has since moved or deleted - and "Blender 열기" on a dead path opens nothing and explains nothing.
        /// Every resolve re-tests the file, falls back to the folder probe, and erases the key when neither
        /// answers, so a dead value cannot resurface on a later open.
        /// </summary>
        private static string ResolveBlendFilePath()
        {
            string stored = BlendFilePath;
            if (BlendFileExists(stored))
            {
                return stored;
            }

            string guessed = GuessBlendFilePath();
            if (!string.IsNullOrEmpty(guessed))
            {
                BlendFilePath = guessed;
                return guessed;
            }

            if (!string.IsNullOrEmpty(stored))
            {
                EditorPrefs.DeleteKey(BlendFilePrefKey);
            }

            return string.Empty;
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
                if (BlendFileExists(picked))
                {
                    BlendFilePath = picked.Replace('\\', '/');
                    statusMessage = "Blender 작업 파일: " + BlendFilePath;
                }
                else if (!string.IsNullOrEmpty(picked))
                {
                    // Nothing is stored for a path that is not there, so the remembered value stays whatever was
                    // last verified instead of being replaced by one that cannot be opened.
                    statusMessage = "그 위치에 파일이 없습니다: " + picked;
                }
            }

            // The Blender add-on numbers its own panel sections, and those numbers are NOT the Unity card numbers
            // (1~7) - this box is Unity card 4. The list below mirrors the add-on panel's section titles exactly
            // (draw_zepeto_panel in BlenderMotion/zepeto_motion_helper.py) so there is only one set of Blender
            // numbers anywhere, instead of the third, invented 1..4 sequence that used to be here.
            DrawMiniHelp(
                "Blender에서 할 일 — 오른쪽 사이드바의 'ZEPETO 모션' 패널 (안 보이면 3D 화면에서 N 키). "
                + "아래 번호는 그 패널의 단계 번호입니다:\n"
                + "  1단계 · 몸 불러오기 — 'ZEPETO 몸 불러오기' (리그를 이미 불러왔으면 이 칸은 사라집니다)\n"
                + "  2단계 · 포즈 만들기 — 뼈를 클릭 → R → Z → 마우스 이동 → 좌클릭\n"
                + "  3단계 · 이 순간 기록 — 프레임을 옮겨가며 '현재 포즈 저장'을 2번 이상\n"
                + "  4단계 · 부드럽게 반복 — '처음과 끝 맞추기'\n"
                + "  5단계 · Unity로 보내기 — 이름을 적고 'Unity로 보내기'\n\n"
                + "그 다음 이 창의 5번 카드로 돌아옵니다.",
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
