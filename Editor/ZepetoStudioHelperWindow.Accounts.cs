using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// ZEPETO 계정 아이디: 검사 규칙과 LOADER에 적용하기.
    ///
    /// 저장된 아이디 목록은 더 이상 없다. 0.7.0에서 그 목록을 드롭다운과 추가/삭제 버튼까지 통째로 걷어냈다
    /// (DrawZepetoIdRow, Steps.cs). 짧고, 거의 바뀌지 않고, 이미 화면에 떠 있는 값 하나를 지키자고 컨트롤을
    /// 셋이나 더 두고 있었기 때문이다. 이제 아이디가 사는 곳은 열려 있는 scene의 LOADER.zepetoId 하나뿐이고,
    /// 옛 목록이 남긴 EditorPrefs 키는 LoadZepetoIdSettings가 지워서 되살아나지 못하게 한다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void ApplyZepetoId(string value)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 아이디를 저장하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return;
            }

            value = SanitizeZepetoId(value);
            if (string.IsNullOrEmpty(value))
            {
                statusMessage = "아이디가 비어 있습니다. 적용할 ID를 입력하세요.";
                ValidateState();
                return;
            }

            string formatError = GetZepetoIdFormatError(value);
            if (!string.IsNullOrEmpty(formatError))
            {
                statusMessage = formatError;
                ValidateState();
                return;
            }

            // 한 번 시도하고, 실패하면 다시 묶고, 다시 시도하고, 그래도 안 되면 설명하고 포기한다.
            // 캐시된 SerializedObject는 scene을 바꾸거나 내보내기를 한 뒤에 낡을 수 있고, 그 대상은 이미 파괴된
            // 오브젝트일 수 있다. 그러니 쓰기 전에 다시 묶는다. 여기서 타이머를 -1000d로 미는 것은 1초 간격을
            // 이번 한 번만 건너뛰기 위해서다. 사용자가 방금 "적용"을 눌렀다면 지금이 다시 찾을 순간이다.
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                lastLoaderSearchTime = -1000d;
                FindLoaderAndSerializedFields();
            }

            // 두 번째도 실패하면 조용히 넘어가지 않는다. 아이디는 적용됐다고 믿는데 scene에는 반영되지 않은
            // 상태가 가장 나쁘다. 어떤 아바타가 로드될지는 이 값 하나가 정하기 때문이다.
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                statusMessage = "LOADER의 zepetoId 필드를 찾지 못해 아이디를 적용하지 못했습니다. 작업 scene을 열었는지 확인하세요.";
                ValidateState();
                return;
            }

            // 세 가지를 모두 해야 이 변경이 scene 저장까지 살아남는다. 하나라도 빠지면 창에는 새 아이디가
            // 보이는데 파일에는 옛 아이디가 남는다.
            //   ApplyModifiedProperties - SerializedObject의 수정을 실제 컴포넌트에 밀어 넣는다.
            //   SetDirty                - 그 컴포넌트가 저장 대상이라고 표시한다.
            //   MarkSceneDirty          - scene 자체를 저장 대상으로 표시한다. 이것이 없으면 Unity는 저장할
            //                             것이 없다고 보고 Ctrl+S가 아무 일도 하지 않는다.
            Undo.RecordObject(zepetoIdObject.targetObject, "Apply ZEPETO Id");
            zepetoIdProperty.stringValue = value;
            zepetoIdObject.ApplyModifiedProperties();
            zepetoIdText = value;
            EditorUtility.SetDirty(zepetoIdObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            // [QA][Acceptance:multi_account]
            // 계정이 바뀌면 로드되는 아바타가 바뀐다. 그러니 1단계는 새 아이디로 다시 확인받아야 한다.
            SetAvatarOutfitStageComplete(false);
            statusMessage = "아이디 적용됨: " + value;
            ValidateState();
        }

        /// <summary>
        /// 텍스트 칸의 초기값을 열려 있는 scene의 LOADER가 이미 들고 있는 값에서 가져온다.
        ///
        /// 아이디는 더 이상 EditorPrefs에 기억되지 않는다. scene이 유일한 진실이다. 실제로 로드되는 것은
        /// LOADER.zepetoId이므로, 칸에 그 밖의 값을 보여 주는 것은 사용자를 속이는 일밖에 되지 않는다.
        /// 예전 버전이 저장해 둔 아이디는 여기서 한 번 지워서 나중에 다시 떠오르지 못하게 한다.
        /// </summary>
        private void LoadZepetoIdSettings()
        {
            foreach (string staleKey in ObsoleteZepetoIdPrefKeys)
            {
                if (EditorPrefs.HasKey(staleKey))
                {
                    EditorPrefs.DeleteKey(staleKey);
                }
            }

            // 읽는 방법은 GetCurrentZepetoId(Loader.cs) 하나로 통일한다. 여기는 OnEnable에서 RefreshAll보다
            // 먼저 도는 자리라 LOADER가 아직 묶이기 전일 수 있는데, 그쪽은 그럴 때 한 번 다시 묶어 보고 답한다.
            zepetoIdText = SanitizeZepetoId(GetCurrentZepetoId());
        }

        // 두 단계로 나뉘어 있고 하는 일이 다르다. 여기 SanitizeZepetoId는 말없이 고친다 - 공백, 제어 문자,
        // 앞의 '@'는 사람이 복사하다 딸려 오는 것이지 사용자가 뜻한 입력이 아니므로 따질 일이 아니다.
        // 반대로 GetZepetoIdFormatError는 고치지 않고 큰 소리로 거절한다. 거기 걸리는 문자는 아이디의 일부일
        // 수도 있어서, 말없이 지우면 사용자가 입력한 적 없는 아이디가 scene에 들어가기 때문이다.
        // (둘은 자체 테스트가 각각 이름으로 확인하므로 하나로 합칠 수 없다.)
        private static string SanitizeZepetoId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim().TrimStart('@');
            System.Text.StringBuilder builder = new System.Text.StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        // ZEPETO 아이디가 실제로 쓰는 문자 집합(영문, 숫자, 밑줄, 마침표, 하이픈)만 통과시킨다. 평범한 계정
        // 이름은 그대로 지나가고, 붙여 넣은 프로필 URL이나 따라온 따옴표는 scene에 닿기 전에 걸린다.
        private static string GetZepetoIdFormatError(string sanitizedId)
        {
            // 이 가지는 죽은 코드가 아니다. 호출자는 셋이고 그중 둘이 빈 문자열을 들고 여기 들어온다.
            //   ApplyZepetoId(위)      - 빈 값을 앞에서 걸러 내므로 여기까지 오지 않는 유일한 호출자다.
            //   DrawZepetoIdRow(Steps.cs) - OnGUI마다 조건 없이 부른다. 그쪽의 IsNullOrEmpty 검사는 결과를
            //                           '표시'할지만 정하므로, 아이디 칸이 빈 scene에서는 매 리페인트마다
            //                           이 가지가 돈다.
            //   자체 테스트           - id-invalid:empty가 이 메서드를 이름으로 찾아 ""를 직접 넣는다.
            // 지우면 안 되는 이유: 빈 문자열은 아래 길이 검사(0자)와 문자 루프(반복 0회)를 그냥 통과해서
            // string.Empty, 즉 "문제 없음"이 돌아온다. 빈 아이디를 올바른 아이디라고 답하는 셈이고
            // id-invalid:empty가 곧바로 빨개진다.
            if (string.IsNullOrEmpty(sanitizedId))
            {
                return "아이디가 비어 있습니다.";
            }

            if (sanitizedId.Length > MaxZepetoIdLength)
            {
                return "아이디가 너무 깁니다. " + MaxZepetoIdLength + "자 이하로 입력하세요.";
            }

            for (int i = 0; i < sanitizedId.Length; i++)
            {
                char c = sanitizedId[i];
                bool isAllowed = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '_'
                    || c == '.'
                    || c == '-';
                if (!isAllowed)
                {
                    return "아이디에 쓸 수 없는 문자가 있습니다: '" + c + "'. 영문, 숫자, _ . - 만 사용하세요.";
                }
            }

            return string.Empty;
        }
    }
}
