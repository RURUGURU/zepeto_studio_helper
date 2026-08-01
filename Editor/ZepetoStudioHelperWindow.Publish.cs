using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 만든 모션이 실제로 갈 수 있는 곳.
    ///
    /// 이 패널이 있는 이유는, 이 도구의 나머지 전부가 Studio ITEM Playground에서 끝나는데 그곳은 아무것도
    /// 내보내지 않기 때문이다. 아이템 SDK가 노출하는 애니메이션 슬롯은 "dynamic" 하나뿐이고, 거기에 넣은
    /// 내용물은 Unity 안에서의 미리보기로만 존재한다. 공식 자료로 확인한 사실은 둘이다. 첫째, ZEPETO
    /// Studio의 업로드 가능 카테고리 목록은 착용 아이템뿐이다(motion / gesture / pose / dance / emote 항목
    /// 자체가 없다). 둘째, setGesture()는 requestOfficialContentList() + downloadAnimation()을 통해 ZEPETO
    /// 서버가 호스팅하는 공식 제스처 라이브러리에 묶여 있어서, 로컬에서 만든 AnimationClip을 받지 않는다.
    /// 그래서 직접 만든 모션의 유일한 문서화된 목적지는 ZEPETO World다.
    ///
    /// 이 설명이 없으면 사용자는 파이프라인을 끝까지 완주하고 나서 결과물을 놓을 데가 없다는 것을 알게 된다.
    /// 그래서 이 패널은 장식이 아니라 이 제품이 성립하는 근거이고, "단순화"의 후보가 아니다.
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
        /// World SDK 컨트롤러는 zepeto.character.controller 패키지에 들어 있고, 이 아이템 제작 템플릿에는 그
        /// 패키지가 없다. 단정하지 않고 매번 확인하는 이유는, 사용자가 나중에 그 패키지를 추가했을 때도
        /// 패널이 사실을 말하게 하기 위해서다.
        /// </summary>
        private static bool HasWorldSdkAnimatorController()
        {
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ZepetoAnimatorControllerPath) != null;
        }

        // 이 패널은 성격이 다른 네 덩어리로 되어 있다.
        //   1. 경고 상자    - 사실만 적는다. 모션 아이템은 없고, 앱 안 제스처는 서버 라이브러리 전용이며,
        //                     목적지는 World뿐이라는 것. 넷 중 이것이 이 패널의 존재 이유다(위 class 주석).
        //   2. 4단계 레시피 - 접히는 부분. World 프로젝트에서 무엇을 하는지의 순서.
        //   3. 프로젝트 상태 - 그 컨트롤러가 지금 이 프로젝트에 있는지. 여기서는 "없음"이 정상이다.
        //   4. 문서 링크 둘  - 주장의 출처. 사용자가 직접 확인할 수 있어야 한다.
        // 2~4는 1을 읽은 사람에게 다음 걸음을 알려 줄 뿐이므로, 접히거나 비어도 사용자는 길을 잃지 않는다.
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

                // 평범한 LabelField는 이 경로를 패널 가장자리에서 잘라 버리는데, 정작 사용자가 써야 하는 것이
                // 그 경로다. 줄바꿈되고 선택 가능한 칸이면 창 너비와 상관없이 읽고 복사할 수 있다.
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
                "이 창의 1~7단계는 '내가 만든 의상이 움직일 때 어떻게 보이는지' 확인하는 미리보기 흐름입니다. "
                + "여기서 고른 동작은 .zepeto 파일에 들어가지 않습니다 — 업로드되는 것은 의상뿐입니다.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }
    }
}
