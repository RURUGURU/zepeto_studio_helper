using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Exporting ZEPETO's shared base body to FBX so motions can be authored on the real bone names and
    /// hierarchy, and importing animations back with that model's Avatar as the retarget source.
    ///
    /// What this does NOT give you is your own avatar. ZepetoBaseModel is the body everyone starts from; a
    /// specific user's face, outfit and body-shape scales only exist after a runtime download during Play.
    /// It also does not make retargeting exact - a Humanoid clip has no room to store bone lengths, so
    /// proportion error surfaces at playback (foot slide, twist collapse), not here. Authoring on the real
    /// hierarchy still matters: it gets the bone names, the rest pose and the twist-bone layout right.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private const string ZepetoBaseModelPath = "Packages/zepeto.character/resources/zepeto/ZepetoBaseModel.prefab";
        private const string RigExportRoot = "Assets/ZepetoHelper/Rig";
        private const string ExportedRigPath = RigExportRoot + "/ZepetoBaseModel.fbx";
        private const string FbxExporterTypeName = "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor";

        /// <summary>
        /// The Unity FBX Exporter is an optional package, so it is reached by reflection. That keeps this helper
        /// compiling in a project that never installs it.
        /// </summary>
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
        /// Builds export options that force a BINARY fbx.
        ///
        /// [AUDIT][Risk:Major][Scope:blender_roundtrip]
        /// ExportModelOptions defaults to ExportFormat.ASCII, and Blender refuses ASCII fbx outright
        /// ("ASCII FBX files are not supported"). Unity reports a successful export either way, so this only
        /// shows up when the file is actually opened in Blender.
        /// Returns null when the options type is unavailable, in which case the caller falls back.
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

            // Export a temporary instance: exporting the prefab asset directly loses the pose/hierarchy the
            // exporter walks, and we must not touch the package asset.
            GameObject instance = UnityEngine.Object.Instantiate(baseModel);
            instance.name = "ZepetoBaseModel";

            try
            {
                string absolute = ToAbsoluteProjectPath(ExportedRigPath);

                object binaryOptions = BuildBinaryExportOptions();
                MethodInfo withOptions = binaryOptions == null
                    ? null
                    : FindFbxExportMethodWithOptions(binaryOptions.GetType());

                object result = withOptions != null
                    ? withOptions.Invoke(null, new object[] { absolute, instance, binaryOptions })
                    : exportObject.Invoke(null, new object[] { absolute, instance });

                string written = result as string;

                if (string.IsNullOrEmpty(written) || !File.Exists(absolute))
                {
                    message = "FBX 내보내기가 실패했습니다. Console을 확인하세요.";
                    return false;
                }

                if (withOptions == null)
                {
                    Debug.LogWarning("Easy ZEPETO Helper: binary fbx options were unavailable, the exported rig may be "
                        + "ASCII and Blender cannot open ASCII fbx.");
                }
            }
            catch (Exception exception)
            {
                message = "FBX 내보내기 중 오류: " + exception.Message;
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.ImportAsset(ExportedRigPath, ImportAssetOptions.ForceUpdate);

            // Make the exported rig Humanoid so it produces the Avatar every animation will retarget through.
            ModelImporter importer = AssetImporter.GetAtPath(ExportedRigPath) as ModelImporter;
            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
            }

            if (!IsBinaryFbx(ToAbsoluteProjectPath(ExportedRigPath)))
            {
                message = "내보낸 FBX가 ASCII 형식입니다. Blender가 열지 못합니다. "
                    + "Edit > Project Settings > Fbx Export에서 Export Format을 Binary로 바꾸고 다시 시도하세요.";
                return false;
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
        /// A binary fbx starts with the literal "Kaydara FBX Binary". Anything else is ASCII, which Blender
        /// rejects on import even though Unity exported it happily.
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
        /// The Avatar generated from the exported ZEPETO rig. Animation FBXs try to copy from this so the
        /// authored angles are read back through ZEPETO's own bone mapping rather than a guessed one.
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
