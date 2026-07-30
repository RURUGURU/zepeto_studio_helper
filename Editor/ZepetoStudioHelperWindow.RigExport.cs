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
        ///
        /// Returns null when the options type is unavailable. The caller then has to use the 2-argument
        /// ExportObject overload, which passes exportOptions: null - and that does NOT fall back to the
        /// project's Fbx Export settings, it builds a fresh ExportModelSettingsSerialize whose exportFormat
        /// field is ASCII (verified in com.unity.formats.fbx 5.1.6, ExportOptionsSettingsSerializeBase:251).
        /// So the fallback can only ever write ASCII, which is why TryExportZepetoRigToFbx deletes what it
        /// produced instead of keeping it.
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
            }

            // [AUDIT][Risk:Major][Scope:rig_export_done_means_openable]
            // Every failure leaves through this one exit instead of returning from inside the try, because a
            // reported failure and a thrown exception can both leave a partial fbx at ExportedRigPath just as
            // easily as an ASCII one - the exporter writes as it walks the hierarchy. Step 3's done test is a
            // bare File.Exists on that path (Flow.cs:172), so any file left behind turns the card green on a
            // broken export and takes step 4's "3번을 먼저 하세요" warning away. DeleteRejectedRigExport is a
            // no-op when nothing was written, so calling it on every failure costs nothing.
            if (!exportOk)
            {
                DeleteRejectedRigExport();
                return false;
            }

            // [AUDIT][Risk:Major][Scope:blender_roundtrip]
            // The magic-byte check decides, not the exporter's return value: ExportObject hands back a path for
            // an ASCII file exactly as happily as for a binary one. It runs BEFORE the importer work for two
            // reasons. Configuring animationType on a file that is about to be thrown away is a full
            // SaveAndReimport of a 1.4MB model for nothing, and it dirties the AssetDatabase on the way. And the
            // only thing that says "step 3 is done" is a bare File.Exists on ExportedRigPath (Flow.cs:172), so a
            // rejected file left on disk turns the card green and takes step 4's warning away - pointing the
            // user at a file Blender refuses to open.
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

            // Make the exported rig Humanoid so it produces the Avatar every animation will retarget through.
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
        /// Removes an export that must not be left behind, .meta included. Called from every failing exit of
        /// TryExportZepetoRigToFbx - the binary check, the exporter reporting failure, and the exception path -
        /// because all three can leave a partial or unopenable file at ExportedRigPath.
        ///
        /// [QC][Invariant:rig_export_done_means_openable]
        /// Nothing tracks "was the export usable" - step 3's done test and step 4's gate are both a bare
        /// File.Exists on ExportedRigPath (Flow.cs:172, GoToBlender.cs:174). So a rejected file may not be left
        /// behind: it would turn the card green, hide step 4's "3번을 먼저 하세요" warning, and name a file
        /// Blender cannot open. The .meta goes with it, otherwise the next attempt inherits the dead file's
        /// importer settings and Unity logs an orphaned-meta warning.
        ///
        /// Nothing on disk means nothing to do, and that exit comes first so a failure that never produced a
        /// file does not pay for an AssetDatabase.Refresh.
        ///
        /// DeleteAsset is tried before the filesystem because it handles the .meta and the AssetDatabase entry
        /// together, but it returns false for a path the AssetDatabase has never imported - which is the usual
        /// case here, since the verification runs before ImportAsset. The filesystem delete covers that, with a
        /// Refresh in case an earlier export had been imported at this path.
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
