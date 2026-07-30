using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Easy.ZepetoHelper.Editor;
using Easy.ZepetoHelper.SelfTest;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Runs against the real helper window in the running editor because batch mode cannot be licensed here.
    /// Drop a trigger file in Temp/ and the suite runs on the next script reload, writing a plain-text report.
    /// </summary>
    public static class ZepetoHelperSelfTest
    {
        // Unity clears Temp/ on startup, so the handshake files live in the project root instead.
        private const string TriggerPath = "zepeto-helper-selftest.trigger";
        private const string ResultPath = "zepeto-helper-selftest.result.txt";
        private const string TestSceneFolder = "Assets/ZepetoHelperTests";
        private const string TestScenePath = TestSceneFolder + "/SelfTestLoaderScene.unity";

        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly List<string> results = new List<string>();
        private static int passCount;
        private static int failCount;

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        // Editor startup does not always route through DidReloadScripts, so the trigger is polled here too.
        // The trigger file is consumed on the first run, which keeps the suite from executing twice.
        [InitializeOnLoadMethod]
        private static void OnEditorLoaded()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        private static void RunIfRequested()
        {
            if (!File.Exists(TriggerPath))
            {
                return;
            }

            // The suite opens and creates scenes, which Unity forbids during Play:
            //   InvalidOperationException: This cannot be used during play mode
            // A recompile while Play is running fires this hook, so without the guard an armed trigger blows up
            // mid-session and the thrown exception leaves the helper's OnGUI layout broken - buttons vanish.
            // Keep the trigger and run once Play is over, so nothing is silently skipped either.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!waitingForPlayToEnd)
                {
                    waitingForPlayToEnd = true;
                    EditorApplication.playModeStateChanged += RunAfterPlayEnds;
                    Debug.Log("ZEPETO Helper self test: Play 중이라 대기합니다. Stop을 누르면 자동으로 실행됩니다.");
                }

                return;
            }

            File.Delete(TriggerPath);
            Run();
        }

        private static bool waitingForPlayToEnd;

        private static void RunAfterPlayEnds(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= RunAfterPlayEnds;
            waitingForPlayToEnd = false;
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Window/Easy/Run ZEPETO Helper Self Test")]
        public static void Run()
        {
            // Second line of defence: the menu item can be picked during Play too, and the scene APIs below
            // would throw the same InvalidOperationException.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("ZEPETO Helper self test는 Play 중에 실행할 수 없습니다 "
                    + "(씬을 열고 만드는 검사가 있습니다). Stop을 누른 뒤 다시 실행하세요.");
                return;
            }

            results.Clear();
            passCount = 0;
            failCount = 0;

            try
            {
                TestNoHardcodedAccount();
                TestMcpCodeRemoved();
                TestVersionComparison();
                TestSdkDetection();
                TestIdSanitizeAndValidate();
                TestSdkLoaderShape();
                TestMultiAccountApplyOnRealWindow();
                TestWorkSceneDiscovery();
                TestSplitComponentBinding();
                TestRealTemplateScene();
            }
            catch (Exception exception)
            {
                Fail("harness", exception.ToString());
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("ZEPETO Helper self test");
            report.AppendLine("pass=" + passCount + " fail=" + failCount);
            report.AppendLine("----");
            for (int i = 0; i < results.Count; i++)
            {
                report.AppendLine(results[i]);
            }

            File.WriteAllText(ResultPath, report.ToString());
            Debug.Log("ZEPETO Helper self test finished. pass=" + passCount + " fail=" + failCount);

            // Leave the editor on the real work scene with the helper open, so the result is visible on screen
            // rather than only in a text file.
            if (File.Exists("Assets/Playground.unity"))
            {
                EditorSceneManager.OpenScene("Assets/Playground.unity", OpenSceneMode.Single);
                ZepetoStudioHelperWindow.Open();
            }
        }

        // ---------- checks ----------

        private static void TestNoHardcodedAccount()
        {
            Type type = typeof(ZepetoStudioHelperWindow);
            FieldInfo legacyConst = type.GetField("BuiltInDefaultZepetoId", AnyStatic);
            Check("no-builtin-id-const", legacyConst == null, "BuiltInDefaultZepetoId still exists");

            string sourcePath = "Packages/com.easy.zepeto-helper/Editor/ZepetoStudioHelperWindow.cs";
            if (File.Exists(sourcePath))
            {
                string source = File.ReadAllText(sourcePath);
                Check("no-personal-id-in-source", source.IndexOf("darbams77", StringComparison.OrdinalIgnoreCase) < 0,
                    "shipped source still contains a personal ZEPETO id");
            }
            else
            {
                Fail("no-personal-id-in-source", "could not read " + sourcePath);
            }
        }

        private static void TestMcpCodeRemoved()
        {
            Type type = typeof(ZepetoStudioHelperWindow);
            string[] gone = { "GetUnityMcpBridgePort", "CanPingUnityMcpBridge", "TryRestartUnityMcpBridge", "BuildMcpStatusText", "AutoRecoverUnityMcpBridgeOnLoad" };
            for (int i = 0; i < gone.Length; i++)
            {
                MethodInfo method = type.GetMethod(gone[i], AnyStatic) ?? type.GetMethod(gone[i], AnyInstance);
                Check("mcp-removed:" + gone[i], method == null, gone[i] + " still exists");
            }
        }

        private static void TestVersionComparison()
        {
            MethodInfo compare = typeof(ZepetoStudioHelperWindow).GetMethod("CompareVersions", AnyStatic);
            if (compare == null)
            {
                Fail("version-compare", "CompareVersions not found");
                return;
            }

            Check("version-compare:newer", (int)compare.Invoke(null, new object[] { "3.2.16", "3.2.12" }) > 0, "3.2.16 should rank above 3.2.12");
            Check("version-compare:older", (int)compare.Invoke(null, new object[] { "3.2.9", "3.2.12" }) < 0, "3.2.9 should rank below 3.2.12");
            Check("version-compare:equal", (int)compare.Invoke(null, new object[] { "3.2.12", "3.2.12" }) == 0, "3.2.12 should equal itself");
            Check("version-compare:suffix", (int)compare.Invoke(null, new object[] { "3.2.12-preview.1", "3.2.12" }) == 0, "prerelease suffix should not break parsing");
        }

        private static void TestSdkDetection()
        {
            MethodInfo installed = typeof(ZepetoStudioHelperWindow).GetMethod("IsRequiredZepetoStudioPackageInstalled", AnyStatic);
            if (installed == null)
            {
                Fail("sdk-detect", "IsRequiredZepetoStudioPackageInstalled not found");
                return;
            }

            object[] args = new object[] { null };
            bool ok = (bool)installed.Invoke(null, args);
            string detected = args[0] as string;
            Check("sdk-detect:installed", ok, "zepeto.studio was not detected as installed (detected '" + detected + "')");
            Note("sdk-detect:version", "detected zepeto.studio " + detected);

            // The old build hard-required exactly 3.2.12. Anything at or above the minimum must now pass,
            // which is what the project's own upgrade to 3.2.16 exercises.
            MethodInfo compare = typeof(ZepetoStudioHelperWindow).GetMethod("CompareVersions", AnyStatic);
            FieldInfo minField = typeof(ZepetoStudioHelperWindow).GetField("MinimumPackageVersion", AnyStatic);
            string minimum = minField == null ? "3.2.12" : (string)minField.GetValue(null);
            bool atLeastMinimum = compare != null
                && (int)compare.Invoke(null, new object[] { detected, minimum }) >= 0;
            Check("sdk-detect:at-least-minimum", atLeastMinimum,
                "detected '" + detected + "' did not compare >= minimum '" + minimum + "'");

            MethodInfo tryGet = typeof(ZepetoStudioHelperWindow).GetMethod("TryGetZepetoStudioPackage", AnyStatic);
            if (tryGet != null)
            {
                object[] pkgArgs = new object[] { null, null };
                bool found = (bool)tryGet.Invoke(null, pkgArgs);
                Check("sdk-detect:source", found && !string.IsNullOrEmpty(pkgArgs[1] as string),
                    "install source was not reported");
                Note("sdk-detect:source-value", "source = " + (pkgArgs[1] as string));
            }
        }

        private static void TestIdSanitizeAndValidate()
        {
            MethodInfo sanitize = typeof(ZepetoStudioHelperWindow).GetMethod("SanitizeZepetoId", AnyStatic);
            MethodInfo formatError = typeof(ZepetoStudioHelperWindow).GetMethod("GetZepetoIdFormatError", AnyStatic);
            if (sanitize == null || formatError == null)
            {
                Fail("id-validate", "SanitizeZepetoId/GetZepetoIdFormatError not found");
                return;
            }

            Check("id-sanitize:at", "sery_2750".Equals(sanitize.Invoke(null, new object[] { "@sery_2750" })), "leading @ was not stripped");
            Check("id-sanitize:spaces", "sery_2750".Equals(sanitize.Invoke(null, new object[] { "  sery_2750  " })), "surrounding spaces were not stripped");
            Check("id-sanitize:inner", "sery2750".Equals(sanitize.Invoke(null, new object[] { "sery 2750" })), "inner whitespace was not stripped");

            Check("id-valid:sery_2750", string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "sery_2750" })), "sery_2750 should be accepted");
            Check("id-valid:darbams77", string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "darbams77" })), "darbams77 should be accepted");
            Check("id-valid:dotted", string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "my.zepeto-01" })), "dots and dashes should be accepted");
            Check("id-invalid:empty", !string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "" })), "empty id should be rejected");
            Check("id-invalid:symbol", !string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "bad!id" })), "'!' should be rejected");
            Check("id-invalid:url", !string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { "https://zepeto.me/x" })), "a pasted URL should be rejected");
        }

        private static void TestSdkLoaderShape()
        {
            Type sdkLoader = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && sdkLoader == null; i++)
            {
                try
                {
                    Type[] types = assemblies[i].GetTypes();
                    for (int t = 0; t < types.Length; t++)
                    {
                        if (types[t].Name == "ZepetoStudioLoader")
                        {
                            sdkLoader = types[t];
                            break;
                        }
                    }
                }
                catch
                {
                    // Reflection-only or partially loaded assemblies are not interesting here.
                }
            }

            if (sdkLoader == null)
            {
                Note("sdk-loader-shape", "ZepetoStudioLoader type not found in loaded assemblies");
            }
            else
            {
                Note("sdk-loader-shape", sdkLoader.FullName + " fields = [" + DescribeFields(sdkLoader) + "]");
            }

            // The helper binds by serialized field name across every component on LOADER, so the decisive question
            // is which ZEPETO type - if any - actually declares zepetoId / AnimationClip / AnimatorController.
            string[] wanted = { "zepetoId", "AnimationClip", "AnimatorController" };
            for (int w = 0; w < wanted.Length; w++)
            {
                List<string> owners = FindMonoBehaviourTypesWithField(wanted[w]);
                Note("field-owner:" + wanted[w],
                    owners.Count == 0 ? "no MonoBehaviour declares this field" : string.Join(", ", owners.ToArray()));
            }

            Type customLoader = FindTypeByName("ZepetoCharacterCustomLoader");
            Note("sdk-customloader-shape",
                customLoader == null ? "not found" : customLoader.FullName + " fields = [" + DescribeFields(customLoader) + "]");

            Type playgroundController = FindTypeByName("PlaygroundController");
            Note("sdk-playgroundcontroller-shape",
                playgroundController == null ? "not found" : playgroundController.FullName + " fields = [" + DescribeFields(playgroundController) + "]");
        }

        private static string DescribeFields(Type type)
        {
            List<string> names = new List<string>();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                names.Add(fields[i].Name + ":" + fields[i].FieldType.Name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static Type FindTypeByName(string simpleName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type[] types = assemblies[i].GetTypes();
                    for (int t = 0; t < types.Length; t++)
                    {
                        if (types[t].Name == simpleName)
                        {
                            return types[t];
                        }
                    }
                }
                catch
                {
                    // Ignore assemblies that cannot be reflected over.
                }
            }

            return null;
        }

        private static List<string> FindMonoBehaviourTypesWithField(string fieldName)
        {
            List<string> owners = new List<string>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                string assemblyName = assemblies[i].GetName().Name;
                if (assemblyName.IndexOf("ZEPETO", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    Type[] types = assemblies[i].GetTypes();
                    for (int t = 0; t < types.Length; t++)
                    {
                        if (!typeof(MonoBehaviour).IsAssignableFrom(types[t]))
                        {
                            continue;
                        }

                        FieldInfo field = types[t].GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            owners.Add(assemblyName + "/" + types[t].Name + " (" + field.FieldType.Name + ")");
                        }
                    }
                }
                catch
                {
                    // Ignore assemblies that cannot be reflected over.
                }
            }

            return owners;
        }

        private static void TestMultiAccountApplyOnRealWindow()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            GameObject loaderObject = new GameObject("LOADER");
            ZepetoHelperTestLoader loaderComponent = loaderObject.AddComponent<ZepetoHelperTestLoader>();
            loaderComponent.zepetoId = string.Empty;

            if (!AssetDatabase.IsValidFolder(TestSceneFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ZepetoHelperTests");
            }

            EditorSceneManager.SaveScene(scene, TestScenePath);

            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                MethodInfo findLoader = type.GetMethod("FindLoaderAndSerializedFields", AnyInstance);
                MethodInfo applyId = type.GetMethod("ApplyZepetoId", AnyInstance);
                if (findLoader == null || applyId == null)
                {
                    Fail("multi-account", "expected helper members were not found");
                    return;
                }

                // The saved-id list was removed in 0.7.0: ids are typed in and the scene's LOADER is the only
                // place one is kept. Assert the members are really gone, so a revert cannot quietly bring back
                // an EditorPrefs-backed account that outlives the project.
                Check("saved-ids:removed",
                    type.GetField("savedZepetoIds", AnyInstance) == null
                        && type.GetMethod("RegisterZepetoId", AnyInstance) == null
                        && type.GetMethod("SetActiveZepetoId", AnyInstance) == null,
                    "the saved-id feature is still present");

                findLoader.Invoke(window, null);

                FieldInfo loaderField = type.GetField("loader", AnyInstance);
                Check("loader-bound", loaderField != null && (loaderField.GetValue(window) as GameObject) != null,
                    "helper did not bind to the LOADER GameObject");

                // Account 1
                applyId.Invoke(window, new object[] { "darbams77" });
                Check("apply-id:first", loaderComponent.zepetoId == "darbams77",
                    "LOADER zepetoId was '" + loaderComponent.zepetoId + "', expected darbams77");

                // Account 2 - the second account must behave identically, not just the first one.
                applyId.Invoke(window, new object[] { "sery_2750" });
                Check("apply-id:second", loaderComponent.zepetoId == "sery_2750",
                    "LOADER zepetoId was '" + loaderComponent.zepetoId + "', expected sery_2750");

                // Account 2 typed with an @ prefix, as copied from the ZEPETO app.
                applyId.Invoke(window, new object[] { "@sery_2750" });
                Check("apply-id:at-prefix", loaderComponent.zepetoId == "sery_2750",
                    "'@sery_2750' did not normalise to sery_2750, got '" + loaderComponent.zepetoId + "'");

                // Switch back - round tripping between accounts must be lossless.
                applyId.Invoke(window, new object[] { "darbams77" });
                Check("apply-id:switch-back", loaderComponent.zepetoId == "darbams77",
                    "switching back failed, got '" + loaderComponent.zepetoId + "'");

                // A malformed id must be refused without corrupting the scene value.
                applyId.Invoke(window, new object[] { "bad id!" });
                Check("apply-id:reject-invalid", loaderComponent.zepetoId == "darbams77",
                    "invalid id overwrote the LOADER value, got '" + loaderComponent.zepetoId + "'");

                // A fresh window must read the id back out of the scene, not out of EditorPrefs.
                ZepetoStudioHelperWindow second = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
                try
                {
                    Type secondType = typeof(ZepetoStudioHelperWindow);
                    secondType.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(second, null);
                    secondType.GetMethod("LoadZepetoIdSettings", AnyInstance).Invoke(second, null);

                    FieldInfo textField = secondType.GetField("zepetoIdText", AnyInstance);
                    string seeded = textField == null ? null : textField.GetValue(second) as string;
                    Check("id-from-scene", seeded == "darbams77",
                        "a new window seeded the id field with '" + (seeded ?? "null") + "', expected the scene value darbams77");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(second);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static void TestWorkSceneDiscovery()
        {
            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                MethodInfo refresh = type.GetMethod("RefreshWorkSceneCandidates", AnyInstance);
                FieldInfo guidsField = type.GetField("workSceneGuids", AnyInstance);
                if (refresh == null || guidsField == null)
                {
                    Fail("scene-discovery", "RefreshWorkSceneCandidates/workSceneGuids not found");
                    return;
                }

                refresh.Invoke(window, null);
                string[] guids = guidsField.GetValue(window) as string[];

                bool foundTestScene = false;
                if (guids != null)
                {
                    for (int i = 0; i < guids.Length; i++)
                    {
                        if (AssetDatabase.GUIDToAssetPath(guids[i]) == TestScenePath)
                        {
                            foundTestScene = true;
                            break;
                        }
                    }
                }

                Check("scene-discovery:finds-loader-scene", foundTestScene,
                    "the scene containing LOADER was not discovered (found " + (guids == null ? 0 : guids.Length) + " candidates)");

                // Discovery must be content based, not name based: the old build only ever looked at a hardcoded
                // Assets/Playground.unity. Finding a LOADER scene that is NOT called Playground proves the change.
                bool foundNonPlaygroundScene = false;
                if (guids != null)
                {
                    for (int i = 0; i < guids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                        if (!string.IsNullOrEmpty(path)
                            && Path.GetFileNameWithoutExtension(path) != "Playground")
                        {
                            foundNonPlaygroundScene = true;
                            break;
                        }
                    }
                }

                Check("scene-discovery:not-name-based", foundNonPlaygroundScene,
                    "no LOADER scene outside the hardcoded Playground name was discovered");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// The SDK splits zepetoId and AnimationClip/AnimatorController across two components, and the template is
        /// free to put them on different GameObjects. Binding must still resolve all three.
        /// </summary>
        private static void TestSplitComponentBinding()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject loaderObject = new GameObject("LOADER");
            ZepetoHelperTestIdOwner idOwner = loaderObject.AddComponent<ZepetoHelperTestIdOwner>();
            idOwner.zepetoId = string.Empty;

            GameObject child = new GameObject("PlaygroundControllerHost");
            child.transform.SetParent(loaderObject.transform);
            child.AddComponent<ZepetoHelperTestClipOwner>();

            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                type.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(window, null);

                object idProp = type.GetField("zepetoIdProperty", AnyInstance).GetValue(window);
                object clipProp = type.GetField("animationClipProperty", AnyInstance).GetValue(window);
                object ctrlProp = type.GetField("animatorControllerProperty", AnyInstance).GetValue(window);

                Check("split-binding:zepetoId", idProp != null, "zepetoId on LOADER itself was not bound");
                Check("split-binding:AnimationClip", clipProp != null, "AnimationClip on a child object was not bound");
                Check("split-binding:AnimatorController", ctrlProp != null, "AnimatorController on a child object was not bound");

                // Applying an id must still work when the id field is on a different component than the clip fields.
                type.GetMethod("ApplyZepetoId", AnyInstance).Invoke(window, new object[] { "sery_2750" });
                Check("split-binding:apply-id", idOwner.zepetoId == "sery_2750",
                    "id apply failed on a split layout, got '" + idOwner.zepetoId + "'");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            // A sibling layout (components on a separate root object) must resolve through the scene-wide pass.
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            GameObject siblingLoader = new GameObject("LOADER");
            siblingLoader.AddComponent<ZepetoHelperTestIdOwner>();
            GameObject elsewhere = new GameObject("SomeOtherRoot");
            elsewhere.AddComponent<ZepetoHelperTestClipOwner>();

            ZepetoStudioHelperWindow siblingWindow = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                type.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(siblingWindow, null);
                object clipProp = type.GetField("animationClipProperty", AnyInstance).GetValue(siblingWindow);
                Check("split-binding:sibling-object", clipProp != null,
                    "AnimationClip on a sibling root object was not bound");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(siblingWindow);
            }
        }

        /// <summary>
        /// End-to-end check against the official ZEPETO Studio template scene, when it is present in the project.
        /// This is the only check that proves the helper binds to the real SDK components rather than a stand-in.
        /// Runs last so the template scene is what stays open in the editor.
        /// </summary>
        private static void TestRealTemplateScene()
        {
            string templateScene = "Assets/Playground.unity";
            if (!File.Exists(templateScene))
            {
                Note("real-template", "Assets/Playground.unity not present, skipped");
                return;
            }

            EditorSceneManager.OpenScene(templateScene, OpenSceneMode.Single);

            GameObject loaderObject = GameObject.Find("LOADER");
            if (loaderObject == null)
            {
                Fail("real-template:loader", "LOADER was not found in the official template scene");
                return;
            }

            // Dump every serialized property the SDK actually exposes, so the binding names can be verified
            // against reality instead of assumed.
            Component[] components = loaderObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                SerializedObject so = new SerializedObject(components[i]);
                List<string> names = new List<string>();
                SerializedProperty iterator = so.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    names.Add(iterator.name + ":" + iterator.propertyType);
                }

                Note("real-template:component", components[i].GetType().FullName + " -> [" + string.Join(", ", names.ToArray()) + "]");
            }

            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                type.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(window, null);

                SerializedProperty idProp = type.GetField("zepetoIdProperty", AnyInstance).GetValue(window) as SerializedProperty;
                SerializedProperty clipProp = type.GetField("animationClipProperty", AnyInstance).GetValue(window) as SerializedProperty;
                SerializedProperty ctrlProp = type.GetField("animatorControllerProperty", AnyInstance).GetValue(window) as SerializedProperty;

                Check("real-template:bind-AnimationClip", clipProp != null, "AnimationClip did not bind on the official template");
                Check("real-template:bind-AnimatorController", ctrlProp != null, "AnimatorController did not bind on the official template");
                Check("real-template:bind-zepetoId", idProp != null, "zepetoId did not bind on the official template");

                if (idProp == null)
                {
                    return;
                }

                MethodInfo applyId = type.GetMethod("ApplyZepetoId", AnyInstance);
                MethodInfo currentId = type.GetMethod("GetCurrentZepetoId", AnyInstance);

                applyId.Invoke(window, new object[] { "darbams77" });
                Check("real-template:apply-first", "darbams77".Equals(currentId.Invoke(window, null)),
                    "first account did not stick, got '" + currentId.Invoke(window, null) + "'");

                applyId.Invoke(window, new object[] { "sery_2750" });
                Check("real-template:apply-second", "sery_2750".Equals(currentId.Invoke(window, null)),
                    "second account did not stick, got '" + currentId.Invoke(window, null) + "'");

                applyId.Invoke(window, new object[] { "darbams77" });
                Check("real-template:switch-back", "darbams77".Equals(currentId.Invoke(window, null)),
                    "switching back failed, got '" + currentId.Invoke(window, null) + "'");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            // Outfit prefab discovery on the real Contents folder.
            string[] prefabGuids = AssetDatabase.IsValidFolder("Assets/Contents")
                ? AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Contents" })
                : new string[0];
            Check("real-template:outfit-prefab", prefabGuids.Length > 0,
                "no outfit prefab found under Assets/Contents");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                Note("real-template:outfit", AssetDatabase.GUIDToAssetPath(prefabGuids[i]));
            }

            // The SDK motion list the helper offers in step 2.
            string animFolder = "Packages/zepeto.studio/resources/Animation";
            int clipCount = AssetDatabase.IsValidFolder(animFolder)
                ? AssetDatabase.FindAssets("t:AnimationClip", new[] { animFolder }).Length
                : 0;
            Check("real-template:sdk-motions", clipCount > 0, "no SDK motion clips were found");
            Note("real-template:sdk-motion-count", clipCount + " clips");

            TestMotionReachesPlaybackSlot();
            TestMotionCatalog();
            TestLiveReloadPlumbing();
        }

        /// <summary>
        /// The catalog must merge SDK and custom motions and, above all, tell a real motion apart from the
        /// single-frame poses the SDK also ships - that confusion is what made the avatar look broken.
        /// </summary>
        private static void TestMotionCatalog()
        {
            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                type.GetMethod("LoadPackageAnimations", AnyInstance).Invoke(window, null);

                object entriesObj = type.GetField("motionEntries", AnyInstance).GetValue(window);
                System.Collections.IList entries = entriesObj as System.Collections.IList;
                Check("catalog:populated", entries != null && entries.Count > 0, "motion catalog is empty");
                Note("catalog:count", (entries == null ? 0 : entries.Count) + " motions");

                // Every SDK clip is Humanoid; that is the premise the whole Blender/Mixamo route relies on.
                AnimationClip pose = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Packages/zepeto.studio/resources/Animation/A_pose.anim");
                AnimationClip motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Packages/zepeto.studio/resources/Animation/Videobooth_282.anim");

                Check("catalog:sdk-is-humanoid", motion != null && motion.isHumanMotion,
                    "SDK motion is not a Humanoid clip, retargeting assumption is wrong");
                Check("catalog:pose-detected", pose != null && pose.length <= 0.1f,
                    "A_pose was expected to be a single-frame pose");

                // The default landing selection must never be a static pose.
                FieldInfo selectedField = type.GetField("selectedAnimationIndex", AnyInstance);
                int selected = (int)selectedField.GetValue(window);
                MethodInfo getSelected = type.GetMethod("GetSelectedPackageAnimation", AnyInstance);
                AnimationClip selectedClip = getSelected.Invoke(window, null) as AnimationClip;
                Check("catalog:default-not-pose",
                    selectedClip != null && selectedClip.length > 0.1f,
                    "default selection is '" + (selectedClip == null ? "NULL" : selectedClip.name) + "', a static pose");
                Note("catalog:default", (selectedClip == null ? "NULL" : selectedClip.name) + " index=" + selected);

                // A pose must be refused with an explanation rather than silently assigned.
                MethodInfo blockReason = type.GetMethod("GetSelectedMotionBlockReason", AnyInstance);
                List<AnimationClip> clips = new List<AnimationClip>();
                object listObj = type.GetField("packageAnimations", AnyInstance).GetValue(window);
                foreach (object o in (System.Collections.IList)listObj) { clips.Add(o as AnimationClip); }

                int poseIndex = clips.IndexOf(pose);
                if (poseIndex >= 0)
                {
                    selectedField.SetValue(window, poseIndex);
                    string reason = blockReason.Invoke(window, null) as string;
                    Check("catalog:pose-blocked", !string.IsNullOrEmpty(reason),
                        "selecting a static pose was not blocked");
                    selectedField.SetValue(window, selected);
                }
                else
                {
                    Fail("catalog:pose-blocked", "A_pose was not present in the catalog");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// The avatar performs whatever the AnimatorOverrideController slot maps to, not what
        /// PlaygroundController.AnimationClip holds. The SDK ships that slot mapped to A_pose (0.04s), so a helper
        /// that only writes the field leaves the avatar standing still. This locks in the real playback path.
        /// </summary>
        private static void TestMotionReachesPlaybackSlot()
        {
            AnimationClip motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Packages/zepeto.studio/resources/Animation/Videobooth_282.anim");
            if (motion == null)
            {
                Fail("playback-slot", "Videobooth_282.anim not found in the SDK");
                return;
            }

            ZepetoStudioHelperWindow window = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
            try
            {
                Type type = typeof(ZepetoStudioHelperWindow);
                type.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(window, null);

                MethodInfo ensureLocal = type.GetMethod("EnsureLocalAnimatorController", AnyInstance);
                object[] ensureArgs = new object[] { null };
                bool localOk = (bool)ensureLocal.Invoke(window, ensureArgs);
                Check("playback-slot:local-controller", localOk,
                    "could not create a project-local AnimatorOverrideController: " + ensureArgs[0]);

                MethodInfo assign = type.GetMethod("AssignAnimationClip", AnyInstance);
                bool assigned = (bool)assign.Invoke(window, new object[] { motion, false });
                Check("playback-slot:assign", assigned, "AssignAnimationClip returned false");

                MethodInfo getPlayback = type.GetMethod("GetPlaybackClip", AnyInstance);
                AnimationClip playback = getPlayback.Invoke(window, null) as AnimationClip;

                Check("playback-slot:updated", playback == motion,
                    "override slot holds '" + (playback == null ? "NULL" : playback.name) + "', expected " + motion.name);
                Check("playback-slot:not-a-pose", playback != null && playback.length > 0.1f,
                    "override slot still holds a static pose, the avatar would not move");
                Note("playback-slot:value", playback == null ? "NULL" : playback.name + " " + playback.length.ToString("0.00") + "s");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// The live-reload loop rests on one invariant: EditorUtility.CopySerialized replaces an AnimationClip
        /// asset's CONTENTS while keeping its GUID and instanceID, so the AnimatorOverrideController's
        /// reference survives and the running Animator picks the new motion up without a rebind. If that is
        /// false the whole design collapses to "rebind the controller every time", so it is asserted first.
        /// </summary>
        private static void TestLiveReloadPlumbing()
        {
            Type type = typeof(ZepetoStudioHelperWindow);

            MethodInfo loopSetting = type.GetMethod("ApplyClipLoopSetting", AnyStatic);
            if (loopSetting == null)
            {
                Fail("live:loop-helper", "ApplyClipLoopSetting was not promoted to the outer class");
            }

            PropertyInfo liveClipPath = type.GetProperty("LiveClipAssetPath", AnyStatic);
            if (liveClipPath == null)
            {
                Fail("live:clip-path", "LiveClipAssetPath is missing");
                return;
            }

            Note("live:clip-path", (string)liveClipPath.GetValue(null, null));

            AnimationClip source = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Packages/zepeto.studio/resources/Animation/Videobooth_282.anim");
            if (source == null)
            {
                Fail("live:source", "Videobooth_282.anim not found in the SDK");
                return;
            }

            const string probePath = "Assets/ZepetoHelperTests/LiveReloadProbe.anim";
            AssetDatabase.DeleteAsset(probePath);

            AnimationClip probe = new AnimationClip();
            probe.name = "LiveReloadProbe";
            AssetDatabase.CreateAsset(probe, probePath);
            AssetDatabase.SaveAssets();

            try
            {
                AnimationClip loaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(probePath);
                if (loaded == null)
                {
                    Fail("live:probe-created", "could not create the probe clip at " + probePath);
                    return;
                }

                string guidBefore = AssetDatabase.AssetPathToGUID(probePath);
                int idBefore = loaded.GetInstanceID();
                float lengthBefore = loaded.length;

                string keepName = loaded.name;
                EditorUtility.CopySerialized(source, loaded);
                loaded.name = keepName;

                string guidAfter = AssetDatabase.AssetPathToGUID(probePath);
                int idAfter = loaded.GetInstanceID();

                Check("live:guid-preserved", guidBefore == guidAfter && !string.IsNullOrEmpty(guidAfter),
                    "GUID changed across CopySerialized: " + guidBefore + " -> " + guidAfter);
                Check("live:instanceid-preserved", idBefore == idAfter,
                    "instanceID changed across CopySerialized: " + idBefore + " -> " + idAfter);
                Check("live:content-replaced", !Mathf.Approximately(loaded.length, lengthBefore)
                        && Mathf.Approximately(loaded.length, source.length),
                    "clip contents did not take: len " + lengthBefore.ToString("0.000") + " -> "
                        + loaded.length.ToString("0.000") + ", source " + source.length.ToString("0.000"));
                Check("live:name-restored", loaded.name == keepName,
                    "name was left as '" + loaded.name + "', expected '" + keepName + "'");

                if (loopSetting != null)
                {
                    loopSetting.Invoke(null, new object[] { loaded, true });
                    Check("live:loop-applied", loaded.wrapMode == WrapMode.Loop,
                        "loop flag did not apply, the motion would play once and freeze");
                }

                Note("live:probe", "len=" + loaded.length.ToString("0.00") + "s guid=" + guidAfter);
            }
            finally
            {
                AssetDatabase.DeleteAsset(probePath);
                AssetDatabase.Refresh();
            }
        }

        // ---------- helpers ----------

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

        private static void Fail(string name, string detail)
        {
            failCount++;
            results.Add("FAIL " + name + " :: " + detail);
        }

        private static void Note(string name, string detail)
        {
            results.Add("NOTE " + name + " :: " + detail);
        }
    }
}
