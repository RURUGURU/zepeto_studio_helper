using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// SDK 동작을 훑어보고, 편집할 수 있는 작업 복사본을 만드는 곳(카드 2).
    ///
    /// 원본은 Packages/ 아래에 있어 이 머신의 모든 프로젝트가 공유하므로 절대 건드리지 않는다. 여기서 하는 일은
    /// 고른 클립을 AnimationCopyRoot로 "_editable" 복사본으로 떠서 그것을 LOADER에 연결하는 것뿐이다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private void SelectPackageAnimation(int animationIndex)
        {
            if (animationIndex < 0 || animationIndex >= packageAnimations.Count)
            {
                return;
            }

            selectedAnimationIndex = animationIndex;
            copiedAnimationClip = FindCopiedAnimationForPackage(packageAnimations[animationIndex]);
            SetMotionSelectStageComplete(false);
        }

        // 이 선택에 해당하는 "_editable" 복사본을 찾는 유일한 자리다.
        //
        // 이름으로 파일 이름을 한 번 더 거르는 것이 요점이다. AssetDatabase.FindAssets는 부분 일치 검색이라
        // 찾는 이름과 상관없는 클립도 함께 돌려주고, 첫 번째 결과를 그냥 집으면 같은 선택에 대해 다른 클립이
        // 나올 수 있다.
        private AnimationClip FindCopiedAnimationForPackage(AnimationClip packageClip)
        {
            if (packageClip == null || !AssetDatabase.IsValidFolder(AnimationCopyRoot))
            {
                return null;
            }

            string expectedName = packageClip.name + EditableClipSuffix;
            string[] guids = AssetDatabase.FindAssets(expectedName + " t:AnimationClip", new[] { AnimationCopyRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(fileName)
                    && fileName.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 고른 동작을 작업 복사본으로 만들어 LOADER에 연결한다. 이미 복사본이 있으면 그것을 다시 쓴다.
        /// </summary>
        /// <param name="completeStageAfterAssign">
        /// false는 "이건 사용자의 선택이 아니라 미리보기를 위한 임시 빌림이다"라는 뜻이다.
        /// PlaySelectedMotionPreview만 false로 부르며, 그래야 잠깐 보려던 동작이 2단계 완료로 기록되지 않는다.
        /// </param>
        private bool UseSelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작을 복사하거나 연결하지 않습니다. 먼저 Stop을 누르세요.";
                return false;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "사용할 동작을 먼저 선택하세요.";
                return false;
            }

            // Humanoid가 아닌 클립이나 한 프레임짜리 포즈는 결국 가만히 서 있는 아바타로 끝난다. 여기서 막아야
            // 실패를 누르는 순간에 설명할 수 있다. 통과시키면 Play에 들어가서야 알게 되고, 그때는 무엇이
            // 잘못됐는지 알려 줄 자리가 없다.
            string blockReason = GetSelectedMotionBlockReason();
            if (!string.IsNullOrEmpty(blockReason))
            {
                statusMessage = blockReason;
                ValidateState();
                return false;
            }

            AnimationClip existingCopy = FindCopiedAnimationForPackage(selected);
            if (existingCopy != null)
            {
                copiedAnimationClip = existingCopy;
                if (AssignAnimationClip(existingCopy))
                {
                    if (completeStageAfterAssign)
                    {
                        SetMotionSelectStageComplete(true);
                    }

                    SelectAndPing(existingCopy);
                    statusMessage = "복사된 동작을 사용합니다: " + existingCopy.name;
                    return true;
                }

                return false;
            }

            // 성공 여부를 CopySelectedAnimation의 말이 아니라 LOADER의 상태에서 다시 읽어 낸다. 복사와 연결은
            // 각각 따로 실패할 수 있고(에셋 복사 실패, LOADER 미바인딩), 여기서 true를 잘못 돌려주면 호출자인
            // PlaySelectedMotionPreview가 빌리지도 못한 클립을 되돌리려 들면서 사용자의 작업 동작을 지운다.
            CopySelectedAnimation(completeStageAfterAssign);
            AnimationClip assignedClip = GetAssignedAnimationClip();
            return assignedClip != null && IsClipDerivedFromPackage(assignedClip, selected);
        }

        /// <summary>
        /// 고른 동작을 잠깐만 아바타에 올려 보는 미리보기. 사용자의 작업 동작은 빌렸다가 Stop에서 돌려준다.
        /// </summary>
        /// <remarks>
        /// 빌림 프로토콜은 셋이 한 벌이다. 여기서 빌리고(무엇을 되돌릴지 기록), Stop이
        /// RestoreTemporarySelectedMotionPreview로 돌려주고, 그 사이를 SessionState 두 값
        /// (isTemporarySelectedMotionPreview / motionPreviewRestoreClip)이 잇는다. 세 번째가 SessionState인
        /// 이유는 Play 진입이 도메인 리로드라서 평범한 필드로는 Stop까지 살아남지 못하기 때문이다
        /// (ZepetoStudioHelperWindow.cs의 [AUDIT][Scope:step2_preview] 참고).
        ///
        /// 기록을 연결보다 먼저 세우는 것이 중요하다. 연결이 실패해도 무엇을 되돌릴지는 이미 알고 있어야 한다.
        /// </remarks>
        private void PlaySelectedMotionPreview()
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "Play할 동작을 먼저 선택하세요.";
                return;
            }

            AnimationClip assignedClip = GetAssignedAnimationClip();
            if (!IsClipDerivedFromPackage(assignedClip, selected))
            {
                motionPreviewRestoreClip = assignedClip;
                isTemporarySelectedMotionPreview = true;
                if (!UseSelectedAnimation(false))
                {
                    isTemporarySelectedMotionPreview = false;
                    motionPreviewRestoreClip = null;
                    statusMessage = "선택한 동작을 LOADER에 연결하지 못했습니다. Console과 Validation을 확인하세요.";
                    ValidateState();
                    return;
                }
            }
            else
            {
                // 이미 이 선택에서 나온 클립이 붙어 있으면 빌린 것이 없다. 되돌릴 것이 없다는 사실을 분명히
                // 적어 두지 않으면, 지난 미리보기가 남긴 기록으로 Stop이 엉뚱한 클립을 복원한다.
                isTemporarySelectedMotionPreview = false;
                motionPreviewRestoreClip = null;
            }

            // 카드 번호가 아니라 내부 스테이지 번호다. 대응표는 Workflow.cs의 PreviewStage* 상수 주석에 있다.
            // 여기서만 필요한 사실은 카드 2의 미리보기와 카드 5의 라이브 확인이 같은 스테이지를 쓴다는 것이다.
            // 둘 다 "재생 슬롯을 빌려 간 Play 세션"이라 구분할 이유가 없다.
            RequestPlayMode(PreviewStageMotion);

            // Play가 실제로 시작되지 않았으면(안전 점검이 막았거나 사용자가 저장 대화상자를 취소했거나)
            // 빌림만 남고 돌려줄 Stop이 영영 오지 않는다. 그러면 사용자의 작업 동작이 미리보기 클립에 덮인 채
            // 끝난다. 그래서 여기서 즉시 되돌린다.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && isTemporarySelectedMotionPreview)
            {
                RestoreTemporarySelectedMotionPreview();
            }
        }

        private void RestoreTemporarySelectedMotionPreview()
        {
            if (!isTemporarySelectedMotionPreview)
            {
                return;
            }

            AnimationClip restoreClip = motionPreviewRestoreClip;
            isTemporarySelectedMotionPreview = false;
            motionPreviewRestoreClip = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode
                || animationClipProperty == null
                || !TryUpdateSerializedObject(animationClipObject))
            {
                return;
            }

            Undo.RecordObject(animationClipObject.targetObject, "Restore ZEPETO Preview Animation");
            animationClipProperty.objectReferenceValue = restoreClip;
            animationClipObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animationClipObject.targetObject);

            // 재생 슬롯도 복원한 클립을 따라와야 한다. 직렬화된 AnimationClip 필드만 되돌리면 아바타는 계속
            // 미리보기 동작을 춘다 - 실제로 재생을 정하는 것은 오버라이드 컨트롤러의 슬롯이기 때문이다.
            if (restoreClip != null)
            {
                string overrideMessage;
                ApplyClipToOverrideController(restoreClip, out overrideMessage);
            }

            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            statusMessage = restoreClip == null
                ? "미리보기 종료: 이전 작업 동작이 없어 연결을 비웠습니다."
                : "미리보기 종료: 이전 작업 동작으로 되돌렸습니다. (" + restoreClip.name + ")";
            ValidateState();
        }

        // "지금 LOADER에 붙은 클립이 이 선택에서 나온 작업 복사본인가"를 판정한다.
        //
        // AnimationCopyRoot만 인정하고 CustomMotionRoot는 일부러 받지 않는다. 이것은 클립 편집 자격을 보는
        // IsClipEditEligiblePath와 정반대이고, 그래서 헷갈리기 쉽다. 기준이 다른 이유는 묻는 질문이 다르기
        // 때문이다. 저쪽은 "이 클립을 손대도 되는가"를 묻고 사용자가 만든 모션도 당연히 포함한다. 이쪽은
        // "다시 복사할 필요가 있는가"를 묻는데, 2단계가 만드는 복사본은 AnimationCopyRoot에만 생긴다.
        // 여기에 CustomMotionRoot를 더하면 이름이 비슷한 사용자 모션이 복사본으로 오인되어 복사가 생략된다.
        private static bool IsClipDerivedFromPackage(AnimationClip clip, AnimationClip packageClip)
        {
            if (clip == null || packageClip == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase)
                && clip.name.StartsWith(packageClip.name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 목록에 오르는 동작 하나. 어디서 왔는지, Humanoid인지, 진짜 동작인지 한 프레임 포즈인지 -
        /// 즉 이 클립이 실제로 재생될지를 결정하는 사실들을 함께 들고 다닌다.
        /// </summary>
        private struct MotionEntry
        {
            public AnimationClip Clip;
            public bool IsCustom;

            public bool IsHumanoid
            {
                // ZEPETO는 Humanoid 아바타를 굴린다. generic/legacy 클립은 ZEPETO 리그에 존재하지 않는 뼈
                // 경로에 묶이기 때문에, 오류 하나 없이 아무 일도 일어나지 않는다.
                get { return Clip != null && Clip.isHumanMotion; }
            }

            public bool IsStaticPose
            {
                get { return Clip != null && Clip.length <= StaticPoseMaxLength; }
            }

            public string BuildLabel()
            {
                if (Clip == null)
                {
                    return " ";
                }

                string label = Clip.name + "  " + Clip.length.ToString("0.0") + "s";
                if (IsCustom)
                {
                    label += "  [내 모션]";
                }

                if (IsStaticPose)
                {
                    label += "  (포즈)";
                }
                else if (!IsHumanoid)
                {
                    label += "  (Humanoid 아님)";
                }

                return MakePopupSafeLabel(label);
            }
        }

        private void LoadPackageAnimations()
        {
            packageAnimations.Clear();
            motionEntries.Clear();

            // SDK 동작이 먼저, 사용자가 만든 것이 그다음. 둘은 같은 목록으로 들어가므로 Mixamo나 Blender에서
            // 온 클립도 내장 동작과 똑같이 쓰인다.
            //
            // 순서를 바꾸면 안 된다. 아래의 인덱스 계산과 selectedAnimationIndex가 이 순서로 채워진 배열을
            // 전제로 하고, 그 인덱스는 SDK 동작이 앞에 오는 목록을 사용자가 보고 고른 결과이기 때문이다.
            CollectMotionsFrom(PackageAnimationFolder, false);
            CollectMotionsFrom(CustomMotionRoot, true);

            packageAnimationNames = new string[motionEntries.Count];
            for (int i = 0; i < motionEntries.Count; i++)
            {
                packageAnimations.Add(motionEntries[i].Clip);
                packageAnimationNames[i] = motionEntries[i].BuildLabel();
            }

            if (selectedAnimationIndex < 0)
            {
                selectedAnimationIndex = FindPreferredDefaultMotionIndex();
            }

            if (selectedAnimationIndex >= motionEntries.Count)
            {
                selectedAnimationIndex = motionEntries.Count - 1;
            }
        }

        private void CollectMotionsFrom(string folder, bool isCustom)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folder });
            Array.Sort(guids, CompareAnimationGuidByName);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    continue;
                }

                motionEntries.Add(new MotionEntry { Clip = clip, IsCustom = isCustom });
            }
        }

        /// <summary>
        /// 실제로 움직이는 클립에 기본값을 맞춘다. 알파벳 순으로 가장 앞에 오는 SDK 클립은 A_pose, 즉 한
        /// 프레임짜리 포즈이고, 그것이 기본값이 된 상태가 바로 "헬퍼가 고장 났다"로 보이는 화면이다.
        /// </summary>
        private int FindPreferredDefaultMotionIndex()
        {
            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (motionEntries[i].Clip != null
                    && motionEntries[i].Clip.name.Equals(PreferredDefaultAnimationName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (!motionEntries[i].IsStaticPose && motionEntries[i].IsHumanoid)
                {
                    return i;
                }
            }

            return motionEntries.Count > 0 ? 0 : -1;
        }

        private bool TryGetSelectedMotionEntry(out MotionEntry entry)
        {
            if (selectedAnimationIndex >= 0 && selectedAnimationIndex < motionEntries.Count)
            {
                entry = motionEntries[selectedAnimationIndex];
                return true;
            }

            entry = default(MotionEntry);
            return false;
        }

        /// <summary>
        /// 고른 클립을 작업 동작으로 쓸 수 없는 이유. 문제가 없으면 빈 문자열.
        /// 비활성 컨트롤에는 반드시 비어 있지 않은 이유가 따라붙어야 하므로, 여기 문구는 그대로 화면에 나간다.
        /// </summary>
        private string GetSelectedMotionBlockReason()
        {
            MotionEntry entry;
            if (!TryGetSelectedMotionEntry(out entry) || entry.Clip == null)
            {
                return "사용할 동작을 먼저 선택하세요.";
            }

            if (!entry.IsHumanoid)
            {
                return "이 클립은 Humanoid가 아닙니다: " + entry.Clip.name
                    + ". FBX를 고른 뒤 Inspector에서 Rig > Animation Type을 Humanoid로 바꾸고 Apply 하세요. "
                    + "아래 'FBX를 ZEPETO용으로 설정' 버튼으로도 처리할 수 있습니다.";
            }

            if (entry.IsStaticPose)
            {
                return "이 클립은 " + entry.Clip.length.ToString("0.00")
                    + "초짜리 정지 포즈입니다. 그대로 쓰면 아바타가 움직이지 않습니다.";
            }

            return string.Empty;
        }

        private static int CompareAnimationGuidByName(string leftGuid, string rightGuid)
        {
            string left = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(leftGuid));
            string right = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(rightGuid));
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        // RefreshAll이 부르는 재조회. 찾는 규칙은 FindCopiedAnimationForPackage 한 곳에만 둔다.
        //
        // 예전에는 여기서 같은 일을 따로 구현하면서 FindAssets 결과의 guids[0]을 그대로 집었다. 그쪽은 파일
        // 이름이 기대한 접두어로 시작하는지까지 확인하는데 여기는 하지 않았으므로, 같은 선택에 대해 두 함수가
        // 서로 다른 클립을 답할 수 있었다. 답이 두 개면 언젠가는 갈라진다.
        private void FindExistingCopiedAnimation()
        {
            copiedAnimationClip = FindCopiedAnimationForPackage(GetSelectedPackageAnimation());
        }

        private AnimationClip GetSelectedPackageAnimation()
        {
            if (selectedAnimationIndex < 0 || selectedAnimationIndex >= packageAnimations.Count)
            {
                return null;
            }

            return packageAnimations[selectedAnimationIndex];
        }

        private void CopySelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작 파일을 복사하거나 LOADER에 연결하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not resolve selected animation path.");
                return;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Animations");

            // GenerateUniqueAssetPath라서 같은 이름이 이미 있으면 뒤에 번호가 붙는다. 그래서 되찾을 때는
            // 이름이 정확히 같은지가 아니라 접두어로 시작하는지를 본다(FindCopiedAnimationForPackage).
            string destinationPath = AssetDatabase.GenerateUniqueAssetPath(
                AnimationCopyRoot + "/" + selected.name + EditableClipSuffix + ".anim");
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not copy animation from " + sourcePath + " to " + destinationPath);
                return;
            }

            AssetDatabase.ImportAsset(destinationPath);
            copiedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
            bool didAssign = AssignAnimationClip(copiedAnimationClip);
            if (didAssign)
            {
                if (completeStageAfterAssign)
                {
                    SetMotionSelectStageComplete(true);
                }
            }
            SelectAndPing(copiedAnimationClip);
            statusMessage = didAssign
                ? "동작을 복사하고 LOADER에 연결했습니다: " + destinationPath
                : "동작 복사본은 만들었지만 LOADER에 연결하지 못했습니다: " + destinationPath;
            ValidateState();
        }
    }
}
