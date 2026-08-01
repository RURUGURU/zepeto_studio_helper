using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 공식 .zepeto export를 실행하고 결과 파일을 보고하는 부분.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void OpenExportMenu()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 export 메뉴를 열지 않습니다. 먼저 Stop을 눌러주세요.";
                return;
            }

            if (clothingPrefab != null)
            {
                SelectAndPing(clothingPrefab);
            }

            // [AUDIT][Risk:Major][Scope:zepeto_export]
            // 공식 SDK 메뉴가 prefab 옆에 <outfit>.zepeto를 쓴다. 헬퍼는 그 로컬 출력물을 읽을 수 있는 파일
            // 이름으로 후처리하고 최종 경로를 UI에 보여줄 뿐이다.
            //
            // 두 경로는 메뉴보다 '먼저' 계산한다. friendly 이름에 들어가는 모션 이름은
            // GetExpectedZepetoPackagePath -> BuildFriendlyExportFileName -> GetAssignedAnimationClip 순으로
            // LOADER의 SerializedObject에서 읽는데, export 메뉴가 바로 그 SerializedObject를 죽이는 대표적인
            // 경우다(Loader.cs의 [QC][Guard:stale_serialized_object]). 메뉴가 죽이기 전이 그 값을 믿고 읽을 수
            // 있는 유일한 지점이다.
            // 메뉴 뒤에서 읽으면 GetAssignedAnimationClip이 EnsureLoaderBinding으로 스스로 다시 잡으려 하지만,
            // 그쪽은 LoaderSearchIntervalSeconds(1초)에 눌려 있어 직전 1초 안에 이미 한 번 잡았으면 아무것도
            // 하지 않고 돌아온다. 그 창에 걸리면 clip이 null로 읽혀 이름이 "motion"으로 떨어지고 결과물이
            // ZEPETO_<outfit>_motion.zepeto가 되는데, RecheckExportResult는 나중에 제대로 계산한 이름으로
            // 찾으므로 파일이 있는데도 "아직 .zepeto 파일이 없습니다"라고 말하게 된다.
            string officialPackagePath = GetOfficialZepetoPackagePath();
            string expectedPackagePath = GetExpectedZepetoPackagePath();

            if (!EditorApplication.ExecuteMenuItem(ExportMenuPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not execute menu item: " + ExportMenuPath);
                statusMessage = "ZEPETO Export 메뉴를 실행하지 못했습니다: " + ExportMenuPath;
                return;
            }

            string finalPackagePath = ResolveExportedPackagePath(officialPackagePath, expectedPackagePath);
            FindLoaderAndSerializedFields();

            if (!string.IsNullOrEmpty(finalPackagePath) && File.Exists(ToAbsoluteProjectPath(finalPackagePath)))
            {
                ResetHelperConsoleSummaryAfterSuccessfulExport();
                statusMessage = "ZEPETO export 파일을 만들었습니다: " + finalPackagePath;
                UnityEngine.Object packageAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPackagePath);
                if (packageAsset != null)
                {
                    SelectAndPing(packageAsset);
                }
            }
            else
            {
                statusMessage = "ZEPETO Export 메뉴를 실행했습니다. Console의 ZEPETO archive 결과를 확인하세요.";
            }

            Repaint();
        }

        private void RecheckExportResult()
        {
            // 이쪽은 살아남아야 할 메뉴 호출이 없으므로 두 경로를 여기서 그냥 계산해 넘긴다.
            string finalPackagePath = ResolveExportedPackagePath(
                GetOfficialZepetoPackagePath(),
                GetExpectedZepetoPackagePath());

            if (!string.IsNullOrEmpty(finalPackagePath) && File.Exists(ToAbsoluteProjectPath(finalPackagePath)))
            {
                statusMessage = "ZEPETO export 파일을 확인했습니다: " + finalPackagePath;
                SelectAndPing(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPackagePath));
            }
            else
            {
                statusMessage = "아직 .zepeto 파일이 없습니다. Console의 ZEPETO archive 로그를 확인하고, "
                    + "export가 끝난 뒤 다시 눌러주세요.";
            }

            Repaint();
        }

        /// <summary>
        /// 공식 출력물을 읽기 좋은 이름으로 옮기고, 그 결과 경로를 돌려준다.
        /// </summary>
        /// <remarks>
        /// export 직후(OpenExportMenu)와 '결과 다시 확인'(RecheckExportResult)은 서로 다른 버튼이지만 "지금
        /// 실제로 어떤 파일이 있는가"를 판정하는 방법은 하나여야 한다. 두 벌로 두었을 때는 한쪽만 고치면 같은
        /// 상태를 놓고 한 버튼은 성공, 다른 버튼은 아직이라고 말하게 된다.
        /// 앞뒤 Refresh 두 번은 각각 다른 일을 한다. 앞의 것은 SDK가 방금 쓴 파일을 AssetDatabase에 보이게 하고,
        /// 뒤의 것은 MoveAsset이 만든 새 경로를 보이게 한다 - 뒤의 것이 없으면 바로 이어지는 LoadAssetAtPath가
        /// 방금 옮긴 파일을 찾지 못한다.
        /// 두 경로를 여기서 계산하지 않고 인자로 받는 이유는 호출자마다 계산할 수 있는 시점이 다르기 때문이다.
        /// friendly 이름은 LOADER의 SerializedObject를 읽어야 나오는데 export 메뉴가 그것을 죽일 수 있으므로,
        /// OpenExportMenu는 메뉴를 부르기 전에 계산해서 넘긴다(그쪽 주석 참조). RecheckExportResult는 살아남을
        /// 메뉴 호출이 없어 부르는 자리에서 바로 계산한다.
        /// </remarks>
        private string ResolveExportedPackagePath(string officialPackagePath, string expectedPackagePath)
        {
            AssetDatabase.Refresh();
            string finalPackagePath = MoveOfficialExportToFriendlyName(officialPackagePath, expectedPackagePath);
            AssetDatabase.Refresh();
            return finalPackagePath;
        }

        /// <summary>
        /// 공식 출력물을 friendly 이름으로 옮기고, 호출자가 존재 여부를 물어야 할 경로를 돌려준다.
        /// </summary>
        /// <remarks>
        /// 출구가 다섯 개이고 각각 다른 경로를 돌려준다는 것이 이 함수의 전부다. 호출자는 돌려받은 경로에
        /// File.Exists를 걸어 UI에 성공을 말할지 결정하므로, 어느 출구로 나갔는지가 곧 화면에 뜨는 문장이 된다:
        /// 공식 경로를 못 구했으면 friendly(= 아직 없음), 둘이 같으면 official, 공식 파일이 실제로 없으면
        /// friendly(= 아직 없음), MoveAsset이 실패했으면 official(= 이름은 못 바꿨지만 파일은 있다),
        /// 성공했으면 friendly.
        ///
        /// [QC][Invariant:export_rename]
        /// 공식 출력물이 존재한 뒤에만 이름을 바꾸고, 지우는 것은 friendly 목적지 하나뿐이다.
        /// 이 규칙이 실패한 SDK export를 성공한 헬퍼 export로 보고하는 것을 막는다.
        /// </remarks>
        private string MoveOfficialExportToFriendlyName(string officialPackagePath, string friendlyPackagePath)
        {
            if (string.IsNullOrEmpty(officialPackagePath))
            {
                return friendlyPackagePath;
            }

            if (string.IsNullOrEmpty(friendlyPackagePath)
                || string.Equals(officialPackagePath, friendlyPackagePath, StringComparison.OrdinalIgnoreCase))
            {
                return officialPackagePath;
            }

            if (!File.Exists(ToAbsoluteProjectPath(officialPackagePath)))
            {
                return friendlyPackagePath;
            }

            if (File.Exists(ToAbsoluteProjectPath(friendlyPackagePath)))
            {
                AssetDatabase.DeleteAsset(friendlyPackagePath);
            }

            string moveError = AssetDatabase.MoveAsset(officialPackagePath, friendlyPackagePath);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not rename export file: " + moveError);
                return officialPackagePath;
            }

            return friendlyPackagePath;
        }

        /// <summary>
        /// export가 성공한 뒤 안전 상태의 기준선을 지금으로 다시 잡는다.
        /// </summary>
        /// <remarks>
        /// 성공했는데 초기화하는 것이 이상해 보이지만, SDK export는 정상 동작 중에도 경고를 여러 개 뱉는다.
        /// 그건 사용자가 고칠 수 있는 문제가 아닌데도 그대로 두면 Safe Status 패널에 남아, 다음에 Play를 누를 때
        /// "위험" 표시로 사용자를 막는다. 성공한 export는 그 카운터를 물려줄 이유가 없다.
        /// 기준선을 어떻게 다시 잡는지는 Safety.cs의 ResetHelperSessionCounters 하나에만 있다(로그 기준선
        /// 재측정과 2초 캐시 무효화까지 포함해서). 여기 남는 것은 "언제 그렇게 하는가"라는 이유뿐이다 -
        /// 같은 여섯 줄을 한 벌 더 두면 한쪽만 고쳐져 export 뒤에만 기준선이 다르게 잡힌다.
        /// </remarks>
        private void ResetHelperConsoleSummaryAfterSuccessfulExport()
        {
            ResetHelperSessionCounters();
        }

        /// <summary>
        /// export가 끝나면 파일이 있어야 할 자리. GetOfficialZepetoPackagePath와 같은 폴더이고 파일 이름만 다르다 -
        /// 두 함수가 서로 다른 위치를 가리킨다고 오해하기 쉬운데, 폴더는 언제나 하나다.
        ///
        /// [QA][Acceptance:visible_output_path]
        /// 7번은 export 전후로 이 경로를 읽어, .zepeto 파일이 정확히 어디에 생기는지 사용자가 눈으로 볼 수 있게 한다.
        /// </summary>
        private string GetExpectedZepetoPackagePath()
        {
            string officialPath = GetOfficialZepetoPackagePath();
            if (string.IsNullOrEmpty(officialPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(officialPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            return folder + "/" + BuildFriendlyExportFileName();
        }

        /// <summary>
        /// SDK가 실제로 쓰는 자리: 선택한 prefab 옆의 &lt;prefab 이름&gt;.zepeto. 이 위치는 헬퍼가 정하는 것이 아니라
        /// 공식 export 메뉴의 동작이므로, 여기서는 그것을 다시 계산해 맞춰볼 뿐이다.
        /// </summary>
        private string GetOfficialZepetoPackagePath()
        {
            if (clothingPrefab == null)
            {
                return string.Empty;
            }

            string prefabPath = AssetDatabase.GetAssetPath(clothingPrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(prefabPath) + ".zepeto";
            return folder + "/" + fileName;
        }

        private string BuildFriendlyExportFileName()
        {
            string outfitName = clothingPrefab == null ? "outfit" : clothingPrefab.name;
            AnimationClip clip = GetAssignedAnimationClip();
            string motionName = clip == null ? "motion" : GetReadableMotionName(clip.name);
            // [QC][Invariant:filename]
            // 의상 이름과 모션 이름을 모두 넣어, Unity의 Project 창 밖에서도 어떤 파일인지 알아볼 수 있게 한다.
            return MakeExportSafeFileName("ZEPETO_" + outfitName + "_" + motionName) + ".zepeto";
        }

        /// <summary>
        /// 파이프라인이 붙인 접미사를 떼어 사람이 읽는 모션 이름으로 되돌린다.
        /// "_clipedit"은 ClipEdit.cs의 CreateNextEditPath가, "_editable"은 Motion.cs의 복사 단계가 붙인 것이라
        /// 여기서 떼는 두 문자열은 그 두 생산자와 정확히 짝이 맞아야 한다. 한쪽만 바꾸면 export 파일 이름에
        /// 내부 접미사가 그대로 새어 나온다.
        /// </summary>
        private static string GetReadableMotionName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "motion";
            }

            string name = rawName;
            int clipEditIndex = name.IndexOf("_clipedit", StringComparison.OrdinalIgnoreCase);
            if (clipEditIndex > 0)
            {
                name = name.Substring(0, clipEditIndex);
            }

            const string editableSuffix = "_editable";
            if (name.EndsWith(editableSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - editableSuffix.Length);
            }

            return string.IsNullOrEmpty(name) ? "motion" : name;
        }

        /// <summary>
        /// 파일 이름으로 쓸 수 있는 문자열로 만든다. 이 패키지가 파일 이름을 만드는 유일한 자리이며,
        /// ClipEditUtility.CreateNextEditPath도 fallback만 바꿔서 이 함수를 쓴다 - 두 벌로 두면 한쪽에서만
        /// 걸러지는 문자가 생긴다.
        /// 공백과 연속 밑줄까지 접는 것은 미관이 아니라, 명령줄이나 웹 업로드에서 따옴표 없이 다룰 수 있게
        /// 하기 위해서다.
        /// </summary>
        private static string MakeExportSafeFileName(string value, string fallback = "ZEPETO_export")
        {
            string safeName = string.IsNullOrEmpty(value) ? fallback : value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            safeName = safeName.Replace(' ', '_');
            while (safeName.IndexOf("__", StringComparison.Ordinal) >= 0)
            {
                safeName = safeName.Replace("__", "_");
            }

            return string.IsNullOrEmpty(safeName) ? fallback : safeName;
        }

        private static string GetExportPackageStatusText(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return "의상 선택 필요";
            }

            string absolutePath = ToAbsoluteProjectPath(projectRelativePath);
            if (!File.Exists(absolutePath))
            {
                return projectRelativePath + " (아직 생성 전)";
            }

            FileInfo fileInfo = new FileInfo(absolutePath);
            return projectRelativePath
                + " (저장됨, "
                + FormatBytes(fileInfo.Length)
                + ", "
                + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                + ")";
        }

        private static bool ExportPackageExists(string projectRelativePath)
        {
            return !string.IsNullOrEmpty(projectRelativePath)
                && File.Exists(ToAbsoluteProjectPath(projectRelativePath));
        }

        /// <summary>
        /// "Assets/..." 형태의 프로젝트 상대 경로를 절대 경로로 바꾼다. Application.dataPath가 Assets 폴더
        /// 자체이므로 ".."가 프로젝트 루트다. AssetDatabase가 모르는 파일(아직 임포트되지 않았거나, .meta처럼
        /// 애초에 asset이 아닌 것)에 File.Exists를 걸어야 할 때 쓰며, 이 패키지 전체에서 그 용도로 쓰인다.
        /// </summary>
        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelativePath));
        }
    }
}
