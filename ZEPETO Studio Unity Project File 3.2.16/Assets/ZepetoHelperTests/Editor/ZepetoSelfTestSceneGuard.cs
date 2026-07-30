using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Everything the runners in this assembly need to know about unsaved open scenes.
    ///
    /// A runner that replaces, saves or instantiates into the scene the user has open has to refuse when that scene
    /// holds unsaved edits. That refusal used to be copy-pasted per runner, and ZepetoRigExportRun is what a third
    /// copy looks like when nobody writes it: no guard at all, while the dirt its export left behind made the two
    /// runners that DID have the guard refuse for the rest of the session. One implementation, one message shape,
    /// every call site.
    /// </summary>
    internal static class ZepetoSelfTestSceneGuard
    {
        /// <summary>
        /// EditorSceneManager.ClearSceneDirtiness(Scene) is internal, so it is reached by reflection - acceptable
        /// in an editor-only test assembly, and null here simply means the running editor does not offer it, which
        /// TryClearSceneDirtiness reports instead of hiding. There is no public equivalent: MarkSceneDirty has no
        /// counterpart, and the only public way to drop the flag is to write the file, which these runners must
        /// never do on the user's behalf.
        /// </summary>
        private static readonly MethodInfo ClearSceneDirtinessMethod = typeof(EditorSceneManager).GetMethod(
            "ClearSceneDirtiness",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(Scene) },
            null);

        /// <summary>
        /// Path of the first open scene holding unsaved edits, or null when every open scene is saved. Every
        /// loaded scene is checked, not just the active one, because a multi-scene setup can have the dirty one
        /// loaded additively.
        /// </summary>
        internal static string FirstDirtyScenePath()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    return string.IsNullOrEmpty(scene.path) ? "untitled scene" : scene.path;
                }
            }

            return null;
        }

        /// <summary>
        /// The one dirty-open-scene refusal. Returns the offending scene path when the caller must NOT run, or
        /// null when every open scene is saved and the caller may proceed.
        ///
        /// A refusal is reported three ways, because a skip that reads like a clean run is worse than the damage
        /// the guard prevents: through the caller's own report writer, as a console warning, and - when
        /// skipRecordPath is given - as a record whose FIRST line says SKIPPED. That record deliberately gets a
        /// path of its own. A runner's result file has to keep meaning "the last real run", so a refusal may never
        /// overwrite it: an empty pass=0 fail=0 tally is indistinguishable from a clean sweep and it destroys the
        /// tracked baseline on the way.
        ///
        /// Deliberately non-interactive, like every other path in these runners: no dialog, and no saving on the
        /// user's behalf - committing somebody's half-finished scene is its own kind of damage.
        /// </summary>
        /// <param name="runnerLabel">Which runner is refusing. Used in the console warning and the record.</param>
        /// <param name="consequence">Korean sentence: what this run would have done to that scene. Shown in both
        /// the console warning and the record.</param>
        /// <param name="skipRecordPath">Where the SKIPPED record goes, or null/empty to write none.</param>
        /// <param name="report">The caller's own report appender, or null when it has none.</param>
        internal static string RefuseIfAnyOpenSceneIsDirty(
            string runnerLabel,
            string consequence,
            string skipRecordPath,
            Action<string> report)
        {
            string dirtyScene = FirstDirtyScenePath();
            if (dirtyScene == null)
            {
                return null;
            }

            string headline = "SKIPPED " + runnerLabel + " :: an open scene has unsaved changes (" + dirtyScene + ")";

            if (report != null)
            {
                report(headline);
                report("  " + consequence);
            }

            WriteSkipRecord(skipRecordPath, headline, consequence);

            Debug.LogWarning(runnerLabel + ": 저장하지 않은 씬(" + dirtyScene + ")이 열려 있어서 실행하지 않았습니다. "
                + consequence + " 씬을 저장하거나 되돌린 뒤 다시 실행하세요.");

            return dirtyScene;
        }

        /// <summary>
        /// Writes the record of a run that did not happen: first line SKIPPED and why, then when, then the detail.
        /// Never the pass/fail shape of a result file, and never at a result file's path - the two must not be
        /// confusable, in either direction.
        /// </summary>
        internal static void WriteSkipRecord(string skipRecordPath, string headline, string detail)
        {
            if (string.IsNullOrEmpty(skipRecordPath))
            {
                return;
            }

            StringBuilder record = new StringBuilder();
            record.AppendLine(headline);
            // InvariantCulture because ':' is a culture-dependent specifier in a custom format string, and a
            // timestamp that changes shape with the editor's locale is a timestamp nothing can compare.
            record.AppendLine("at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            record.AppendLine("----");
            record.AppendLine(detail);
            record.AppendLine("Nothing ran, so no result file was written and the previous result file still "
                + "describes the last real run.");
            try { File.WriteAllText(skipRecordPath, record.ToString()); } catch { }
        }

        /// <summary>
        /// Removes the record a previous refusal left. Called at the top of a real run so a stale SKIPPED file
        /// sitting next to a fresh result file cannot make the last real run look like it was refused.
        /// </summary>
        internal static void ClearSkipRecord(string skipRecordPath)
        {
            if (string.IsNullOrEmpty(skipRecordPath))
            {
                return;
            }

            try
            {
                if (File.Exists(skipRecordPath))
                {
                    File.Delete(skipRecordPath);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Drops the unsaved-changes flag on every loaded scene WITHOUT writing anything to disk.
        ///
        /// Only legitimate for a caller that (1) refused to start unless every open scene was already saved and
        /// (2) has verified that nothing its run did to the scene survives. Under those two conditions the flag is
        /// simply untrue - memory and disk agree again - and leaving it set is not harmless: every runner here
        /// refuses on a dirty scene, so one run's leftover flag silently disables the others for the rest of the
        /// session.
        ///
        /// Fails soft and never quietly: `detail` explains the outcome either way, and false means "the scene is
        /// still marked dirty", which the caller is expected to report.
        /// </summary>
        internal static bool TryClearSceneDirtiness(out string detail)
        {
            if (ClearSceneDirtinessMethod == null)
            {
                detail = "EditorSceneManager.ClearSceneDirtiness is not available in this editor";
                return false;
            }

            int cleared = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                {
                    continue;
                }

                try
                {
                    ClearSceneDirtinessMethod.Invoke(null, new object[] { scene });
                }
                catch (Exception exception)
                {
                    detail = "ClearSceneDirtiness failed on " + DescribeScene(scene) + ": " + exception.Message;
                    return false;
                }

                // Re-read through the manager rather than trusting the call: Scene is a struct wrapping a native
                // handle, so isDirty is a fresh query and this is the only real confirmation available.
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    detail = "ClearSceneDirtiness left " + DescribeScene(scene) + " marked dirty";
                    return false;
                }

                cleared++;
            }

            detail = cleared + " scene(s) unmarked";
            return true;
        }

        private static string DescribeScene(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? "untitled scene" : scene.path;
        }
    }
}
