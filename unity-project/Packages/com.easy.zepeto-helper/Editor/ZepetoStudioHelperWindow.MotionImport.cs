using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 외부 애니메이션 FBX(Mixamo 다운로드, Blender export)를 ZEPETO 아바타가 재생할 수 있는 clip으로 바꾸는 부분.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// 공식 ZEPETO 커스텀 애니메이션 가이드가 요구하는 import 설정을 적용한다:
        /// Animation Type = Humanoid, 그리고 아바타를 화면 안에 붙잡아 두는 root transform 설정.
        /// 이걸 틀리는 것이 임포트한 모션이 아무것도 하지 않는 가장 흔한 원인이다.
        ///
        /// 다섯 단계로 흐른다:
        ///   ① Humanoid + importAnimation
        ///   ② Avatar 복사 판정, 또는 오염된 .meta 복구
        ///   ③ Root Transform 고정
        ///   ④ 결과 검증 - VerifyMotionFbxImportResult로 나가 있다
        ///   ⑤ 보고
        /// ①②③이 각각 SaveAndReimport로 끝나고 그때마다 importer를 다시 읽는 것은 아래 [AUDIT] 블록이 적어 둔
        /// 이유 때문이며, 세 번으로 나뉜 구조와 그 순서는 의도된 것이다.
        /// </summary>
        private bool TryConfigureMotionFbx(string assetPath, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(assetPath))
            {
                message = "FBX 파일을 Project 창에서 먼저 선택하세요.";
                return false;
            }

            string extension = Path.GetExtension(assetPath);
            if (!".fbx".Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                message = "FBX 파일이 아닙니다: " + assetPath;
                return false;
            }

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                message = "이 파일의 import 설정을 읽지 못했습니다: " + assetPath;
                return false;
            }

            List<string> changes = new List<string>();

            // ① Humanoid + importAnimation
            // [AUDIT][Risk:Major][Scope:humanoid_setup]
            // Unity는 animationType과 sourceAvatar를 한 번에 받아들이지 않는다: avatar 복사는 이미 임포트된 리그를
            // 기준으로 검증되므로, 둘을 한 번에 쓰면 조용히 CreateFromThisModel로 되돌아간다.
            // 그래서 각 단계는 다음 단계가 importer를 다시 읽기 전에 반드시 재임포트한다.
            if (importer.animationType != ModelImporterAnimationType.Human || !importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimation = true;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changes.Add("Animation Type = Humanoid");

                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                {
                    message = "재임포트 후 import 설정을 다시 읽지 못했습니다: " + assetPath;
                    return false;
                }
            }

            // [AUDIT][Scope:retarget_source]
            // 내보낸 리그의 Avatar를 복사하는 것을 시도하기는 하지만, Unity는 임포트한 모든 모델의 루트를 FILE
            // 이름으로 만들기 때문에 대상 skeleton은 원본 avatar와 언제나 그 루트 항목 하나만큼 다르고, Unity는
            // 조용히 CreateFromThisModel로 되돌린다. 이 설명은 fbx의 노드 표에서 직접 다시 측정한 것이다.
            // 이 주석이 예전에 달고 있던 숫자(106 대 103 일치)는 파일에 들어 있는 값이 아니었다:
            // ZepetoBaseModel.fbx에는 'ZepetoBaseModel'을 머리로 하는 Model 노드가 106개 있고,
            // ZepetoRig_Wave.fbx에는 루트 레벨 Model 노드가 둘 - 'body'(Mesh)와 'hips'(Null) - 있는데 Unity가
            // 파일 이름을 딴 'ZepetoRig_Wave' 루트를 덧붙이므로, 두 skeleton 모두 106개 항목이고 106개 중 105개
            // 이름이 일치하며 다른 것은 루트뿐이다. Blender는 armature 오브젝트를 실제로 다시 내보낸다. 그
            // 'hips' Null이 Lcl Scaling (0.01, 0.01, 0.01)을 지고 있는 노드이고, ZepetoRig_Wave.fbx.meta의
            // 모든 뼈별 "position error" 경고가 여기서 나온다 (전역 174752x 배율 하나이며 경고가 붙은 64개 뼈
            // 전부에서 동일하다 - skeleton이 망가진 것이 아니다). Assets/CustomMotions 아래의 모든 .fbx.meta가
            // 되돌림을 확인해 준다: avatarSetup: 1이 CreateFromThisModel이고, 2였던 적이 없다.
            //
            // 폴백은 받아들일 만하지만, 이 주석의 이전 판이 주장하던 이유 때문은 아니다.
            // Humanoid clip은 정규화된 근육 ANGLE과 월드 공간 body transform 하나, 그리고 손/발 IK 목표를 저장한다 -
            // 뼈 길이는 애초에 담을 수 없다 (hasTranslationDoF는 0이고 rotation/position/scale 커브 목록은 비어서
            // 나온다). 그러므로 원본 avatar가 비율을 "맞게" 만들어 주지 않는다. 그것이 정하는 것은 작성된 각도를
            // 어떤 매핑으로 되읽느냐뿐이다. 어느 avatar를 쓰든 비율 불일치는 임포트 오류가 아니라 재생 시점에
            // 발 미끄러짐과 twist 붕괴로 나타난다. DrawPublishGuide 참조.
            //
            // ② Avatar 복사 판정, 또는 오염된 .meta 복구
            // [AUDIT][Risk:Critical][Scope:avatar_poisoning]
            // 복사는 시도만 할 것이 아니라 GATE를 통과해야 한다. sourceAvatar를 대입하면 ZEPETO의
            // humanDescription이 대상의 .meta에 쓰이는데, 대상 skeleton이 그 뼈 이름들을 갖고 있지 않으면 그
            // asset은 오염된다: 이후의 모든 재임포트가
            //   "Avatar creation failed: Transform 'hips' for human bone 'Hips' not found"
            // 로 실패한다. 잘못된 매핑이 이제 이번 실행이 아니라 asset 자체의 일부이기 때문이다.
            // Assets/CustomMotions의 두 파일(Wave_Hello.fbx, AddonSmokeTest.fbx)이 정확히 그 상태였다 -
            // 21개 뼈짜리 generic skeleton에 이 줄이 돌기 전까지는 멀쩡히 임포트되고 있었다 - 그리고 .meta 파일을
            // 손으로 지우는 것이 유일한 탈출구였다. 뼈대가 맞지 않는 어떤 skeleton에서든 이 버튼을 누르면 그
            // 상태가 다시 만들어지므로, 막는 것만으로는 절반이다. 아래에서 남아 있는 매핑을 CLEAR까지 하는 이유는
            // 이 버튼이 사용자가 가진 유일한 복구 경로이기 때문이다.
            //
            // 복사를 건너뛰는 것은 성능이나 품질의 하향이 아니다. Humanoid clip은 정규화된 근육 각도를 담을 뿐
            // 뼈 이름을 전혀 담지 않으므로, FBX 자신의 avatar로 추출한 clip도 ZEPETO 아바타 위로 그대로
            // 리타게팅된다 - Wave_Hello.anim이 그 증거다: ZEPETO 뼈 이름 55개 중 0개인데도 130개 커브를 가진
            // 유효한 Humanoid clip이다. Generic/Mixamo 계열 이름(Hips, Spine, LeftArm)은 바로 Unity 자동
            // 매퍼의 어휘라서 그런 FBX는 스스로 avatar를 만들어 낸다. Unity가 자동 매핑하지 못하는 쪽은 ZEPETO
            // 이름이고, 그래서 리그에는 손으로 작성한 humanDescription이 존재한다.
            Avatar rigAvatar = FindExportedRigAvatar();

            // 아래 블록 밖에 둔다: 임포트 후 검증이 실패했을 때 복사를 거절한 이유가 곧 진단이 되는데,
            // 그 검증은 이유를 다시 계산할 수 없다.
            string copySkipReason = string.Empty;

            // "&& importer.sourceAvatar != rigAvatar"를 일부러 넣지 않았다. Unity는 거절된 복사를
            // CreateFromThisModel로 되돌리면서 복사된 매핑은 .meta에 남겨 두므로, 정작 복구가 필요한 asset에서
            // sourceAvatar가 null로 읽힌다 - 그 조건은 바로 그 asset들에 대해 이 블록을 건너뛰게 만들었고
            // 피해를 영구적으로 만들었다.
            if (rigAvatar != null)
            {
                if (!CanCopyRigAvatarTo(assetPath, rigAvatar, out copySkipReason))
                {
                    string clearedReason;
                    if (NeedsForeignAvatarMapCleared(importer, assetPath, out clearedReason))
                    {
                        ClearForeignAvatarMap(importer);
                        changes.Add("다른 리그의 Avatar 뼈 매핑을 지웠습니다 - 이제 이 FBX가 자기 뼈대로 Avatar를 "
                            + "만듭니다 (" + clearedReason + ")");

                        importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                    }
                    else
                    {
                        changes.Add("Avatar는 이 FBX에서 생성 - " + copySkipReason);
                    }
                }
                else if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther
                    || importer.sourceAvatar != rigAvatar)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = rigAvatar;
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();

                    importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                    if (importer != null && importer.sourceAvatar == rigAvatar)
                    {
                        changes.Add("Avatar = ZEPETO 리그에서 복사 (" + rigAvatar.name + ")");
                    }
                    else
                    {
                        changes.Add("Avatar는 이 FBX에서 생성 (모션 자체의 뼈대를 기준으로 읽습니다)");
                    }
                }
            }

            if (importer == null)
            {
                message = "import 설정을 다시 읽지 못했습니다: " + assetPath;
                return false;
            }

            // ③ Root Transform 고정
            // root motion을 pose에 구워 넣어, 미리보기 아바타가 부스 카메라가 향한 자리를 벗어나 걸어 나가지 않게 한다.
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].lockRootRotation = true;
                    clips[i].keepOriginalOrientation = true;
                    clips[i].lockRootHeightY = true;
                    clips[i].keepOriginalPositionY = true;
                    clips[i].lockRootPositionXZ = true;
                    clips[i].keepOriginalPositionXZ = false;
                }

                importer.clipAnimations = clips;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changes.Add("Root Transform 고정 (" + clips.Length + "개 클립)");
            }
            else
            {
                changes.Add("경고: 이 FBX에 애니메이션 클립이 없습니다");
            }

            // ④ 결과 검증
            if (!VerifyMotionFbxImportResult(assetPath, rigAvatar, copySkipReason, changes, out message))
            {
                return false;
            }

            // ⑤ 보고
            if (changes.Count == 0)
            {
                message = "이미 ZEPETO용 설정입니다: " + Path.GetFileName(assetPath);
                return true;
            }

            message = Path.GetFileName(assetPath) + " 설정 완료 — " + string.Join(", ", changes.ToArray());
            return true;
        }

        /// <summary>
        /// 임포트가 실제로 쓸 수 있는 Humanoid Avatar를 만들어냈는지 확인하고, 만들지 못했다면 사용자에게 보여줄
        /// 설명을 조립한다. 성공하면 message는 비어 있고 호출자가 자기 문장을 채운다.
        ///
        /// 위쪽 설정 단계와는 assetPath / rigAvatar / copySkipReason / changes 네 값으로만 이어져 있다.
        ///
        /// [AUDIT][Risk:Major][Scope:humanoid_setup]
        /// Avatar를 만들지 못한 Humanoid 임포트는 어디에서도 예외가 아니고 false 반환도 아니다 - Unity는 그
        /// 이유를 importer에 기록하고 Avatar가 아예 없는 모델을 돌려준다. 그래서 우리가 쓴 설정을 성공으로
        /// 보고하면 절대 재생될 수 없는 모션 항목이 만들어졌다. Assets/CustomMotions의 fbx 세 개 중 둘이 정확히
        /// 그 상태로 있었다: 뼈 이름은 Mixamo 계열(HumanoidRig/Hips/Spine/LeftArm, 22개 노드)인데 이 파이프라인이
        /// 얹어 놓은 humanDescription은 ZEPETO 뼈를 가리켰고, .meta에는
        /// rigImportErrors: "Avatar creation failed:\n\tTransform 'hips' for human bone 'Hips' not found"
        /// 가 기록돼 있었다. 위쪽의 clear가 그것을 이제 복구하지만 이 검사는 남는다: Avatar를 만들지 못하는 다른
        /// skeleton은 얼마든지 있고 아무리 지워도 그들에게는 도움이 되지 않는다. 뼈 이름을 다시 매핑하는 것은
        /// 의도적으로 범위 밖이며, 이 검사가 할 일은 결과에 대해 거짓말하지 않는 것뿐이다. 양쪽이 다 필요하다 -
        /// 기록된 텍스트는 원인을 말하고, Avatar는 그 clip이 실제로 리타게팅될 수 있는지를 말한다.
        ///
        /// importer를 한 번 더 읽어 오는 것부터 시작하는 이유: 위 clip 단계가 SaveAndReimport로 끝나는데 그것이
        /// 앞의 두 단계가 이미 막고 있는 것과 똑같은 방식으로 C# 래퍼를 무효화한다. 여기서 null이면 오류 텍스트를
        /// 잃을 뿐 검사 자체는 잃지 않는다 - 판정하는 것은 Avatar 쪽이다.
        /// </summary>
        private static bool VerifyMotionFbxImportResult(
            string assetPath,
            Avatar rigAvatar,
            string copySkipReason,
            List<string> changes,
            out string message)
        {
            message = string.Empty;

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            string rigImportError = GetRigImportErrorText(importer);
            Avatar producedAvatar = FindImportedModelAvatar(assetPath);
            bool avatarUsable = producedAvatar != null && producedAvatar.isValid && producedAvatar.isHuman;
            if (string.IsNullOrEmpty(rigImportError) && avatarUsable)
            {
                return true;
            }

            string detail = string.IsNullOrEmpty(rigImportError)
                ? (producedAvatar == null
                    ? "Avatar가 만들어지지 않았습니다"
                    : "Avatar 상태: isValid=" + producedAvatar.isValid + ", isHuman=" + producedAvatar.isHuman)
                : "Unity가 기록한 원인: " + (rigImportError.Length > 300
                    ? rigImportError.Substring(0, 300) + " …"
                    : rigImportError);

            // [AUDIT][Risk:Major][Scope:humanoid_setup]
            // 원인은 상태에서 읽어 내지, 추측하지 않는다. "뼈 이름이 ZEPETO와 다릅니다"라고 단정하는 것은 여기에
            // 도달하는 가장 흔한 경로에 대해 틀린 말이었다: 내보낸 리그가 없으면 원본 Avatar도 없으니 복사된 것이
            // 없고 FBX는 스스로 Avatar를 만들도록 남겨진 것인데 - ZEPETO 이름을 쓰는 skeleton은 그 이름들이
            // Unity 자동 매퍼의 어휘에 없어서 스스로 만들지 못한다. 리그가 실제로 있는 경우에는 복사를 거절한
            // 이유와 실제로 쓰인 설정이 곧 진단인데, 예전에는 그 둘을 모두 버리고 있었다.
            if (rigAvatar == null)
            {
                message = Path.GetFileName(assetPath)
                    + ": Humanoid Avatar를 만들지 못했습니다. 3번(ZEPETO 리그 내보내기)을 아직 하지 않아서 "
                    + "리타게팅 원본 Avatar가 없습니다. 이 FBX가 ZEPETO 리그 위에서 만든 동작이라면 "
                    + "뼈 이름(hips/spine/upperArm_L)을 Unity가 스스로 매핑하지 못하므로, 내보낸 리그의 "
                    + "Avatar를 복사하는 것 말고는 Avatar를 만들 방법이 없습니다. "
                    + "3번을 먼저 누른 뒤 이 버튼을 다시 누르세요. " + detail;
            }
            else
            {
                message = Path.GetFileName(assetPath)
                    + ": Humanoid Avatar를 만들지 못했습니다. 이 FBX는 ZEPETO 아바타로 재생할 수 없습니다. "
                    + "뼈 이름을 자동으로 바꿔주는 기능은 없습니다. 4번의 Blender 애드온으로 ZEPETO 리그 위에 "
                    + "동작을 만들어 내보내는 방법만 지원합니다."
                    + (string.IsNullOrEmpty(copySkipReason)
                        ? string.Empty
                        : " Avatar 복사 판정: " + copySkipReason + ".")
                    + (changes.Count == 0
                        ? string.Empty
                        : " 적용한 설정: " + string.Join(", ", changes.ToArray()) + ".")
                    + " " + detail;
            }

            return false;
        }

        /// <summary>
        /// Unity가 model importer에 기록해 둔 rig import 오류를 HelpBox에 들어가도록 한 줄로 펴서 돌려준다.
        /// 임포트가 아무것도 보고하지 않았으면 빈 문자열.
        ///
        /// ModelImporter는 이 값을 공개 API로 노출하지 않는다. m_RigImportErrors는 Unity 자신의 Rig 인스펙터가
        /// 바인딩하는 직렬화 이름이고 .meta에 rigImportErrors로 쓰여 나오는 것이다. 그 뒤의 이름 훑기는 필드
        /// 이름이 바뀌었을 때 예외 대신 "보고된 것 없음"으로 떨어지게 하려고 있다. NextVisible이 아니라
        /// Next(true)를 쓰는 것은 이 프로퍼티가 기본 인스펙터에서 숨겨져 있기 때문이다.
        ///
        /// propertyType 검사는 장식이 아니다: stringValue는 다른 타입의 프로퍼티에서 예외를 던지고, 두 조회 모두
        /// 이름으로 찾기 때문이다. `using`이 필요한 것은 ConfigureMotionFolderForLivePreview가 폴더 안의 fbx마다
        /// 이 함수를 한 번씩 부르는데, SerializedObject는 dispose될 때까지 네이티브 핸들을 붙잡고 있기 때문이다.
        /// </summary>
        private static string GetRigImportErrorText(ModelImporter importer)
        {
            if (importer == null)
            {
                return string.Empty;
            }

            using (SerializedObject serialized = new SerializedObject(importer))
            {
                SerializedProperty direct = serialized.FindProperty("m_RigImportErrors");
                if (direct != null && direct.propertyType == SerializedPropertyType.String)
                {
                    return FlattenImporterMessage(direct.stringValue);
                }

                SerializedProperty iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.String
                        && iterator.name.IndexOf("RigImportError", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return FlattenImporterMessage(iterator.stringValue);
                    }
                }
            }

            return string.Empty;
        }

        private static string FlattenImporterMessage(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }

        /// <summary>
        /// Unity가 이 model asset에 대해 실제로 만들어 낸 Avatar, avatar 생성이 실패했으면 null.
        ///
        /// FindExportedRigAvatar와 달리 isHuman으로 거르지 않는다: 호출자는 "Avatar가 아예 없다"와
        /// "Avatar는 있는데 Humanoid가 아니다"를 구분해야 하고, 그 둘은 화면에 다른 문장을 필요로 한다.
        /// </summary>
        private static Avatar FindImportedModelAvatar(string assetPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                Avatar avatar = assets[i] as Avatar;
                if (avatar != null)
                {
                    return avatar;
                }
            }

            return null;
        }

        /// <summary>
        /// 리그 Avatar의 humanDescription을 이 모델에 복사해도 모델이 망가지지 않는지.
        ///
        /// Unity는 이것을 대신 검증해 주지 않는다: sourceAvatar를 대입하면 대상 skeleton이 어떻게 생겼든 원본
        /// humanDescription이 대상의 .meta에 쓰이고, 대상에 없는 뼈를 가리키는 매핑은 그때부터 그 asset의 avatar
        /// 생성을 계속 실패시킨다. 그래서 뼈 이름은 대입 전에 확인해야 한다 - 뒤에 확인하는 것은 막기에 이미
        /// 늦다. 이미 다른 리그의 매핑을 들고 있는 .meta를 복구하는 것이 나머지 절반이고, 그쪽은
        /// NeedsForeignAvatarMapCleared에 있다.
        ///
        /// 뼈의 존재만 보고 계층은 보지 않는다: Unity는 임포트한 모든 모델의 루트를 파일 이름으로 만들기 때문에
        /// 두 skeleton은 언제나 그 루트 항목 하나만큼 다르고, 정확한 일치는 애초에 도달할 수 없다.
        /// </summary>
        private static bool CanCopyRigAvatarTo(string assetPath, Avatar sourceAvatar, out string reason)
        {
            reason = string.Empty;

            HumanBone[] humanBones = sourceAvatar == null
                ? null
                : sourceAvatar.humanDescription.human;
            if (humanBones == null || humanBones.Length == 0)
            {
                reason = "리그 Avatar에 사람 뼈 매핑이 없습니다";
                return false;
            }

            HashSet<string> modelBoneNames = CollectModelBoneNames(assetPath);
            if (modelBoneNames == null)
            {
                reason = "임포트된 모델을 읽지 못했습니다";
                return false;
            }

            int missingCount;
            string firstMissingName;
            CountMissingHumanBones(humanBones, modelBoneNames, out missingCount, out firstMissingName);

            if (missingCount > 0)
            {
                reason = "이 FBX의 뼈 이름이 ZEPETO 리그와 달라 Avatar를 복사하지 않았습니다 ("
                    + humanBones.Length + "개 중 " + missingCount + "개 없음, 예: " + firstMissingName + ")";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 임포트된 모델의 모든 transform 이름, 모델 자체를 읽지 못했으면 null. 복사 게이트와 오염 해제 검사가
        /// 함께 쓴다. "이 FBX가 실제로 가진 뼈가 무엇인가"의 정의를 하나로 두기 위해서다.
        /// </summary>
        private static HashSet<string> CollectModelBoneNames(string assetPath)
        {
            GameObject model = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            if (model == null)
            {
                return null;
            }

            HashSet<string> modelBoneNames = new HashSet<string>(StringComparer.Ordinal);
            Transform[] modelTransforms = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < modelTransforms.Length; i++)
            {
                modelBoneNames.Add(modelTransforms[i].name);
            }

            return modelBoneNames;
        }

        /// <summary>
        /// 사람 뼈 매핑이 가리키는 이름 중 모델에 없는 것이 몇 개인지와, 메시지에 쓸 첫 번째 이름.
        /// boneName이 비어 있는 항목은 없는 뼈가 아니라 매핑되지 않은 선택적 뼈다.
        /// </summary>
        private static void CountMissingHumanBones(
            HumanBone[] humanBones,
            HashSet<string> modelBoneNames,
            out int missingCount,
            out string firstMissingName)
        {
            missingCount = 0;
            firstMissingName = string.Empty;

            for (int i = 0; i < humanBones.Length; i++)
            {
                string boneName = humanBones[i].boneName;
                if (string.IsNullOrEmpty(boneName) || modelBoneNames.Contains(boneName))
                {
                    continue;
                }

                missingCount++;
                if (firstMissingName.Length == 0)
                {
                    firstMissingName = boneName;
                }
            }
        }

        /// <summary>
        /// 이 asset이 다시 임포트되려면 먼저 지워야 하는 Avatar 설정을 지고 있는지.
        ///
        /// [AUDIT][Risk:Critical][Scope:avatar_poisoning]
        /// 복사를 거절하는 것은 아직 오염되지 않은 FBX만 보호한다. 이미 .meta에 다른 리그의 humanDescription을
        /// 들고 있는 asset은 복구되어야 하고, 이 버튼이 사용자가 가진 유일한 복구 경로다 - 그렇지 않으면 해결책은
        /// ".meta를 손으로 지운다"인데 그걸 찾아낼 사람은 없다.
        ///
        /// avatarSetup/sourceAvatar를 되돌리는 것만으로는 충분하지 않고, 그래서 저장된 매핑까지 들여다본다.
        /// Unity는 거절된 복사를 avatarSetup: 1(CreateFromThisModel)과
        /// lastHumanDescriptionAvatarSource: {instanceID: 0}으로 되돌리면서 복사된 humanDescription은 그대로 둔다 -
        /// Assets/CustomMotions/ZepetoRig_Wave.fbx.meta에서 측정한 값이 그렇다: avatarSetup 1, source instanceID 0,
        /// 그리고 'boneName: hips'로 시작하는 55개 항목짜리 human 목록. 즉 오염된 asset에서는 그 두 필드가 이미
        /// 깨끗하게 읽히는데 avatar 생성을 망가뜨리는 매핑은 그대로 남아 있다. 없애야 하는 것은 그 매핑이다.
        ///
        /// asset이 아직 쓸 만한 Humanoid Avatar를 만들어 내는 동안에는 아무것도 다시 쓰지 않으며, 그 전제 하나가
        /// 이 복구를 새로운 버그로 만들지 않는 장치다. 손으로 설정한 매핑은 절대 지워지지 않고,
        /// optimizeGameObjects가 뼈 계층을 걷어낸 모델도 마찬가지다 - 그런 모델은 transform 이름이 사라져서 어떤
        /// 매핑이든 남의 것처럼 보이지만 Avatar는 멀쩡하므로 건드리지 않는다.
        ///
        /// 반복에 빠질 수도 없다. clear 후에는 Unity가 이 모델 자신의 뼈를 자동 매핑하므로
        /// (autoGenerateAvatarMappingIfUnspecified: 1), 저장된 매핑은 존재하는 뼈만 가리킬 수 있고 설정은 이미
        /// CreateFromThisModel/null이다. 그러면 다음에 눌렀을 때 두 검사 모두 할 일 없음을 보고한다 -
        /// Avatar가 쓸 만하게 나왔든 아니든 마찬가지다.
        /// </summary>
        private static bool NeedsForeignAvatarMapCleared(ModelImporter importer, string assetPath, out string reason)
        {
            reason = string.Empty;

            if (importer == null)
            {
                return false;
            }

            Avatar current = FindImportedModelAvatar(assetPath);
            if (current != null && current.isValid && current.isHuman)
            {
                return false;
            }

            HumanBone[] storedBones = importer.humanDescription.human;
            if (storedBones != null && storedBones.Length > 0)
            {
                HashSet<string> modelBoneNames = CollectModelBoneNames(assetPath);
                if (modelBoneNames != null)
                {
                    int missingCount;
                    string firstMissingName;
                    CountMissingHumanBones(storedBones, modelBoneNames, out missingCount, out firstMissingName);
                    if (missingCount > 0)
                    {
                        reason = ".meta에 남아있던 뼈 매핑이 이 FBX에 없는 뼈를 가리킵니다 ("
                            + storedBones.Length + "개 중 " + missingCount + "개 없음, 예: "
                            + firstMissingName + ")";
                        return true;
                    }
                }
            }

            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                || importer.sourceAvatar != null)
            {
                reason = "다른 리그의 Avatar를 복사하도록 설정돼 있었습니다";
                return true;
            }

            return false;
        }

        /// <summary>
        /// importer를 한 번도 설정한 적 없는 Humanoid 임포트의 모습으로 되돌리고 재임포트한다.
        ///
        /// human/skeleton 목록을 비우는 것이 Unity의 autoGenerateAvatarMappingIfUnspecified 플래그가 반응하는
        /// "지정되지 않음" 상태이므로, 재임포트는 남의 매핑을 다시 쓰는 대신 이 모델 자신의 뼈를 매핑한다.
        /// 언제나 NeedsForeignAvatarMapCleared 뒤에서만 불리며, 무엇을 지워도 안전한지에 대한 판단은 그쪽에 있다.
        /// </summary>
        private static void ClearForeignAvatarMap(ModelImporter importer)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;

            HumanDescription cleared = importer.humanDescription;
            cleared.human = new HumanBone[0];
            cleared.skeleton = new SkeletonBone[0];
            importer.humanDescription = cleared;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 임포트된 FBX 안에 들어 있는 clip을 커스텀 모션 폴더 아래의 독립 .anim으로 복사한다.
        /// model asset 안에 박혀 있는 clip은 읽기 전용이라, 배속 / 자르기 / 반복 편집에는 사본이 필요하다.
        /// </summary>
        private bool TryExtractMotionFromFbx(string assetPath, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrEmpty(assetPath))
            {
                message = "FBX 파일을 Project 창에서 먼저 선택하세요.";
                return false;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            AnimationClip source = null;
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip candidate = assets[i] as AnimationClip;
                // model importer는 숨겨진 __preview__ clip을 하나 더 붙인다. 그것은 건너뛴다.
                if (candidate != null && (candidate.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    source = candidate;
                    break;
                }
            }

            if (source == null)
            {
                message = "이 FBX 안에서 애니메이션 클립을 찾지 못했습니다. Mixamo에서 'With Skin' 대신 애니메이션이 포함된 상태로 받았는지 확인하세요.";
                return false;
            }

            if (!source.isHumanMotion)
            {
                message = "클립이 Humanoid가 아닙니다. 먼저 'FBX를 ZEPETO용으로 설정'을 누르세요.";
                return false;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Motions");

            string safeName = MakeExportSafeFileName(Path.GetFileNameWithoutExtension(assetPath));
            string destination = AssetDatabase.GenerateUniqueAssetPath(CustomMotionRoot + "/" + safeName + ".anim");

            AnimationClip copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(destination);
            AssetDatabase.CreateAsset(copy, destination);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(destination);

            AnimationClip created = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (created == null)
            {
                message = "클립을 저장하지 못했습니다: " + destination;
                return false;
            }

            LoadPackageAnimations();
            for (int i = 0; i < motionEntries.Count; i++)
            {
                if (motionEntries[i].Clip == created)
                {
                    selectedAnimationIndex = i;
                    break;
                }
            }

            SelectAndPing(created);
            message = "내 모션으로 추가했습니다: " + destination
                + " (" + created.length.ToString("0.00") + "초). 2번 목록에서 바로 고를 수 있습니다.";
            return true;
        }

        private static string GetSelectedFbxPath()
        {
            UnityEngine.Object selection = Selection.activeObject;
            if (selection == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(selection);
            return ".fbx".Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase) ? path : string.Empty;
        }

        /// <summary>
        /// 라이브 루프를 거치지 않고 들어온 fbx를 등록하는 자리 - Mixamo 다운로드이거나, 라이브 미리보기를
        /// 설정하기 전에 만든 Blender export. 5번의 라이브 경로는 이 둘을 자동으로 해 준다.
        ///
        /// 여기 버튼의 1·2는 이 상자 안에서만 쓰는 순서다. Unity 카드 번호(1~7)도, Blender 애드온 패널의
        /// 단계 번호도 아니다 - 세 체계의 관계는 GoToBlender.cs의 DrawGoToBlenderBody 머리 주석 참조.
        /// </summary>
        private void DrawManualMotionImportBody()
        {
            string fbxPath = GetSelectedFbxPath();
            DrawStatusRow("선택된 FBX", string.IsNullOrEmpty(fbxPath) ? "없음 - Project 창에서 FBX 선택" : fbxPath);

            bool hasFbx = !string.IsNullOrEmpty(fbxPath) && !EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!hasFbx))
            {
                if (DrawBlueActionButton("1. FBX를 ZEPETO용으로 설정", hasFbx, GUILayout.Height(26f)))
                {
                    string message;
                    TryConfigureMotionFbx(fbxPath, out message);
                    statusMessage = message;
                    ValidateState();
                }

                if (DrawBlueActionButton("2. 내 모션으로 추가", hasFbx, GUILayout.Height(26f)))
                {
                    string message;
                    TryExtractMotionFromFbx(fbxPath, out message);
                    statusMessage = message;
                    ValidateState();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawMiniHelp(
                "Project 창에서 FBX를 클릭한 뒤 1번 → 2번 순서로 누릅니다. "
                + "ZEPETO는 Humanoid 애니메이션만 재생합니다.",
                MessageType.None);
        }
    }
}
