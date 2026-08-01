using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 넘겨주는 지점: 흐름 중에서 Unity 밖에서 일어나는 부분.
    ///
    /// 이 창의 다른 상자들은 전부 여기서 눌러 끝나는 일이다. 이 상자만 반대다 - "여기서 멈추세요, 다음 단계는
    /// 다른 프로그램입니다"라고 말하고 그 프로그램을 대신 열어주기 위해 존재한다. 이게 없으면 Blender로 우회하는
    /// 구간이 화면에서 아예 보이지 않는다. 주변 상자들은 Unity 쪽 버튼만 보여주므로, 어디서 자리를 뜨는지
    /// 표시해 주는 것이 화면에 하나도 없기 때문이다.
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
        /// 예외를 던지는 경우까지 접어 넣은 File.Exists. 다른 컴퓨터에서 기억된 경로에는 이 컴퓨터가 거부하는
        /// 문자나 루트가 들어 있을 수 있고, 그러면 Path/File은 false를 답하는 대신 ArgumentException이나
        /// NotSupportedException을 던진다.
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
        /// BlenderMotion 폴더 하나에서 쓸 .blend, 쓸 만한 것이 없으면 빈 문자열.
        /// 정해진 이름을 먼저 확인하고, 그 다음에는 가장 최근에 쓰인 .blend가 이긴다. 관계없는 두 프로젝트가
        /// 한 폴더를 공유하는 경우보다, 작업 파일의 이름을 바꿔 쓰는 경우가 훨씬 흔하기 때문이다.
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
                    // 두 곳의 fbx 열거가 ".part"로 막아야 하는 것과 같은 Windows 8.3 짧은 이름 함정이다:
                    // "*.blend" 패턴은 Blender 자신의 zepeto_motion.blend1 롤링 백업까지 돌려줄 수 있고,
                    // .blend1을 열면 그 백업 이후에 저장한 것이 전부 조용히 사라진다.
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
        /// 사용자에게 찾아달라고 하기 전에, Unity 프로젝트 옆에서 작업용 .blend를 먼저 찾아본다.
        ///
        /// 애드온은 자기 경로들을 .blend가 놓인 자리에서 유도하므로, 여기서는 고정된 파일 이름이 아니라 폴더만
        /// 찾으면 된다: 프로젝트 옆의 BlenderMotion(출하 시 배치) 또는 프로젝트 안. 두 조사 모두 폴더가 있다고
        /// 가정하지 않으며, .blend가 하나도 없는 폴더는 폴더가 없는 것과 같게 취급한다.
        /// </summary>
        private static string GuessBlendFilePath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string parent = Path.GetDirectoryName(projectRoot);
            // 예전과 같은 두 위치, 같은 순서: 프로젝트 안을 먼저 보고 그 다음 옆을 본다(출하 시 배치는 옆이다).
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
        /// 기억된 경로는 절대 그냥 믿지 않는다. EditorPrefs는 프로젝트가 아니라 이 컴퓨터의 에디터 설치에 매여
        /// 있으므로, 저장된 값이 이 프로젝트를 복사해 온 다른 컴퓨터의 파일을 가리킬 수도 있고 사용자가 그 뒤에
        /// 옮기거나 지운 파일일 수도 있다 - 그리고 죽은 경로에 대고 "Blender 열기"를 누르면 아무것도 열리지 않고
        /// 이유도 설명되지 않는다. 그래서 해석할 때마다 파일을 다시 확인하고, 안 되면 폴더 조사로 되돌아가고,
        /// 둘 다 답하지 못하면 키를 지운다. 죽은 값이 다음에 창을 열 때 되살아나지 못하게 하기 위해서다.
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

        // 이 상자는 코드베이스에서 번호 체계 세 개가 만나는 유일한 자리다. 숫자를 읽기 전에 이것부터 읽어야 한다:
        //   · Unity 카드 번호 1~7 - 이 상자 자체는 Unity 카드 4다.
        //   · Blender 애드온 패널의 1단계~5단계 - 아래 DrawMiniHelp가 나열하는 번호가 이것이다.
        //   · 수동 임포트 상자의 1~2 - 또 다른 체계이며 MotionImport.cs의 DrawManualMotionImportBody에 있다.
        // 셋은 서로 무관하다. 한쪽 숫자를 다른 쪽에 맞춰 "정리"하면 사용자가 화면과 Blender를 대조할 수 없게 된다.
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
                    // 없는 경로는 저장하지 않는다. 기억된 값은 마지막으로 확인된 것으로 남고, 열 수 없는 값으로
                    // 덮어쓰이지 않는다.
                    statusMessage = "그 위치에 파일이 없습니다: " + picked;
                }
            }

            // 아래 목록은 애드온 패널의 섹션 제목을 그대로 옮긴 것이다
            // (BlenderMotion/zepeto_motion_helper.py의 draw_zepeto_panel). 그래서 Blender 쪽 번호는 어디에도
            // 한 벌만 존재한다. 예전에 여기 있던, 어디에도 없는 1~4 순서를 지어내는 세 번째 체계가 사라진 이유다.
            // 애드온의 제목이 바뀌면 이 다섯 줄도 같이 바뀌어야 한다.
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
        /// OS가 .blend에 연결해 둔 프로그램으로 파일을 연다. Blender가 설치된 컴퓨터라면 그게 Blender다.
        /// 문서 경로에 대고 Process.Start를 쓰려면 UseShellExecute가 필요한데, .NET Core에서는 그게 기본값이 아니다.
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
                // "직접 더블클릭해 보세요"는 쓸모없는 안내였다. 압도적으로 유력한 원인이 바로 .blend에 연결된
                // 프로그램이 없다는 것이고(Blender가 설치되지 않았거나, 연결 없이 설치되었다), 더블클릭도 똑같이
                // 실패하기 때문이다. 실제 해결책을 가리키고, 어느 쪽이든 폴더는 열어준다.
                statusMessage = "Blender를 열지 못했습니다. Blender가 설치되어 있는지 먼저 확인하세요 "
                    + "(blender.org, 무료). 설치돼 있는데도 안 되면 .blend 파일을 우클릭 → '연결 프로그램' → "
                    + "Blender를 한 번 지정하면 다음부터 이 버튼이 동작합니다. 파일 위치: " + path
                    + " / 원인: " + exception.Message;
                EditorUtility.RevealInFinder(path);
            }
        }
    }
}
