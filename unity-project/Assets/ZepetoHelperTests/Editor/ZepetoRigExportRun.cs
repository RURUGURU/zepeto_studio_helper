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
    /// 진짜 ZEPETO 기본 모델을 헬퍼를 통해 FBX로 내보내고, 그 결과가 Blender가 실제로 열 수 있는
    /// 리그인지 단언한다. Unity -> Blender -> Unity 왕복의 앞쪽 절반이다.
    ///
    /// 예전에는 사실만 나열하고 pass/fail은 말하지 않았다. 그래서 망가진 내보내기와 정상 내보내기가
    /// 누군가 숫자를 눈으로 확인하기 전까지 똑같이 읽혔다. 아래 세 단언은 왕복이 애초에 가능한지를
    /// 가르는 것들이고, zepeto-helper-selftest.result.txt와 같은 PASS/FAIL/NOTE 형식으로
    /// ResultPath에 쓰인다.
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
        /// 모든 바이너리 fbx가 시작하는 18바이트. Blender는 ASCII fbx를 아예 거부하는데("ASCII FBX files
        /// are not supported") Unity는 어느 쪽이든 내보내기 성공이라고 보고한다. 그래서 이 리터럴이
        /// "내보낸 리그를 Blender에서 열 수는 있는가"에 대해 로컬에서 얻을 수 있는 유일한 증거이고,
        /// 그것이 내보내기 경로가 존재하는 이유 전부다.
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
            // 내보내기는 에셋을 쓰기 때문에 Play 중에는 거부된다. 트리거는 그대로 두고 편집 모드로
            // 돌아올 때까지 기다린다. 조용히 return하면 사용자에게는 트리거 파일이 아무 일도 하지 않는
            // 것처럼 보인다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ZepetoSelfTestSceneGuard.WaitForEditMode(RunnerLabel, Bootstrap);
                return;
            }

            File.Delete(TriggerPath);
            Run();
        }

        private static void Run()
        {
            File.WriteAllText(ReportPath, "ZEPETO rig export (serial " + Serial + ")\n----\n");

            // 정적 상태는 다음 도메인 리로드까지 살아남는다. 안 지우면 같은 세션의 두 번째 트리거가
            // 첫 실행의 결과에 이어 붙어 두 번 집계된다.
            results.Clear();
            passCount = 0;
            failCount = 0;

            // [QC][Invariant:never_touch_an_unsaved_open_scene]
            // TryExportZepetoRigToFbx는 ZEPETO 기본 모델을 활성 씬에 Instantiate했다가 다시 지운다.
            // 즉 이 러너도 형제들과 똑같이 사용자가 열어 둔 씬을 건드리는데, 넷 중 유일하게 그 거부가
            // 없었다. 더 나쁜 것은 그 변형이 세운 dirty 플래그 때문에 자기 테스트와 커스텀 모션 실행이
            // 그 세션 내내 거부됐다는 점이다. 리그 내보내기를 트리거하면 자기 테스트 트리거가 죽었다.
            if (ZepetoSelfTestSceneGuard.RefuseIfAnyOpenSceneIsDirty(
                    RunnerLabel,
                    "이 실행은 내보내기 과정에서 열린 씬에 임시 오브젝트를 만들고 지우기 때문에 씬을 건드립니다.",
                    SkipPath,
                    Append) != null)
            {
                Append("  Save or discard the scene yourself, then drop " + TriggerPath + " again.");
                // 일부러 WriteResults 전에 return한다. ResultPath는 계속 "마지막 실제 실행"을 뜻해야
                // 하는데 거기 적힌 pass=0 fail=0은 깨끗한 통과와 똑같이 읽힌다.
                return;
            }

            ZepetoSelfTestSceneGuard.ClearSkipRecord(SkipPath);

            // 실행 끝의 "씬에 아무것도 남지 않았다" 검사를 위한 기준값.
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

            // 파일이 없는 것은 여기서 진짜 실패다. "아직 내보내기를 안 돌렸다"가 아니다. 내보내기
            // 경로는 바이너리 검증에 실패하면 자기 출력물을 스스로 지운다. 쓸 수 없는 ASCII fbx가
            // 완성된 얼굴로 프로젝트에 남지 못하게 하려는 것이다. 그래서 "파일 없음"은 내보내기가
            // 돌았고 Blender가 열 수 없는 무언가를 만들었다는 뜻이다.
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
        /// 로드된 각 씬의 루트 GameObject 개수를 씬 순서대로.
        ///
        /// 이 실행이 씬에 하는 변형은 전부 임시 루트 오브젝트다 - 내보내기가 Instantiate했다가 finally
        /// 블록에서 지우는 기본 모델, 그리고 헬퍼 창 인스턴스가 자기와 함께 넣고 빼는 대역 몸. 그래서
        /// 개수가 그대로 돌아왔다는 것이 아무것도 살아남지 않았다는 증거다. 비교는 헬퍼 인스턴스를 파괴한
        /// 뒤에만 한다. 그때가 둘 중 두 번째가 사라진 시점이다.
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
        /// 내보내기가 열린 씬에 무엇을 했는지 보고하고, 가능하면 되돌린다.
        ///
        /// 내보내기는 씬에 흔적을 남기지 않아도 "저장하지 않은 변경" 표시는 남긴다. Instantiate와
        /// DestroyImmediate 둘 다 그 플래그를 세우고 어느 쪽도 내리지 않는다. 이 플래그는 겉모습 문제가
        /// 아니다. 이 어셈블리의 모든 러너가 dirty한 씬에서는 시작을 거부하므로, 리그 내보내기 한 번이
        /// 사용자가 편집한 적도 없는 씬을 저장하거나 되돌릴 때까지 자기 테스트와 커스텀 모션 실행을
        /// 막아 버린다.
        ///
        /// 그래서: 아무것도 남지 않았음을 먼저 증명하고 나서 디스크에 쓰지 않고 플래그만 내린다. 저장은
        /// 절대 하지 않고(이 러너는 사용자의 씬 파일을 쓰면 안 된다) 침묵도 하지 않는다. 어느 쪽이든
        /// NOTE로 남긴다. Check가 아니라 NOTE인 이유는 이 러너들의 검사 이름이 기록된 결과 파일과
        /// 비교되기 때문이다.
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
        /// 실행이 열린 씬에 남긴 것, 없으면 null.
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
                    return ZepetoSelfTestSceneGuard.DescribeScene(scene)
                        + " now has " + now + " root objects, had " + rootCountsBefore[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 파일 앞부분을 ASCII로 디코딩한 값, 읽지 못했으면 그 이유를 짧게. BinaryFbxMagic.Length 바이트만
        /// 정확히 읽어서 그 상수와 바로 비교할 수 있게 한다.
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
        /// zepeto-helper-selftest.result.txt와 같은 형식의 pass/fail 요약을 그 옆에 쓰고, 같은 줄들을
        /// 진단 리포트에도 복사한다. 두 파일이 서로 다른 말을 하는 일이 없게 하려는 것이다.
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
