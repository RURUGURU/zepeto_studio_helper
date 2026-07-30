using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Where an authored motion can actually go.
    ///
    /// This panel exists because the rest of the tool ends in the Studio ITEM Playground, which ships nothing:
    /// the item SDK exposes a single overridable animation slot named "dynamic", and its contents are a Unity
    /// preview only. Verified against the official sources - ZEPETO Studio's publishable category list is
    /// wearables only (no motion / gesture / pose / dance / emote), and setGesture() is bound to ZEPETO's
    /// server-hosted official gesture library via requestOfficialContentList() + downloadAnimation(), so it
    /// will not take a locally authored AnimationClip. The one documented destination for a self-authored
    /// motion is a ZEPETO World.
    ///
    /// Without this text a user finishes the whole pipeline and has nowhere to put the result.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string WorldSdkCustomAnimationUrl =
            "https://docs.zepeto.me/world-sdk-guide-kr/5kAvgPrJTUnobt5ci6usT";
        private const string StudioCategoryGuidelinesUrl =
            "https://docs.zepeto.me/studio-guide/category-guidelines";
        private const string ZepetoAnimatorControllerPath =
            "Packages/zepeto.character.controller/runtime/resources/animatorcontroller/ZepetoAnimatorV2.controller";

        /// <summary>
        /// The World SDK controller ships with zepeto.character.controller, which this item-creation template
        /// does not include. Checking rather than asserting keeps the panel honest if the user later adds it.
        /// </summary>
        private static bool HasWorldSdkAnimatorController()
        {
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ZepetoAnimatorControllerPath) != null;
        }

        private void DrawPublishGuide()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("이 모션을 제페토에 넣기", EditorStyles.boldLabel);

            DrawMiniHelp(
                "제페토 스튜디오에는 '모션' 아이템이 없습니다. 업로드 가능한 항목은 전부 착용 아이템(옷·헤어·신발·가방 등)입니다.\n"
                + "앱 안의 포즈·제스처는 제페토 서버의 공식 라이브러리에서만 불러오기 때문에, "
                + "내가 만든 동작을 그 목록에 넣을 수는 없습니다.\n\n"
                + "내가 만든 모션이 갈 수 있는 유일한 공식 목적지는 ZEPETO World입니다.",
                MessageType.Warning);

            showPublishRecipe = EditorGUILayout.Foldout(showPublishRecipe, "월드에 넣는 방법 (4단계)", true);
            if (showPublishRecipe)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("1. World SDK 프로젝트에 이 FBX를 넣습니다");
                EditorGUILayout.LabelField("2. Rig > Animation Type = Humanoid");
                EditorGUILayout.LabelField("3. Animation > Motion > Root Motion Node = <Root Transform>");
                EditorGUILayout.LabelField("4. ZepetoAnimatorV2.controller를 복제해 클립을 스테이트로 추가 → animator.Play()");
                EditorGUI.indentLevel--;

                // A plain LabelField clips this path at the panel edge, and the path is the actionable part.
                // A wrapped, selectable field keeps it readable and copyable at any window width.
                EditorGUILayout.LabelField("복제할 컨트롤러 (월드 프로젝트 안에서):", EditorStyles.miniBoldLabel);
                EditorGUILayout.SelectableLabel(
                    ZepetoAnimatorControllerPath,
                    EditorStyles.textArea,
                    GUILayout.Height(34f));

                DrawStatusRow("이 프로젝트에 있음?", HasWorldSdkAnimatorController() ? "있음" : "없음 (월드 프로젝트에만 있습니다)");

                DrawMiniHelp(
                    "2번과 3번은 위의 '1. FBX를 ZEPETO용으로 설정'이 이미 해둡니다. "
                    + "즉 여기서 만든 FBX는 월드 프로젝트로 그대로 옮기면 됩니다.\n\n"
                    + "지금 이 프로젝트는 '아이템(의상) 제작' 템플릿이라 위 컨트롤러가 없습니다. "
                    + "4번은 별도의 ZEPETO World 프로젝트를 새로 만들어서 거기서 하는 작업입니다.\n\n"
                    + "주의: 월드 클립은 방문자 아바타 위에서 재생되므로, 스테이트에 Foot IK를 켜지 않으면 "
                    + "체형이 다른 사람에게서 발이 바닥을 뚫거나 뜹니다.",
                    MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("월드 커스텀 애니메이션 문서 열기", GUILayout.Height(24f)))
            {
                Application.OpenURL(WorldSdkCustomAnimationUrl);
            }

            if (DrawSecondaryButton("스튜디오 카테고리 목록 확인", GUILayout.Height(24f)))
            {
                Application.OpenURL(StudioCategoryGuidelinesUrl);
            }
            EditorGUILayout.EndHorizontal();

            DrawMiniHelp(
                "이 창의 1~4단계는 '내가 만든 의상이 움직일 때 어떻게 보이는지' 확인하는 미리보기 흐름입니다. "
                + "여기서 고른 동작은 .zepeto 파일에 들어가지 않습니다 — 업로드되는 것은 의상뿐입니다.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }
    }
}
