using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Easy.ZepetoHelper.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Easy.ZepetoHelper.SelfTestEditor
{
    /// <summary>
    /// 이 어셈블리의 러너 네 개가 "실행 전 상태를 어떻게 지키고 되돌리는가"를 모아 둔 곳이다.
    ///
    /// 두 가지를 담당한다. (1) 저장하지 않은 열린 씬에 대한 거부, (2) 러너가 건드리고 되돌려야 하는
    /// 에디터 세션 상태(작업 단계 완료 플래그)와 Play 종료 대기.
    ///
    /// 씬을 갈아치우거나 저장하거나 씬 안에 오브젝트를 만드는 러너는, 열린 씬에 저장하지 않은 편집이
    /// 있으면 반드시 거부해야 한다. 그 거부문은 러너마다 복사돼 있었고 ZepetoRigExportRun에는 아예
    /// 없었다. 그런데 그 러너가 남긴 dirty 플래그 때문에 가드를 가진 나머지 러너들이 그 세션 내내
    /// 거부하는 상태가 됐다. ZepetoLiveReloadRun도 같은 상태였다 - 네 개 중 가장 침습적인 러너인데
    /// 가드가 없었다. 구현 하나, 메시지 모양 하나, 호출 지점 전부.
    /// </summary>
    internal static class ZepetoSelfTestSceneGuard
    {
        /// <summary>
        /// EditorSceneManager.ClearSceneDirtiness(Scene)는 internal이라 리플렉션으로 잡는다. 에디터 전용
        /// 테스트 어셈블리에서는 받아들일 만한 비용이고, null은 "이 에디터가 그 메서드를 제공하지 않는다"는
        /// 뜻일 뿐이라 TryClearSceneDirtiness가 숨기지 않고 그대로 보고한다. 공개 대체재는 없다.
        /// MarkSceneDirty에는 짝이 없고, 플래그를 내리는 유일한 공개 경로는 파일을 쓰는 것인데 이 러너들은
        /// 사용자 대신 씬 파일을 쓰는 일이 금지돼 있다.
        /// </summary>
        private static readonly MethodInfo ClearSceneDirtinessMethod = typeof(EditorSceneManager).GetMethod(
            "ClearSceneDirtiness",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(Scene) },
            null);

        /// <summary>
        /// 저장하지 않은 편집이 있는 첫 번째 열린 씬의 경로, 전부 저장돼 있으면 null. 활성 씬만이 아니라
        /// 로드된 씬 전부를 본다. 멀티 씬 구성에서는 dirty한 쪽이 additive로 올라와 있을 수 있다.
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
        /// 단 하나뿐인 "저장 안 된 씬이 열려 있으면 거부" 지점. 호출자가 실행하면 안 될 때는 문제의 씬
        /// 경로를, 전부 저장돼 있어 진행해도 될 때는 null을 돌려준다.
        ///
        /// 거부는 세 곳에 남긴다. 가드가 막아 준 피해보다 "깨끗한 실행처럼 보이는 건너뜀"이 더 나쁘기
        /// 때문이다. 호출자의 리포트, 콘솔 경고, 그리고 skipRecordPath가 있으면 첫 줄이 SKIPPED인 기록
        /// 파일. 그 기록은 일부러 자기 경로를 따로 쓴다. 러너의 result 파일은 계속 "마지막 실제 실행"을
        /// 뜻해야 하므로 거부가 그것을 덮어써서는 안 된다. pass=0 fail=0 집계는 깨끗한 통과와 구분되지
        /// 않으면서 추적 중인 기준 파일까지 없애 버린다.
        ///
        /// 이 러너들의 다른 모든 경로와 마찬가지로 일부러 비대화식이다. 대화상자도 없고, 사용자 대신
        /// 저장하지도 않는다. 남의 미완성 씬을 커밋해 버리는 것도 그 나름의 피해다.
        /// </summary>
        /// <param name="runnerLabel">거부하는 러너 이름. 콘솔 경고와 기록 파일에 쓰인다.</param>
        /// <param name="consequence">이 실행이 그 씬에 무엇을 했을지 알려주는 한국어 문장. 경고와 기록
        /// 양쪽에 함께 나간다.</param>
        /// <param name="skipRecordPath">SKIPPED 기록을 쓸 경로. null이나 빈 문자열이면 쓰지 않는다.</param>
        /// <param name="report">호출자 자신의 리포트 출력기. 없으면 null.</param>
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
        /// 일어나지 않은 실행의 기록을 쓴다. 첫 줄에 SKIPPED와 이유, 그다음 시각, 그다음 상세.
        /// result 파일의 pass/fail 모양은 절대 쓰지 않고 result 파일 경로에도 절대 쓰지 않는다.
        /// 두 파일은 어느 방향으로도 헷갈려서는 안 된다.
        /// </summary>
        internal static void WriteSkipRecord(string skipRecordPath, string headline, string detail)
        {
            if (string.IsNullOrEmpty(skipRecordPath))
            {
                return;
            }

            StringBuilder record = new StringBuilder();
            record.AppendLine(headline);
            // InvariantCulture인 이유: 사용자 지정 서식 문자열에서 ':'는 문화권에 따라 해석이 달라지는
            // 지정자다. 에디터 로케일에 따라 모양이 바뀌는 타임스탬프는 아무것도 비교할 수 없다.
            record.AppendLine("at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            record.AppendLine("----");
            record.AppendLine(detail);
            record.AppendLine("Nothing ran, so no result file was written and the previous result file still "
                + "describes the last real run.");
            try { File.WriteAllText(skipRecordPath, record.ToString()); } catch { }
        }

        /// <summary>
        /// 이전 거부가 남긴 기록을 지운다. 실제 실행의 맨 앞에서 부른다. 새 result 파일 옆에 남아 있는
        /// 오래된 SKIPPED 파일이 "마지막 실제 실행은 거부됐다"처럼 읽히게 두면 안 되기 때문이다.
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
        /// 디스크에 아무것도 쓰지 않고 로드된 모든 씬의 "저장하지 않은 변경" 플래그만 내린다.
        ///
        /// (1) 모든 열린 씬이 이미 저장돼 있을 때만 시작하도록 거부했고, (2) 자기 실행이 씬에 남긴 것이
        /// 없음을 확인한 호출자에게만 정당하다. 그 두 조건 아래에서 이 플래그는 그냥 사실이 아니며 -
        /// 메모리와 디스크가 다시 일치한다 - 그대로 두는 것도 무해하지 않다. 여기 러너들은 전부 dirty한
        /// 씬에서 거부하므로, 한 실행이 남긴 플래그가 그 세션 내내 나머지를 조용히 막는다.
        ///
        /// 실패해도 조용하지 않게: `detail`이 어느 쪽이든 결과를 설명하고, false는 "씬이 여전히 dirty로
        /// 표시돼 있다"는 뜻이며 호출자는 그것을 보고해야 한다.
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

                // 호출 결과를 믿지 않고 매니저에서 다시 읽는다. Scene은 네이티브 핸들을 감싼 struct라
                // isDirty는 매번 새 질의이고, 이것이 여기서 가능한 유일한 실제 확인이다.
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

        internal static string DescribeScene(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? "untitled scene" : scene.path;
        }

        // ---------- Play 종료 대기 ----------

        /// <summary>
        /// 재무장을 기다리는 중인 러너 이름. 한 러너가 대기 핸들러를 두 번 붙이지 못하게 막는 용도라
        /// 도메인 리로드와 함께 사라져도 된다. 리로드가 일어나면 트리거 파일이 아직 남아 있으므로
        /// 러너의 [InitializeOnLoad] 경로가 처음부터 다시 판단한다.
        /// </summary>
        private static readonly HashSet<string> runnersWaitingForEditMode = new HashSet<string>();

        /// <summary>
        /// Play가 끝나 편집 모드로 돌아오면 `resume`을 한 번 부른다. 이미 기다리는 중이면 아무것도 하지
        /// 않고 false.
        ///
        /// delayCall로 다시 시도하면 안 된다. delayCall은 에디터 틱마다 다시 불려서 Play 세션 내내 콘솔을
        /// 도배한다. 의미 있는 이벤트 하나만 기다린다. 네 러너 모두 이 함수를 쓴다. 예전에는 둘만
        /// 기다리고 둘은 조용히 return해서, 사용자 입장에서는 트리거 파일을 놓았는데 다음 재컴파일까지
        /// 아무 일도 일어나지 않는 것처럼 보였다.
        /// </summary>
        internal static bool WaitForEditMode(string runnerLabel, Action resume)
        {
            if (resume == null || !runnersWaitingForEditMode.Add(runnerLabel))
            {
                return false;
            }

            Action<PlayModeStateChange> handler = null;
            handler = change =>
            {
                if (change != PlayModeStateChange.EnteredEditMode)
                {
                    return;
                }

                EditorApplication.playModeStateChanged -= handler;
                runnersWaitingForEditMode.Remove(runnerLabel);
                EditorApplication.delayCall += () => resume();
            };

            EditorApplication.playModeStateChanged += handler;
            Debug.Log(runnerLabel + ": Play 중이라 대기합니다. Stop을 누르면 자동으로 실행됩니다.");
            return true;
        }

        // ---------- 작업 단계 완료 플래그 ----------

        /// <summary>
        /// 헬퍼가 단계 완료 여부를 담아 두는 SessionState 키를 들고 있는 상수들의 이름.
        ///
        /// 값이 아니라 상수 "이름"으로 잡는 이유: 헬퍼가 키 문자열을 바꾸면 여기서 null이 나와 눈에 띄게
        /// 실패하지, 엉뚱한 키를 조용히 복원하지 않는다. 세 플래그는 서로 연쇄한다 - ApplyZepetoId는
        /// SetAvatarOutfitStageComplete(false)를 거쳐 셋 다 내린다 - 그래서 셋을 한 묶음으로 다룬다.
        /// </summary>
        private static readonly string[] StageFlagKeyFields =
        {
            "AvatarOutfitStageCompleteSessionKey",
            "MotionSelectStageCompleteSessionKey",
            "ClipStageCompleteSessionKey",
        };

        private const string StageBackupPrefix = "Easy.ZepetoHelper.SelfTest.StageBackup.";
        private const string StageBackupArmedKey = "Easy.ZepetoHelper.SelfTest.StageBackup.Armed";

        /// <summary>
        /// 사용자의 작업 단계 진행 상황을 백업한다.
        ///
        /// 러너가 ApplyZepetoId나 AssignAnimationClip을 부르면 헬퍼는 "이 단계 다시 확인해야 한다"며
        /// 완료 플래그를 내린다. 사용자 입장에서는 테스트 하나 돌렸더니 헬퍼가 1번 카드로 되돌아가 있는
        /// 것이다. 러너가 바꾼 것은 러너가 되돌려야 한다.
        ///
        /// 백업 자체도 SessionState에 둔다. Play 진입은 도메인 리로드라 정적 필드는 복원 시점까지
        /// 살아남지 못한다. EditorPrefs는 틀린 선택이다 - 이 값들은 에디터 세션과 함께 죽어야 한다.
        ///
        /// 되돌리는 층은 SessionState 하나뿐이다. 헬퍼 창 인스턴스는 이 값을 자기 필드에 캐시하고
        /// 도메인 리로드/OnEnable에서 다시 읽으므로, Play를 거치는 러너는 그 시점에 화면까지 맞춰진다.
        /// Play를 거치지 않는 자기 테스트에서는 이미 열려 있던 창이 다음 리로드 때 따라온다.
        /// </summary>
        internal static string CaptureWorkflowStageFlags()
        {
            string[] keys = ResolveStageFlagKeys();
            if (keys == null)
            {
                return "helper stage keys not found, nothing captured";
            }

            for (int i = 0; i < keys.Length; i++)
            {
                SessionState.SetBool(StageBackupPrefix + i, SessionState.GetBool(keys[i], false));
            }

            SessionState.SetBool(StageBackupArmedKey, true);
            return keys.Length + " stage flag(s) captured";
        }

        /// <summary>
        /// CaptureWorkflowStageFlags가 잡아 둔 값을 되돌린다. 잡아 둔 것이 없으면 아무것도 쓰지 않고
        /// null을 돌려준다. 호출자가 그때는 리포트에 한 줄도 남기지 않도록 - 실행하지 않은 실행의
        /// 리포트에 줄이 붙는 것을 막으려는 것이다.
        /// 실행 도중 죽어 백업이 남더라도 다음 실행의 Capture가 덮어쓰므로 오래된 값이 되살아나지 않는다.
        /// </summary>
        internal static string RestoreWorkflowStageFlags()
        {
            if (!SessionState.GetBool(StageBackupArmedKey, false))
            {
                return null;
            }

            string[] keys = ResolveStageFlagKeys();
            if (keys == null)
            {
                SessionState.SetBool(StageBackupArmedKey, false);
                return "helper stage keys not found, stage flags left as the run set them";
            }

            StringBuilder detail = new StringBuilder();
            for (int i = 0; i < keys.Length; i++)
            {
                bool value = SessionState.GetBool(StageBackupPrefix + i, false);
                SessionState.SetBool(keys[i], value);
                if (i > 0)
                {
                    detail.Append(", ");
                }

                detail.Append(StageFlagKeyFields[i]).Append('=').Append(value ? "true" : "false");
            }

            SessionState.SetBool(StageBackupArmedKey, false);
            return detail.ToString();
        }

        /// <summary>
        /// 헬퍼의 private const 세 개에서 실제 키 문자열을 읽어 온다. 하나라도 없으면 null - 부분 복원은
        /// 세 플래그가 서로 연쇄하는 이 상태에서 복원하지 않는 것보다 나쁘다.
        /// </summary>
        private static string[] ResolveStageFlagKeys()
        {
            string[] keys = new string[StageFlagKeyFields.Length];
            for (int i = 0; i < StageFlagKeyFields.Length; i++)
            {
                FieldInfo field = typeof(ZepetoStudioHelperWindow).GetField(
                    StageFlagKeyFields[i],
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                keys[i] = field == null ? null : field.GetValue(null) as string;
                if (string.IsNullOrEmpty(keys[i]))
                {
                    return null;
                }
            }

            return keys;
        }
    }
}
