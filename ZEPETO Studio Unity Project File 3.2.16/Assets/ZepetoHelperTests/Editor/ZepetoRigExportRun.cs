using System;
using System.IO;
using System.Reflection;
using Easy.ZepetoHelper.Editor;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Exports the real ZEPETO base model to FBX through the helper and reports whether the result is a usable
    /// Humanoid rig for Blender. This is the first half of the Unity -> Blender -> Unity round trip.
    /// </summary>
    public static class ZepetoRigExportRun
    {
        private const string TriggerPath = "zepeto-rig-export.trigger";
        private const string ReportPath = "zepeto-rig-export.report.txt";
        private const int Serial = 1;

        private static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [InitializeOnLoadMethod]
        private static void OnLoad() { EditorApplication.delayCall += Bootstrap; }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnReload() { EditorApplication.delayCall += Bootstrap; }

        private static void Bootstrap()
        {
            if (!File.Exists(TriggerPath)) { return; }
            // Exporting writes assets and is refused during Play; keep the trigger until edit mode returns.
            if (EditorApplication.isPlayingOrWillChangePlaymode) { return; }
            File.Delete(TriggerPath);
            Run();
        }

        private static void Run()
        {
            File.WriteAllText(ReportPath, "ZEPETO rig export (serial " + Serial + ")\n----\n");

            Type helperType = typeof(ZepetoStudioHelperWindow);

            MethodInfo isInstalled = helperType.GetMethod("IsFbxExporterInstalled", Stat);
            Append("FBX Exporter installed: " + (isInstalled == null ? "?" : isInstalled.Invoke(null, null).ToString()));

            GameObject baseModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/zepeto.character/resources/zepeto/ZepetoBaseModel.prefab");
            Append("base model found: " + (baseModel != null));
            if (baseModel != null)
            {
                SkinnedMeshRenderer[] skins = baseModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Animator animator = baseModel.GetComponentInChildren<Animator>();
                Append("  skinned meshes: " + skins.Length
                    + "  animator: " + (animator != null)
                    + "  avatar: " + (animator != null && animator.avatar != null ? animator.avatar.name + " isHuman=" + animator.avatar.isHuman : "none"));
            }

            ScriptableObject helper = ScriptableObject.CreateInstance(helperType);
            try
            {
                object[] args = new object[] { null };
                bool ok = (bool)helperType.GetMethod("TryExportZepetoRigToFbx", Inst).Invoke(helper, args);
                Append("export -> " + ok);
                Append("  message: " + args[0]);
            }
            catch (Exception exception)
            {
                Append("EXCEPTION: " + exception);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(helper);
            }

            const string rigPath = "Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx";
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", rigPath));
            Append("fbx exists: " + File.Exists(absolute)
                + (File.Exists(absolute) ? "  size=" + (new FileInfo(absolute).Length / 1024) + "KB" : string.Empty));

            if (File.Exists(absolute))
            {
                ModelImporter importer = AssetImporter.GetAtPath(rigPath) as ModelImporter;
                Append("animationType: " + (importer == null ? "?" : importer.animationType.ToString()));

                UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(rigPath);
                int meshes = 0, avatars = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Mesh) { meshes++; }
                    Avatar av = all[i] as Avatar;
                    if (av != null)
                    {
                        avatars++;
                        Append("  avatar '" + av.name + "' isValid=" + av.isValid + " isHuman=" + av.isHuman);
                    }
                }
                Append("  meshes in fbx: " + meshes + "  avatars: " + avatars);

                GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
                if (imported != null)
                {
                    Transform[] bones = imported.GetComponentsInChildren<Transform>(true);
                    Append("  transforms (bones+nodes): " + bones.Length);
                    SkinnedMeshRenderer[] skins = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Append("  skinned meshes: " + skins.Length);
                }
            }

            Append("--- done ---");
            Debug.Log("ZEPETO rig export run finished");
        }

        private static void Append(string line)
        {
            try { File.AppendAllText(ReportPath, line + Environment.NewLine); } catch { }
        }
    }
}
