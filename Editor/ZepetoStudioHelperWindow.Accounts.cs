using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// ZEPETO account ids: validation rules, the saved id list and applying an id to LOADER.
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

            // The cached SerializedObject can be stale after a scene change or export, so rebind before writing.
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                lastLoaderSearchTime = -1000d;
                FindLoaderAndSerializedFields();
            }

            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                statusMessage = "LOADER의 zepetoId 필드를 찾지 못해 아이디를 적용하지 못했습니다. 작업 scene을 열었는지 확인하세요.";
                ValidateState();
                return;
            }

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
            // Switching account changes which avatar loads, so stage 1 must be re-confirmed for the new id.
            SetAvatarOutfitStageComplete(false);
            statusMessage = "아이디 적용됨: " + value;
            ValidateState();
        }

        /// <summary>
        /// Seeds the text field from whatever the open scene's LOADER already holds.
        ///
        /// Ids are no longer remembered in EditorPrefs. The scene is the single source of truth: what is in
        /// LOADER.zepetoId is what will load, so showing anything else in the field can only mislead. Any ids
        /// saved by an older helper version are cleaned out once, so they cannot resurface later.
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

            zepetoIdText = SanitizeZepetoId(GetLoaderZepetoId());
        }

        private string GetLoaderZepetoId()
        {
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                return string.Empty;
            }

            return zepetoIdProperty.stringValue;
        }

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

        // Accepts the character set ZEPETO ids actually use (letters, digits, underscore, period, dash) so normal
        // account names pass while a pasted profile URL or stray quote is rejected before it reaches the scene.
        private static string GetZepetoIdFormatError(string sanitizedId)
        {
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
