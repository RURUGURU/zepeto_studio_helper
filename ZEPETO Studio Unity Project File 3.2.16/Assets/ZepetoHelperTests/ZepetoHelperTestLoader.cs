using UnityEngine;

namespace Easy.ZepetoHelper.SelfTest
{
    /// <summary>
    /// Stand-in for the ZEPETO Studio template LOADER component. It exposes exactly the three serialized field
    /// names the helper binds to, so the helper can be exercised without the account-gated template project.
    /// </summary>
    public sealed class ZepetoHelperTestLoader : MonoBehaviour
    {
        public string zepetoId;
        public AnimationClip AnimationClip;
        public Object AnimatorController;
    }

    /// <summary>
    /// Mirrors Zepeto.ZepetoCharacterCustomLoader, which is the component that really owns zepetoId.
    /// </summary>
    public sealed class ZepetoHelperTestIdOwner : MonoBehaviour
    {
        public string zepetoId;
    }

    /// <summary>
    /// Mirrors ZEPETO.Studio.PlaygroundController, which really owns AnimationClip and AnimatorController.
    /// In the SDK these live on a different component than zepetoId, and possibly a different GameObject.
    /// </summary>
    public sealed class ZepetoHelperTestClipOwner : MonoBehaviour
    {
        public AnimationClip AnimationClip;
        public Object AnimatorController;
    }
}
