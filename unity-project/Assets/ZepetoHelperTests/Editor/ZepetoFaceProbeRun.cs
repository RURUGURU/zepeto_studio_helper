using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// Play 중 내려온 진짜 ZEPETO 아바타에 얼굴 블렌드셰이프가 있는지 확인하는 일회성 조사 도구.
    ///
    /// 이 질문이 중요한 이유: 우리가 내보내는 ZepetoBaseModel.prefab에는 블렌드셰이프가 0개다.
    /// 그런데 SDK가 함께 배포하는 공식 클립(ANI_039_v03.anim 등)에는
    /// blendShape.zepeto.eyeBlinkLeft 같은 커브가 57종 들어 있고 path는 "body"다.
    /// '기본 몸에는 없지만 진짜 아바타에는 있다'가 참인지 확인해야 눈 깜빡임이 원리적으로
    /// 가능한지 말할 수 있다.
    ///
    /// [주의] Play 진입은 도메인 리로드를 일으킨다. 첫 판은 EditorApplication.update에 붙은 콜백과
    /// static 필드로 상태를 들고 있었는데, 리로드가 둘 다 지워서 리포트가 영영 나오지 않았다.
    /// 이 프로젝트가 STATUS.md에 적어 둔 바로 그 함정이다. 상태는 SessionState에 두고, 구독은
    /// 리로드마다 다시 도는 [InitializeOnLoadMethod]에서 다시 건다.
    /// </summary>
    [InitializeOnLoad]
    public static class ZepetoFaceProbeRun
    {
        private const string TriggerPath = "zepeto-face-probe.trigger";
        private const string ReportPath = "zepeto-face-probe.report.txt";
        private const string ArmedKey = "ZepetoFaceProbe.Armed";
        private const string DeadlineKey = "ZepetoFaceProbe.Deadline";

        static ZepetoFaceProbeRun()
        {
            EditorApplication.delayCall += Bootstrap;
        }

        private static void Bootstrap()
        {
            if (SessionState.GetBool(ArmedKey, false))
            {
                // Play 쪽 리로드다. 아바타가 내려올 시간을 주고 나서 조사한다.
                if (EditorApplication.isPlaying)
                {
                    if (SessionState.GetFloat(DeadlineKey, 0f) <= 0f)
                    {
                        SessionState.SetFloat(DeadlineKey,
                            (float)EditorApplication.timeSinceStartup + 25f);
                    }
                    EditorApplication.update += Tick;
                }
                return;
            }

            if (!File.Exists(TriggerPath))
            {
                return;
            }
            File.Delete(TriggerPath);
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetFloat(DeadlineKey, 0f);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(DeadlineKey, 0f))
            {
                return;
            }
            EditorApplication.update -= Tick;
            SessionState.SetBool(ArmedKey, false);
            Report();
            EditorApplication.ExitPlaymode();
        }

        private static void Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ZEPETO face probe - Play 중 실제 아바타");
            sb.AppendLine("----");

            SkinnedMeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            sb.AppendLine("SkinnedMeshRenderer 개수: " + renderers.Length);

            foreach (SkinnedMeshRenderer smr in renderers)
            {
                Mesh mesh = smr.sharedMesh;
                int count = mesh == null ? -1 : mesh.blendShapeCount;
                sb.AppendLine(string.Format("  name='{0}'  mesh='{1}'  blendShapes={2}",
                    smr.name, mesh == null ? "(null)" : mesh.name, count));

                if (mesh == null || count <= 0)
                {
                    continue;
                }
                List<string> notable = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    if (n.IndexOf("Blink", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Smile", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("brow", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        notable.Add(n);
                    }
                }
                sb.AppendLine("     깜빡임/표정 후보 " + notable.Count + "개: "
                    + (notable.Count == 0 ? "(없음)" : string.Join(", ", notable.ToArray())));
            }

            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("face probe written");
        }
    }
}
