using UnityEngine;

namespace Easy.ZepetoHelper.SelfTest
{
    /// <summary>
    /// ZEPETO Studio 템플릿의 LOADER 컴포넌트 대역. 헬퍼가 바인딩하는 직렬화 필드 이름 세 개를 그대로
    /// 노출하므로, 계정이 있어야 받을 수 있는 템플릿 프로젝트 없이도 헬퍼를 돌려볼 수 있다.
    /// AnimationClip / AnimatorController 필드는 어떤 검사도 읽지 않지만 지우면 안 된다.
    /// field-owner:* NOTE 세 줄이 이 타입 이름을 그대로 적어 두고 있어서 기준 결과 파일이 바뀐다.
    /// </summary>
    public sealed class ZepetoHelperTestLoader : MonoBehaviour
    {
        public string zepetoId;
        public AnimationClip AnimationClip;
        public Object AnimatorController;
    }

    /// <summary>
    /// zepetoId를 실제로 소유한 컴포넌트인 Zepeto.ZepetoCharacterCustomLoader의 대역.
    /// </summary>
    public sealed class ZepetoHelperTestIdOwner : MonoBehaviour
    {
        public string zepetoId;
    }

    /// <summary>
    /// AnimationClip과 AnimatorController를 실제로 소유한 ZEPETO.Studio.PlaygroundController의 대역.
    /// SDK에서는 이 둘이 zepetoId와 다른 컴포넌트에, 경우에 따라 다른 GameObject에 붙어 있다.
    /// 이 둘과 위의 IdOwner는 SDK 배치를 흉내 낸 분리 레이아웃 픽스처이고, split-binding:* 다섯 검사가
    /// 존재하는 이유 자체다. ZepetoHelperTestLoader의 중복이 아니다.
    /// </summary>
    public sealed class ZepetoHelperTestClipOwner : MonoBehaviour
    {
        public AnimationClip AnimationClip;
        public Object AnimatorController;
    }
}
