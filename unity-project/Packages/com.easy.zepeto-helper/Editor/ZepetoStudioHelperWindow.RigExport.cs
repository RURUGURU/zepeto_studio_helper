using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// ZEPETO가 공유하는 기본 몸을 FBX로 내보내서 진짜 뼈 이름과 계층 위에서 모션을 만들 수 있게 하고,
    /// 그 모델의 Avatar를 리타게팅 원본으로 삼아 애니메이션을 되받아오는 부분.
    ///
    /// 이걸로 얻지 못하는 것은 "내 아바타"다. ZepetoBaseModel은 모두가 출발점으로 쓰는 몸이고, 특정 사용자의
    /// 얼굴·의상·체형 스케일은 Play 중 런타임 다운로드 이후에만 존재한다.
    /// 리타게팅이 정확해지는 것도 아니다 - Humanoid 클립에는 뼈 길이를 담을 자리가 없어서, 비율 오차는 여기가
    /// 아니라 재생 시점에 드러난다(발 미끄러짐, twist 붕괴). 그래도 진짜 계층 위에서 만드는 것은 여전히 중요하다.
    /// 뼈 이름과 rest pose와 twist 뼈 배치를 맞게 가져가기 때문이다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string ZepetoBaseModelPath = "Packages/zepeto.character/resources/zepeto/ZepetoBaseModel.prefab";
        private const string RigExportRoot = "Assets/ZepetoHelper/Rig";
        private const string ExportedRigPath = RigExportRoot + "/ZepetoBaseModel.fbx";

        // 아래 네 개의 타입 이름과 이 파일의 모든 reflection은 한 가지 이유에서 나온다: Unity FBX Exporter는
        // 선택 설치 패키지다. 직접 참조하면 그것을 설치하지 않은 프로젝트에서 이 헬퍼가 컴파일되지 않는다.
        // 그래서 이 안에서 무엇이든 null이면 그것은 "고장"이 아니라 "설치되어 있지 않음"으로 읽어야 한다 -
        // 타입, 메서드, 프로퍼티 어느 단계에서 null이 나오든 마찬가지다.
        private const string FbxExporterTypeName = "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor";
        private const string ExportOptionsTypeName = "UnityEditor.Formats.Fbx.Exporter.ExportModelOptions, Unity.Formats.Fbx.Editor";
        private const string ExportFormatTypeName = "UnityEditor.Formats.Fbx.Exporter.ExportFormat, Unity.Formats.Fbx.Editor";

        private static MethodInfo FindFbxExportMethod()
        {
            Type exporter = Type.GetType(FbxExporterTypeName);
            if (exporter == null)
            {
                return null;
            }

            // ExportObject(string filePath, UnityEngine.Object singleObject)
            return exporter.GetMethod(
                "ExportObject",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(UnityEngine.Object) },
                null);
        }

        /// <summary>
        /// BINARY fbx를 강제하는 export 옵션을 만든다.
        ///
        /// [AUDIT][Risk:Major][Scope:blender_roundtrip]
        /// ExportModelOptions의 기본값은 ExportFormat.ASCII인데, Blender는 ASCII fbx를 아예 거부한다
        /// ("ASCII FBX files are not supported"). Unity는 어느 쪽이든 export 성공으로 보고하므로, 이 문제는
        /// 파일을 실제로 Blender에서 열어봐야 드러난다.
        ///
        /// 옵션 타입을 못 구하면 null을 돌려준다. 그러면 호출자는 인자 2개짜리 ExportObject 오버로드를 쓰게 되는데,
        /// 그것은 exportOptions: null을 넘기는 것이고 - 그 경우 프로젝트의 Fbx Export 설정으로 폴백하지 않는다.
        /// exportFormat 필드가 ASCII인 ExportModelSettingsSerialize를 새로 만든다
        /// (com.unity.formats.fbx 5.1.6, ExportOptionsSettingsSerializeBase:251에서 확인).
        /// 즉 폴백 경로는 ASCII밖에 쓸 수 없고, 그래서 TryExportZepetoRigToFbx는 그 결과물을 남겨두지 않고 지운다.
        /// </summary>
        private static object BuildBinaryExportOptions()
        {
            try
            {
                Type optionsType = Type.GetType(ExportOptionsTypeName);
                Type formatType = Type.GetType(ExportFormatTypeName);
                if (optionsType == null || formatType == null)
                {
                    return null;
                }

                object options = Activator.CreateInstance(optionsType);
                PropertyInfo format = optionsType.GetProperty("ExportFormat");
                if (format == null || !format.CanWrite)
                {
                    return null;
                }

                format.SetValue(options, Enum.Parse(formatType, "Binary"), null);
                return options;
            }
            catch
            {
                return null;
            }
        }

        private static MethodInfo FindFbxExportMethodWithOptions(Type optionsType)
        {
            Type exporter = Type.GetType(FbxExporterTypeName);
            if (exporter == null || optionsType == null)
            {
                return null;
            }

            return exporter.GetMethod(
                "ExportObject",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(UnityEngine.Object), optionsType },
                null);
        }

        private static bool IsFbxExporterInstalled()
        {
            return FindFbxExportMethod() != null;
        }

        /// <summary>
        /// 기본 몸을 ExportedRigPath로 내보낸다. 흐름은 다섯 토막이다:
        ///   ① 전제조건 - Play 아님, FBX Exporter 있음, 기본 모델 있음, 출력 폴더 있음
        ///   ② 임시 인스턴스 - prefab asset을 직접 내보내면 exporter가 훑는 pose/계층이 사라진다
        ///   ③ reflection export - 옵션을 만들 수 있으면 3-arg, 아니면 2-arg 오버로드
        ///   ④ finally 정리 - 인스턴스는 어느 경로로 나가든 반드시 파괴된다
        ///   ⑤ 단일 실패 출구 - 아래 [AUDIT] 블록이 이유를 적어 둔, 여기서 유일하게 return false 하는 자리
        /// exportOk가 message와 별개의 플래그로 존재하는 이유가 여기 있다. try 안에서 곧바로 return하면 ④를
        /// 건너뛰거나 ⑤를 건너뛰게 되므로, 성공 여부는 플래그로 들고 나오고 message는 실패 사유를 담아 나온다.
        /// ⑤ 이후의 "검증하고 받아들이는" 절반은 TryAdoptExportedRig에 있으며, 이 절반과는 exportOk /
        /// requestedBinary 두 값으로만 연결된다.
        /// </summary>
        private bool TryExportZepetoRigToFbx(out string message)
        {
            message = string.Empty;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play 중에는 내보낼 수 없습니다. 먼저 Stop을 누르세요.";
                return false;
            }

            MethodInfo exportObject = FindFbxExportMethod();
            if (exportObject == null)
            {
                message = "Unity FBX Exporter 패키지가 없습니다. Package Manager에서 'FBX Exporter'를 설치하세요 "
                    + "(Packages/manifest.json에 com.unity.formats.fbx 추가).";
                return false;
            }

            GameObject baseModel = AssetDatabase.LoadAssetAtPath<GameObject>(ZepetoBaseModelPath);
            if (baseModel == null)
            {
                message = "ZEPETO 기본 모델을 찾지 못했습니다: " + ZepetoBaseModelPath;
                return false;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Rig");

            // 임시 인스턴스를 내보낸다: prefab asset을 그대로 내보내면 exporter가 훑는 pose/계층을 잃고,
            // 패키지 asset 자체를 건드려서도 안 된다.
            GameObject instance = UnityEngine.Object.Instantiate(baseModel);
            instance.name = "ZepetoBaseModel";

            // 텍스처 참조를 끊고 내보낸다. 이유는 StripTexturesForExport의 주석에 있다.
            // 여기서 만들어진 임시 재질들은 아래 finally에서 인스턴스와 함께 반드시 파괴한다.
            List<Material> temporaryMaterials = StripTexturesForExport(instance);

            bool requestedBinary = false;
            bool exportOk = false;

            try
            {
                string absolute = ToAbsoluteProjectPath(ExportedRigPath);

                object binaryOptions = BuildBinaryExportOptions();
                MethodInfo withOptions = binaryOptions == null
                    ? null
                    : FindFbxExportMethodWithOptions(binaryOptions.GetType());
                requestedBinary = withOptions != null;

                object result = requestedBinary
                    ? withOptions.Invoke(null, new object[] { absolute, instance, binaryOptions })
                    : exportObject.Invoke(null, new object[] { absolute, instance });

                string written = result as string;

                if (string.IsNullOrEmpty(written) || !File.Exists(absolute))
                {
                    message = "FBX 내보내기가 실패했습니다. Console을 확인하세요.";
                }
                else
                {
                    exportOk = true;

                    if (!requestedBinary)
                    {
                        Debug.LogWarning("Easy ZEPETO Helper: the installed FBX Exporter did not expose the "
                            + "ExportModelOptions overload, so ExportFormat.Binary could not be requested. The rig "
                            + "was written as ASCII and is about to be rejected and deleted.");
                    }
                }
            }
            catch (Exception exception)
            {
                message = "FBX 내보내기 중 오류: " + exception.Message;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                // 인스턴스를 파괴해도 재질은 따라 죽지 않는다. new Material(...)로 만든 것은 씬 오브젝트가
                // 아니라 떠 있는 애셋이라, 여기서 지우지 않으면 에디터를 닫을 때까지 메모리에 남는다.
                for (int i = 0; i < temporaryMaterials.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryMaterials[i]);
                }
            }

            return TryAdoptExportedRig(exportOk, requestedBinary, ref message);
        }

        /// <summary>
        /// 내보내기 직전에 임시 인스턴스의 모든 텍스처 참조를 끊는다.
        ///
        /// [AUDIT][Risk:Major][Scope:blender_roundtrip]
        /// ZepetoBaseModel의 텍스처는 prefab 안에 끼워 넣어진(embedded) 서브애셋이다. 그래서
        /// AssetDatabase.GetAssetPath(texture)가 텍스처 파일이 아니라 그것을 품고 있는 .prefab의 경로를
        /// 돌려준다. FBX exporter는 그 문자열을 그대로 FbxFileTexture의 파일명으로 적고, Blender는 임포트할
        /// 때마다 그 경로를 이미지로 열려고 시도해서 실패한다:
        ///     ERROR IMB_load_image_from_memory: unknown file-format (....../ZepetoBaseModel.prefab)
        /// 리그를 불러올 때마다 콘솔에 뜨는 이 에러는 실제로 아무것도 망가뜨리지 않지만, 처음 쓰는 사람은
        /// 자기가 뭘 잘못했다고 읽는다. 그리고 사라지지 않는 빨간 줄은 진짜 에러를 묻어 버린다.
        ///
        /// 두 번째 이유가 더 무겁다. exporter는 그 경로를 절대 경로로도 한 번 적는데, 그 문자열에는 내보낸
        /// 사람의 계정 이름이 들어 있다(C:\Users\&lt;계정&gt;\...). 이 fbx는 저장소에 커밋되는 파일이라,
        /// 고치지 않으면 릴리스마다 계정 이름을 같이 배포하게 된다.
        ///
        /// 텍스처를 버려도 잃는 것이 없다: 이 fbx는 Blender에서 뼈 이름·rest pose·몸 실루엣을 보려고 쓰는
        /// 것이고, 애드온은 임포트하자마자 메시를 hide_select로 잠가 클릭조차 막는다.
        ///
        /// 공유 재질을 직접 건드리지 않는 것이 중요하다 - 그것은 패키지 애셋이다. 사본을 만들어 인스턴스에만
        /// 꽂고, 사본은 호출자가 finally에서 파괴한다.
        /// </summary>
        private static List<Material> StripTexturesForExport(GameObject instance)
        {
            List<Material> temporaries = new List<Material>();

            // 꺼져 있는 렌더러까지 포함한다(includeInactive: true). exporter의 ExportUnrendered 기본값이
            // 참이라 비활성 렌더러도 내보내지는데, 여기서 빠뜨리면 그 하나가 .prefab 경로를 도로 써 넣는다.
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] originals = renderers[r].sharedMaterials;
                Material[] replacements = new Material[originals.Length];

                for (int m = 0; m < originals.Length; m++)
                {
                    if (originals[m] == null)
                    {
                        // 빈 슬롯은 빈 채로 둔다. 여기에 재질을 채워 넣으면 원본에 없던 것을 만들어 내는 것이다.
                        continue;
                    }

                    Material copy = new Material(originals[m]);
                    // 이름은 유지한다. FBX의 재질 이름이 바뀌면 Blender에서 몸 파트를 구분할 수 없게 된다.
                    copy.name = originals[m].name;
                    // 이 사본은 어디에도 저장되면 안 된다. 씬을 저장하는 순간 딸려 들어가면 그때부터 프로젝트에
                    // 정체불명의 재질이 남는다.
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    ClearTextureProperties(copy);

                    replacements[m] = copy;
                    temporaries.Add(copy);
                }

                renderers[r].sharedMaterials = replacements;
            }

            return temporaries;
        }

        /// <summary>
        /// 셰이더가 선언한 모든 텍스처 슬롯을 null로 만든다.
        ///
        /// mainTexture 하나만 지우면 안 된다. ZEPETO 셰이더는 _MainTex 말고도 노멀·마스크 슬롯을 갖고 있고,
        /// exporter는 그중 무엇이든 발견하는 대로 FbxFileTexture를 쓴다 - 하나만 남아도 .prefab 경로가
        /// 그대로 파일에 들어간다. 그래서 프로퍼티 목록을 셰이더에서 직접 읽어 전부 훑는다.
        /// </summary>
        private static void ClearTextureProperties(Material material)
        {
            Shader shader = material.shader;
            if (shader == null)
            {
                return;
            }

            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }

                material.SetTexture(ShaderUtil.GetPropertyName(shader, i), null);
            }
        }

        /// <summary>
        /// 방금 쓰인 파일을 검증하고, 통과하면 프로젝트가 쓸 수 있게 받아들인다(임포트, Humanoid, Avatar 보고).
        /// 실패한 export의 message는 이미 채워져 있으므로 덮어쓰지 않는다.
        /// </summary>
        private bool TryAdoptExportedRig(bool exportOk, bool requestedBinary, ref string message)
        {
            // [AUDIT][Risk:Major][Scope:rig_export_done_means_openable]
            // 모든 실패는 try 안에서 return하지 않고 이 하나의 출구로 빠져나온다. 실패 보고든 던져진 예외든
            // ASCII 파일만큼이나 쉽게 ExportedRigPath에 반쪽짜리 fbx를 남길 수 있기 때문이다 - exporter는 계층을
            // 훑어가며 쓴다. 3번 카드의 완료 판정은 그 경로에 대한 맨 File.Exists 하나뿐이므로
            // (Flow.cs의 DrawStep3ExportBody), 남겨진 파일은 망가진 export에서도 카드를 초록으로 만들고 4번의
            // "3번을 먼저 하세요" 경고까지 없애버린다. DeleteRejectedRigExport는 쓰인 것이 없으면 아무 일도 하지
            // 않으므로, 모든 실패 경로에서 불러도 비용이 없다.
            if (!exportOk)
            {
                DeleteRejectedRigExport();
                return false;
            }

            // [AUDIT][Risk:Major][Scope:blender_roundtrip]
            // 판정하는 것은 exporter의 반환값이 아니라 매직 바이트 검사다: ExportObject는 ASCII 파일에 대해서도
            // binary와 똑같이 기꺼이 경로를 돌려준다. 이 검사가 importer 작업보다 먼저 오는 데는 두 가지 이유가
            // 있다. 곧 버릴 파일에 animationType을 설정하는 것은 1.4MB 모델을 통째로 SaveAndReimport 하는 헛일이고,
            // 그 과정에서 AssetDatabase까지 더럽힌다. 그리고 "3번이 끝났다"고 말하는 것은 ExportedRigPath에 대한
            // 맨 File.Exists 하나뿐이라(Flow.cs의 DrawStep3ExportBody), 거부된 파일을 디스크에 남기면 카드가
            // 초록이 되고 4번의 경고가 사라져서 - Blender가 열지 못하는 파일을 사용자에게 가리키게 된다.
            if (!IsBinaryFbx(ToAbsoluteProjectPath(ExportedRigPath)))
            {
                DeleteRejectedRigExport();
                message = "내보낸 FBX가 ASCII 형식이라 삭제했습니다. Blender는 ASCII FBX를 열지 못합니다"
                    + " (\"ASCII FBX files are not supported\"). "
                    + (requestedBinary
                        ? "설치된 FBX Exporter 패키지가 Binary 형식 지정을 받아들이지 않았습니다. "
                        : "설치된 FBX Exporter 패키지 버전에서는 Binary 모드를 지정할 방법이 없었습니다. ")
                    + "Package Manager에서 FBX Exporter(com.unity.formats.fbx)를 설치하거나, 이미 있다면 "
                    + "Remove 후 다시 Install 해서 복구한 뒤 3번을 다시 누르세요. "
                    + "Project Settings의 Fbx Export 설정을 바꾸는 것으로는 해결되지 않습니다.";
                return false;
            }

            AssetDatabase.ImportAsset(ExportedRigPath, ImportAssetOptions.ForceUpdate);

            // 내보낸 리그를 Humanoid로 만들어, 모든 애니메이션이 리타게팅해 갈 Avatar를 생성하게 한다.
            ModelImporter importer = AssetImporter.GetAtPath(ExportedRigPath) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
            }

            Avatar avatar = FindExportedRigAvatar();
            message = "ZEPETO 리그를 내보냈습니다: " + ExportedRigPath
                + (avatar == null
                    ? " (Avatar 생성 실패 - Inspector에서 Rig > Animation Type을 확인하세요)"
                    : " / Avatar: " + avatar.name + " (isHuman=" + avatar.isHuman + ")");

            SelectAndPing(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ExportedRigPath));
            return true;
        }

        /// <summary>
        /// binary fbx는 "Kaydara FBX Binary"라는 문자열로 시작한다(18바이트). 그 밖은 전부 ASCII이고,
        /// Unity는 기꺼이 내보냈지만 Blender는 임포트에서 거부한다.
        /// </summary>
        private static bool IsBinaryFbx(string absolutePath)
        {
            try
            {
                if (!File.Exists(absolutePath))
                {
                    return false;
                }

                using (FileStream stream = File.OpenRead(absolutePath))
                {
                    byte[] header = new byte[18];
                    int read = stream.Read(header, 0, header.Length);
                    if (read < header.Length)
                    {
                        return false;
                    }

                    return System.Text.Encoding.ASCII.GetString(header) == "Kaydara FBX Binary";
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 남겨두면 안 되는 export 결과물을 .meta까지 함께 지운다. TryAdoptExportedRig의 실패 출구 전부에서
        /// 불린다 - binary 검사, exporter의 실패 보고, 예외 경로 - 셋 다 ExportedRigPath에 반쪽짜리거나 열 수
        /// 없는 파일을 남길 수 있기 때문이다.
        ///
        /// [QC][Invariant:rig_export_done_means_openable]
        /// "그 export가 쓸 만했는가"를 기록하는 것은 아무것도 없다. 3번의 완료 판정도 4번의 게이트도 둘 다
        /// ExportedRigPath에 대한 맨 File.Exists다(Flow.cs의 DrawStep3ExportBody, GoToBlender.cs의
        /// DrawGoToBlenderBody). 그래서 거부된 파일을 남겨서는 안 된다: 카드를 초록으로 만들고, 4번의
        /// "3번을 먼저 하세요" 경고를 감추고, Blender가 열지 못하는 파일을 가리키게 된다. .meta도 같이 지운다.
        /// 안 그러면 다음 시도가 죽은 파일의 importer 설정을 물려받고 Unity가 고아 meta 경고를 남긴다.
        ///
        /// 디스크에 아무것도 없으면 할 일도 없고, 그 출구를 맨 앞에 두어 파일을 아예 만들지 못한 실패가
        /// AssetDatabase.Refresh 비용을 내지 않게 한다.
        ///
        /// DeleteAsset을 파일시스템보다 먼저 시도하는 것은 .meta와 AssetDatabase 항목을 한 번에 처리해 주기
        /// 때문이다. 다만 AssetDatabase가 한 번도 임포트한 적 없는 경로에는 false를 돌려주는데 - 여기서는 그게
        /// 보통이다. 검증이 ImportAsset보다 먼저 돌기 때문이다. 파일시스템 삭제가 그 경우를 받아내고,
        /// 이전 export가 이 경로에 임포트되어 있었을 경우를 위해 Refresh를 붙인다.
        /// </summary>
        private static void DeleteRejectedRigExport()
        {
            string absolute = ToAbsoluteProjectPath(ExportedRigPath);
            if (!File.Exists(absolute) && !File.Exists(absolute + ".meta"))
            {
                return;
            }

            if (AssetDatabase.DeleteAsset(ExportedRigPath))
            {
                return;
            }

            try
            {
                if (File.Exists(absolute))
                {
                    File.Delete(absolute);
                }

                string metaPath = absolute + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Easy ZEPETO Helper: could not delete the rejected rig export at "
                    + ExportedRigPath + " (" + exception.Message + "). Delete it by hand - step 3 keeps "
                    + "reporting done while that file is there.");
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 내보낸 ZEPETO 리그에서 생성된 Avatar. 애니메이션 FBX들은 이것을 복사하려 시도하는데, 그래야 작성된
        /// 각도가 추측된 매핑이 아니라 ZEPETO 자신의 뼈 매핑을 통해 읽히기 때문이다.
        /// </summary>
        private static Avatar FindExportedRigAvatar()
        {
            if (!File.Exists(ToAbsoluteProjectPath(ExportedRigPath)))
            {
                return null;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ExportedRigPath);
            for (int i = 0; i < assets.Length; i++)
            {
                Avatar avatar = assets[i] as Avatar;
                if (avatar != null && avatar.isHuman)
                {
                    return avatar;
                }
            }

            return null;
        }

        private void DrawRigExportBody()
        {

            bool exporterInstalled = IsFbxExporterInstalled();
            bool alreadyExported = File.Exists(ToAbsoluteProjectPath(ExportedRigPath));
            Avatar avatar = FindExportedRigAvatar();

            DrawStatusRow("FBX Exporter 패키지", exporterInstalled ? "설치됨" : "없음");
            DrawStatusRow("내보낸 리그", alreadyExported ? ExportedRigPath : "아직 없음");
            DrawStatusRow("리타게팅 Avatar", avatar == null ? "없음" : avatar.name);

            if (!exporterInstalled)
            {
                DrawMiniHelp(
                    "Package Manager에서 FBX Exporter를 설치하면 ZEPETO 기본 몸을 FBX로 내보낼 수 있습니다. "
                    + "뼈 이름과 뼈대 구조가 실제 ZEPETO와 같아서 Blender에서 만든 동작이 그대로 붙습니다.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!exporterInstalled || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawBlueActionButton(
                        alreadyExported ? "ZEPETO 리그 다시 내보내기" : "ZEPETO 리그 내보내기",
                        exporterInstalled && !EditorApplication.isPlayingOrWillChangePlaymode,
                        GUILayout.Height(28f)))
                {
                    string message;
                    TryExportZepetoRigToFbx(out message);
                    statusMessage = message;
                    ValidateState();
                }
            }

            if (alreadyExported)
            {
                if (DrawSecondaryButton("리그 폴더 열기", GUILayout.Height(24f)))
                {
                    EditorUtility.RevealInFinder(ToAbsoluteProjectPath(ExportedRigPath));
                }
            }

            DrawMiniHelp(
                "내보낸 FBX를 Blender로 가져가서 애니메이션을 만들고, 다시 FBX로 내보내면 "
                + "5번에서 그대로 받습니다. 이때 이 리그의 Avatar를 기준으로 리타게팅합니다.\n\n"
                + "이건 모두가 공유하는 '기본 몸'입니다. 내 얼굴·머리·옷·체형은 Play 중에만 서버에서 내려받으므로 "
                + "여기에 들어있지 않습니다. 다만 내 몸으로 만들든 기본 몸으로 만들든 "
                + "모션 파일 자체는 완전히 같습니다 — Humanoid 클립에는 뼈 길이가 저장되지 않기 때문입니다. "
                + "내 캐릭터 모습은 5번에서 Play를 눌러 진짜 아바타로 확인하세요.",
                MessageType.None);

        }
    }
}
