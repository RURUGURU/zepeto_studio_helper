using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Easy.ZepetoHelper.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Exports the real ZEPETO base model to FBX through the helper and asserts the result is a rig Blender can
    /// actually open. This is the first half of the Unity -> Blender -> Unity round trip.
    ///
    /// It used to only dump facts and say nothing pass/fail, which meant a broken export read the same as a
    /// good one unless somebody eyeballed the numbers. The three assertions below are the ones that decide
    /// whether the round trip is possible at all, and they are written to ResultPath in the same
    /// PASS/FAIL/NOTE format as zepeto-helper-selftest.result.txt.
    /// </summary>
    public static class ZepetoRigExportRun
    {
        private const string TriggerPath = "zepeto-rig-export.trigger";
        private const string ReportPath = "zepeto-rig-export.report.txt";
        private const string ResultPath = "zepeto-rig-export.result.txt";
        private const string SkipPath = "zepeto-rig-export.skipped.txt";
        private const string RigPath = "Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx";
        private const string RunnerLabel = "ZEPETO rig export run";
        private const int Serial = 2;

        /// <summary>
        /// The 18 bytes every binary fbx starts with. Blender refuses ASCII fbx outright ("ASCII FBX files are
        /// not supported") while Unity reports a successful export either way, so this literal is the only local
        /// evidence that the exported rig can be opened in Blender at all - which is the entire point of the
        /// export path.
        /// </summary>
        private const string BinaryFbxMagic = "Kaydara FBX Binary";

        private static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly List<string> results = new List<string>();
        private static int passCount;
        private static int failCount;

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

            // Static state survives until the next domain reload, so a second trigger in the same session would
            // otherwise append to - and double-count - the first run's results.
            results.Clear();
            passCount = 0;
            failCount = 0;

            // [QC][Invariant:never_touch_an_unsaved_open_scene]
            // TryExportZepetoRigToFbx instantiates the ZEPETO base model into the ACTIVE scene and destroys it
            // again, so this runner mutates the scene the user has open exactly like its two siblings do - and it
            // was the only one without their refusal. Worse, the unsaved-changes flag that mutation sets is what
            // then made the self test and the custom motion run refuse for the rest of the session: triggering the
            // rig export poisoned the self-test trigger.
            if (ZepetoSelfTestSceneGuard.RefuseIfAnyOpenSceneIsDirty(
                    RunnerLabel,
                    "이 실행은 내보내기 과정에서 열린 씬에 임시 오브젝트를 만들고 지우기 때문에 씬을 건드립니다.",
                    SkipPath,
                    Append) != null)
            {
                Append("  Save or discard the scene yourself, then drop " + TriggerPath + " again.");
                // Returns before WriteResults on purpose: ResultPath has to keep meaning "the last real run", and
                // a pass=0 fail=0 tally there would read exactly like a clean sweep.
                return;
            }

            ZepetoSelfTestSceneGuard.ClearSkipRecord(SkipPath);

            // Baseline for the "nothing survived in the scene" test at the end of the run.
            int[] rootCountsBefore = CaptureSceneRootCounts();

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

            bool exported = false;
            string exportMessage = string.Empty;

            ScriptableObject helper = ScriptableObject.CreateInstance(helperType);
            try
            {
                object[] args = new object[] { null };
                exported = (bool)helperType.GetMethod("TryExportZepetoRigToFbx", Inst).Invoke(helper, args);
                exportMessage = args[0] as string;
                Append("export -> " + exported);
                Append("  message: " + exportMessage);
            }
            catch (Exception exception)
            {
                Append("EXCEPTION: " + exception);
                exportMessage = "EXCEPTION: " + exception.Message;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(helper);
            }

            Check("rig-export:export-succeeded", exported,
                "TryExportZepetoRigToFbx did not succeed :: " + exportMessage);

            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RigPath));
            bool fbxExists = File.Exists(absolute);
            Append("fbx exists: " + fbxExists
                + (fbxExists ? "  size=" + (new FileInfo(absolute).Length / 1024) + "KB" : string.Empty));

            // A missing file is a genuine FAILURE here, never "the export has not been run yet". The export path
            // deletes its own output when the binary verification fails, precisely so an unusable ASCII fbx
            // cannot sit in the project looking finished - so "no file" means the export ran and produced
            // something Blender could not have opened.
            Check("rig-export:fbx-exists", fbxExists,
                "no file at " + RigPath + ". Either the export never wrote one, or it wrote one and then deleted "
                + "it because the binary verification failed.");

            string header = fbxExists ? ReadFbxHeader(absolute) : "(no file)";
            Note("rig-export:header", header);
            Check("rig-export:binary-fbx", header == BinaryFbxMagic,
                "the first " + BinaryFbxMagic.Length + " bytes are '" + header + "', expected '" + BinaryFbxMagic
                + "'. Blender cannot read ASCII fbx and Unity reports a successful export either way, so this is "
                + "the check that decides whether the Blender half of the round trip is possible.");

            ModelImporter rigImporter = fbxExists ? AssetImporter.GetAtPath(RigPath) as ModelImporter : null;
            Check("rig-export:animation-type-human",
                rigImporter != null && rigImporter.animationType == ModelImporterAnimationType.Human,
                "animationType is "
                + (rigImporter == null ? "(no importer)" : rigImporter.animationType.ToString())
                + ", expected Human. Without it Unity generates no Avatar and nothing can retarget through the "
                + "exported rig.");

            if (fbxExists)
            {
                Append("animationType: " + (rigImporter == null ? "?" : rigImporter.animationType.ToString()));

                UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(RigPath);
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

                GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
                if (imported != null)
                {
                    Transform[] bones = imported.GetComponentsInChildren<Transform>(true);
                    Append("  transforms (bones+nodes): " + bones.Length);
                    SkinnedMeshRenderer[] skins = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Append("  skinned meshes: " + skins.Length);
                }
            }

            ReportSceneDirtAfterExport(rootCountsBefore);

            WriteResults();

            Append("--- done ---");
            Debug.Log("ZEPETO rig export run finished. pass=" + passCount + " fail=" + failCount);
        }

        /// <summary>
        /// Root GameObject count of every loaded scene, in scene order.
        ///
        /// The run's scene mutations are all temporary root objects - the base model the export instantiates and
        /// destroys in a finally block, and the stand-in body a helper window instance adds and removes with
        /// itself - so a count that comes back unchanged is the evidence that none of them survived. It is only
        /// compared after the helper instance is destroyed, which is when the second of those is gone.
        /// </summary>
        private static int[] CaptureSceneRootCounts()
        {
            int[] counts = new int[SceneManager.sceneCount];
            for (int i = 0; i < counts.Length; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                counts[i] = scene.isLoaded ? scene.rootCount : -1;
            }

            return counts;
        }

        /// <summary>
        /// Reports - and where possible undoes - what the export did to the open scene.
        ///
        /// The export leaves the scene marked as having unsaved changes even when it leaves no trace in it: both
        /// Instantiate and DestroyImmediate set that flag and neither clears it. The flag is not cosmetic, because
        /// every runner in this assembly refuses to start on a dirty scene, so a rig export would disable the self
        /// test and the custom motion run until the user saved or reverted a scene they never edited.
        ///
        /// So: prove nothing survived, then drop the flag without writing to disk. Never a save - this runner must
        /// not write the user's scene file - and never silence: whichever way it goes, it is a NOTE. A NOTE rather
        /// than a Check because the check names of these runners are compared against recorded result files.
        /// </summary>
        private static void ReportSceneDirtAfterExport(int[] rootCountsBefore)
        {
            string survivor = DescribeSurvivingSceneChange(rootCountsBefore);
            if (survivor != null)
            {
                Note("rig-export:scene-dirt", "the run left a change in the open scene (" + survivor
                    + "). It was NOT undone and the scene still reads as modified - revert it by hand. The self "
                    + "test and the custom motion run refuse to start until then.");
                return;
            }

            if (ZepetoSelfTestSceneGuard.FirstDirtyScenePath() == null)
            {
                Note("rig-export:scene-dirt", "none - the open scene is unchanged and still reads as saved");
                return;
            }

            string detail;
            if (ZepetoSelfTestSceneGuard.TryClearSceneDirtiness(out detail))
            {
                Note("rig-export:scene-dirt", "nothing survived in the scene, so the unsaved-changes flag the "
                    + "export set was dropped without writing anything to disk (" + detail + ")");
                return;
            }

            Note("rig-export:scene-dirt", "nothing survived in the scene, but the unsaved-changes flag could not "
                + "be dropped (" + detail + "). The open scene still reads as modified, so the self test and the "
                + "custom motion run will refuse until it is saved or reverted.");
        }

        /// <summary>
        /// What the run left behind in the open scenes, or null when nothing did.
        /// </summary>
        private static string DescribeSurvivingSceneChange(int[] rootCountsBefore)
        {
            if (rootCountsBefore == null || rootCountsBefore.Length != SceneManager.sceneCount)
            {
                return "the set of loaded scenes changed during the run";
            }

            for (int i = 0; i < rootCountsBefore.Length; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                int now = scene.isLoaded ? scene.rootCount : -1;
                if (now != rootCountsBefore[i])
                {
                    return (string.IsNullOrEmpty(scene.path) ? "untitled scene" : scene.path)
                        + " now has " + now + " root objects, had " + rootCountsBefore[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The first bytes of the file decoded as ASCII, or a short description of why they could not be read.
        /// Reads exactly BinaryFbxMagic.Length bytes so the value can be compared to it directly.
        /// </summary>
        private static string ReadFbxHeader(string absolutePath)
        {
            try
            {
                using (FileStream stream = File.OpenRead(absolutePath))
                {
                    byte[] header = new byte[BinaryFbxMagic.Length];
                    int read = stream.Read(header, 0, header.Length);
                    if (read < header.Length)
                    {
                        return "(only " + read + " bytes)";
                    }

                    return Encoding.ASCII.GetString(header);
                }
            }
            catch (Exception exception)
            {
                return "(unreadable: " + exception.Message + ")";
            }
        }

        /// <summary>
        /// Writes the pass/fail summary in the same format as zepeto-helper-selftest.result.txt, beside it, and
        /// mirrors the lines into the diagnostic report so the two files never disagree.
        /// </summary>
        private static void WriteResults()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("ZEPETO rig export check (serial " + Serial + ")");
            report.AppendLine("pass=" + passCount + " fail=" + failCount);
            report.AppendLine("----");
            for (int i = 0; i < results.Count; i++)
            {
                report.AppendLine(results[i]);
            }

            try { File.WriteAllText(ResultPath, report.ToString()); } catch { }

            Append("---- results ----");
            Append("pass=" + passCount + " fail=" + failCount);
            for (int i = 0; i < results.Count; i++)
            {
                Append(results[i]);
            }
        }

        private static void Check(string name, bool condition, string failDetail)
        {
            if (condition)
            {
                passCount++;
                results.Add("PASS " + name);
            }
            else
            {
                failCount++;
                results.Add("FAIL " + name + " :: " + failDetail);
            }
        }

        private static void Note(string name, string detail)
        {
            results.Add("NOTE " + name + " :: " + detail);
        }

        private static void Append(string line)
        {
            try { File.AppendAllText(ReportPath, line + Environment.NewLine); } catch { }
        }
    }
}
