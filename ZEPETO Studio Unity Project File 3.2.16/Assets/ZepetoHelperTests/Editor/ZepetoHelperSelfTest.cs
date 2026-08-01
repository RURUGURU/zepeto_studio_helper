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
    /// 배치 모드에 라이선스를 줄 수 없는 환경이라, 돌고 있는 에디터 안에서 진짜 헬퍼 창을 상대로
    /// 검사한다. 트리거 파일을 놓으면 다음 스크립트 리로드에서 실행되고 평문 리포트를 쓴다.
    /// </summary>
    public static class ZepetoHelperSelfTest
    {
        // Unity가 시작할 때 Temp/를 비우기 때문에 핸드셰이크 파일은 프로젝트 루트에 둔다.
        private const string TriggerPath = "zepeto-helper-selftest.trigger";
        private const string ResultPath = "zepeto-helper-selftest.result.txt";
        // 거부는 자기 파일을 따로 쓴다. ResultPath는 마지막 "실제" 실행이 찾아낸 것 외에는 아무 말도
        // 해서는 안 된다.
        private const string SkipPath = "zepeto-helper-selftest.skipped.txt";
        private const string RunnerLabel = "ZEPETO Helper self test";
        private const string TestSceneFolder = "Assets/ZepetoHelperTests";
        private const string TestScenePath = TestSceneFolder + "/SelfTestLoaderScene.unity";
        private const string PackageRoot = "Packages/com.easy.zepeto-helper";
        private const string LivePreviewSourcePath = PackageRoot + "/Editor/ZepetoStudioHelperWindow.LivePreview.cs";

        // live-part:both-enumeration-sites가 두 메서드 몸통 안에서 찾는 코드 조각. 상수로 둔 이유는
        // 실패 메시지가 이 값을 그대로 인용해서, 검사가 빨개졌을 때 무엇을 찾다 못 찾았는지가 리포트만
        // 보고도 드러나게 하려는 것이다.
        private const string PartSkipNeedle = "EndsWith(\".part\"";

        /// <summary>
        /// 한 번의 완전한 실행이 내야 하는 Check/Fail 줄의 개수.
        ///
        /// [QC][Invariant:result_file_is_the_last_real_run]
        /// 0인지만 보는 가드로는 부족했다. Assets/Playground.unity가 없으면 TestRealTemplateScene이
        /// NOTE만 남기고 돌아가면서 22개를 건너뛰는데, 그러면 'pass=38 fail=0'이 추적 중인 'pass=60'을
        /// 덮어썼다. 깨끗한 통과와 구분이 안 되는 부분 실행이다. 개수가 다르면 그 자체를 FAIL로 적어
        /// 결과 파일이 절대 깨끗해 보이지 않게 한다. 검사를 추가하면 이 숫자도 함께 올려야 하고,
        /// 잊으면 실패가 나는 것이 의도다.
        /// </summary>
        private const int ExpectedCheckCount = 70;

        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly List<string> results = new List<string>();
        private static int passCount;
        private static int failCount;

        /// <summary>
        /// 작성자의 실제 ZEPETO 아이디. 동시에 아래 아이디 형식 검사의 "현실의 아이디도 받아들여진다"
        /// 픽스처이기도 하다. 그래서 리포트의 검사 이름들이 예전 그대로 한 바이트도 다르지 않게 남는다.
        ///
        /// 두 조각으로 나눠 이어 붙인 것은 의도다. 여기에 그대로 적으면 (a) 아이디가 사라졌는지 확인하려고
        /// 저장소를 grep하는 사람에게 걸린다 - 그것이 사라졌음을 증명하는 코드 안의 거짓 양성이다 -
        /// 그리고 (b) no-personal-id 스캔 범위가 이 어셈블리 소스까지 넓어지는 순간 그 스캔에 걸린다.
        /// 컴파일된 dll 앞에서는 아무것도 숨기지 못하고(컴파일러가 이어 붙이기를 접는다) 숨길 생각도
        /// 없다. 이 불변식은 소스와 문서로 "배포되는" 것에 관한 것이다.
        /// PersonalTokens보다 먼저 선언한다. static 필드 초기화는 선언 순서대로 돌기 때문이다.
        ///
        /// 알고 감수하는 예외가 하나 있다. 아래 검사 이름이 "id-valid:" + 이 값이라, 추적되는
        /// zepeto-helper-selftest.result.txt에는 이 아이디가 그대로 적힌다. 스캔 범위는 패키지가
        /// 배포하는 파일이라 그 결과 파일은 애초에 불변식의 사정권 밖이다.
        /// </summary>
        private static readonly string PersonalIdSample = "darbam" + "s77";

        // [QC][Invariant:no_personal_account_data_shipped]
        // 패키지가 배포하는 어떤 파일에도 나타나면 안 되는 토큰들. 휴리스틱이 아니라 손으로 관리하는
        // 명시적 목록인 것은 의도다. \w+\d{2,} 같은 패턴은 Videobooth_282 같은 SDK 에셋 이름까지 잡고,
        // 이 검사는 결정적이고 의존성 없이 남아야 한다. 개발 중에 새 개인 계정이나 기계 이름을 쓰게 되면
        // 여기에 추가한다.
        private static readonly string[] PersonalTokens =
        {
            // 헬퍼가 한때 기본값으로 배포하던 계정.
            PersonalIdSample,

            // 그 어간. C:\Users\<user>\Desktop\... 같은 절대 경로가 소스나 문서에 붙여넣기라도 되면
            // 작성자의 Windows 프로필 폴더까지 함께 잡힌다.
            "dar" + "ba",
        };

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        // 에디터 시작이 항상 DidReloadScripts를 거치지는 않아서 트리거를 여기서도 확인한다. 트리거
        // 파일은 첫 실행에서 소비되므로 검사 묶음이 두 번 돌지 않는다.
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

            // 이 묶음은 씬을 열고 만드는데 Unity는 Play 중에 그것을 금지한다:
            //   InvalidOperationException: This cannot be used during play mode
            // Play 중의 재컴파일이 이 훅을 발동시키므로, 가드가 없으면 무장된 트리거가 세션 한가운데서
            // 터지고 그 예외가 헬퍼의 OnGUI 레이아웃을 망가뜨린다 - 버튼들이 사라진다.
            // 트리거는 그대로 두고 Play가 끝나면 실행한다. 그래야 조용히 건너뛰는 것도 없다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ZepetoSelfTestSceneGuard.WaitForEditMode(RunnerLabel, RunIfRequested);
                return;
            }

            File.Delete(TriggerPath);
            Run();
        }

        [MenuItem("Window/Easy/Run ZEPETO Helper Self Test")]
        public static void Run()
        {
            // 두 번째 방어선. 메뉴 항목은 Play 중에도 고를 수 있고, 아래 씬 API들은 똑같은
            // InvalidOperationException을 던진다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("ZEPETO Helper self test는 Play 중에 실행할 수 없습니다 "
                    + "(씬을 열고 만드는 검사가 있습니다). Stop을 누른 뒤 다시 실행하세요.");
                return;
            }

            results.Clear();
            passCount = 0;
            failCount = 0;

            // 세 번째 방어선이자, 에디터 상태가 아니라 사용자의 작업을 지키는 유일한 방어선.
            // 이 묶음은 EditorSceneManager.NewScene(..., Single)을 부르고 열려 있는 것 위에
            // Assets/Playground.unity를 연다. 두 API 모두 묻지 않기 때문에, 굴러다니던 트리거 파일 하나가
            // 누가 한 시간 동안 편집하던 씬을 조용히 버리기에 충분했다.
            // 일부러 묻지 않고(트리거 경로는 비대화식이어야 한다) 일부러 사용자 대신 저장하지도 않는다.
            // 남의 미완성 씬을 커밋해 버리는 것도 그 나름의 피해다.
            // 보호 대상은 묶음이 "시작할 때" 열려 있던 씬뿐이다. 묶음이 스스로 만들고 더럽히는 씬은
            // 묶음의 것이고, 그것들은 계속 자유롭게 갈아치운다.
            //
            // [QC][Invariant:result_file_is_the_last_real_run]
            // 거부는 ResultPath 근처에도 가면 안 된다. 예전에는 건너뜀을 Note하고 WriteReport를 불렀고,
            // 그것이 추적 중인 "pass=60 fail=0" 기록을 "pass=0 fail=0"으로 덮어썼다. 깨끗한 실행과
            // 똑같이 읽히는 거부이고, 그 과정에서 기준 파일까지 사라졌다. SKIPPED 기록은 대신 SkipPath에
            // 첫 줄부터 남고, 여기서는 리포트가 쓰이기 전에 돌아간다.
            if (ZepetoSelfTestSceneGuard.RefuseIfAnyOpenSceneIsDirty(
                    RunnerLabel,
                    "이 검사는 열린 씬을 갈아치우기 때문에 편집 내용이 사라집니다.",
                    SkipPath,
                    null) != null)
            {
                return;
            }

            // 가드를 통과했으니 이것은 "실제" 실행이다. 앞선 거부의 기록은 이제 낡은 것이다.
            ZepetoSelfTestSceneGuard.ClearSkipRecord(SkipPath);

            // ApplyZepetoId는 여기서 여덟 번 넘게 불리고 그때마다 헬퍼가 아바타/의상 단계 완료를 내리며,
            // 그 플래그는 나머지 두 단계로 연쇄한다. 검사 한 번 돌렸다고 사용자의 헬퍼가 1번 카드로
            // 돌아가 있으면 안 되므로 실행 전 값을 들고 있다가 끝에서 되돌린다.
            ZepetoSelfTestSceneGuard.CaptureWorkflowStageFlags();

            try
            {
                TestNoHardcodedAccount();
                TestExportedRigArtifact();
                TestMcpCodeRemoved();
                TestVersionComparison();
                TestSdkDetection();
                TestIdSanitizeAndValidate();
                TestSdkLoaderShape();
                TestMultiAccountApplyOnRealWindow();
                TestWorkSceneDiscovery();
                TestSplitComponentBinding();
                TestCriticalGuards();
                TestRealTemplateScene();
            }
            catch (Exception exception)
            {
                Fail("harness", exception.ToString());
            }

            // [QC][Invariant:result_file_is_the_last_real_run]
            // 마지막 Check가 끝난 바로 이 자리에서 결과 파일을 쓴다. 아래 뒷정리 - 작업 씬 열기, 헬퍼
            // 띄우기, 단계 플래그 되돌리기 - 는 결과가 아니라 편의인데, 그중 하나가 던지면 방금 계산한
            // 검사 전부가 사라진다. 결과 파일은 이 실행이 남기는 유일한 지속적 산출물이라 뒷정리 실패와
            // 맞바꿀 수 있는 것이 아니다. 실제로 OpenScene은 씬 파일이 잠겨 있으면 던지고, Open()은
            // 창을 처음 만드는 경우 OnEnable -> RefreshAll 전체를 이 스택 위에서 돌린다.
            //
            // 그래서 순서는 "쓰고 - 정리하고 - 정리 결과를 덧붙여 다시 쓴다"이다. 두 번째 쓰기에 닿지
            // 못해도 디스크에는 이미 완전한 결과가 있고, 닿으면 뒷정리 NOTE까지 담긴 판이 그것을
            // 대체한다. 개수 비교는 VerifyExpectedCheckCount가 한 번만 하므로 두 번 써도 FAIL 한 줄이
            // 두 번 붙지 않는다.
            VerifyExpectedCheckCount();
            WriteReport();

            // RunCleanUp은 스스로 예외를 삼키고 NOTE로만 남기므로 아래 두 번째 쓰기는 사실상 항상
            // 실행된다. 그래도 위에서 한 번 쓰는 이유는, 그 사실에 기대지 않기 위해서다.
            RunCleanUp();
            WriteReport();

            Debug.Log("ZEPETO Helper self test finished. pass=" + passCount + " fail=" + failCount);
        }

        /// <summary>
        /// 검사가 전부 끝난 뒤의 뒷정리. 결과가 텍스트 파일에만 있지 않고 화면에도 보이도록 실제 작업
        /// 씬을 열고 헬퍼를 띄운 상태로 에디터를 남기고, 사용자의 단계 완료 플래그를 되돌리고, 이 묶음이
        /// 씬에 남긴 dirty 표시를 보고한다.
        ///
        /// 세 덩어리 모두 자기 try/catch를 갖는다. 여기서 예외 하나가 Run()을 빠져나가면 이미 쓰인
        /// 결과 파일은 살아남지만 나머지 뒷정리가 통째로 건너뛰어진다 - 특히 단계 플래그 복원을 놓치면
        /// 사용자의 헬퍼가 1번 카드로 되돌아간 채 남는다. 그것이 CaptureWorkflowStageFlags가 존재하는
        /// 이유이므로, 실패는 다음 덩어리를 막지 않고 NOTE 한 줄로만 남는다.
        /// </summary>
        private static void RunCleanUp()
        {
            try
            {
                OpenWorkSceneAndHelper();
            }
            catch (Exception exception)
            {
                Note("post-suite-window", "the work scene and helper window could not be left open ("
                    + exception.Message + "), so the scene-dirt report below describes whatever was open instead");
            }

            try
            {
                string stageDetail = ZepetoSelfTestSceneGuard.RestoreWorkflowStageFlags();
                Note("stage-flags-restored", string.IsNullOrEmpty(stageDetail)
                    ? "nothing was captured, so the helper's stage flags were left as the suite set them"
                    : stageDetail);
            }
            catch (Exception exception)
            {
                Note("stage-flags-restored", "the restore threw (" + exception.Message
                    + "), so the helper may still be showing the stage completion this suite reset");
            }

            try
            {
                ReportSceneDirtAfterSuite();
            }
            catch (Exception exception)
            {
                Note("scene-dirt", "the probe threw (" + exception.Message
                    + "), so whether this suite left an open scene marked as modified is unknown");
            }
        }

        /// <summary>
        /// 작업 씬을 열고 헬퍼 창을 띄운다. 템플릿 씬이 없는 프로젝트에서는 아무것도 하지 않는다.
        ///
        /// 마지막에 ForcePreviewBodySync를 부르는 것이 요점이다. 이유는 그쪽 주석에 있다 - 요약하면
        /// 이 뒤에 오는 scene-dirt 프로브가 "Open() 이후의 안정 상태"를 재게 하려는 것이다.
        /// </summary>
        private static void OpenWorkSceneAndHelper()
        {
            if (!File.Exists("Assets/Playground.unity"))
            {
                return;
            }

            EditorSceneManager.OpenScene("Assets/Playground.unity", OpenSceneMode.Single);
            ZepetoStudioHelperWindow.Open();

            string syncDetail = ForcePreviewBodySync();
            if (syncDetail != null)
            {
                Note("post-suite-window", syncDetail);
            }
        }

        /// <summary>
        /// 헬퍼의 대역 몸 만들기를 지금 이 자리에서 실행시킨다.
        ///
        /// 이것이 없으면 아래 scene-dirt 프로브는 한 프레임 이른 상태를 잰다. Open()은
        /// GetWindow&lt;ZepetoStudioHelperWindow&gt;()라서, 창이 이미 열려 있으면 - 이 묶음이 도킹된 창을
        /// 남기므로 두 번째 실행부터는 항상 그렇다 - 새로 만들지 않고 그 인스턴스를 그대로 돌려준다.
        /// OnEnable이 다시 돌지 않으니 그 안의 SyncPreviewBody도 돌지 않고, RefreshAll에는 그 호출이
        /// 없다. 그때 대역 몸을 만드는 유일한 경로는 OnGUI의 자가 치유인데 그쪽은 반드시
        /// EditorApplication.delayCall로 미룬다(OnGUI 안에서 씬을 바꾸는 것이 금지돼 있기 때문이다).
        /// 즉 씬이 더러워지는 시점은 Run()이 반환한 뒤다.
        /// 기록된 결과의 "none - the open scene reads as saved"가 바로 그 증거였다. 프로브는 통과했지만
        /// TryClearSceneDirtiness는 한 번도 불리지 않았다.
        ///
        /// 그래서 미뤄질 일을 여기서 직접 부른다. OnGUI 밖이라 씬을 바꿔도 되는 자리이고, 자가 치유가
        /// 부르는 것과 똑같은 production 메서드를 부르는 것이라 검사가 무엇을 재현하지 않는다.
        /// 돌려주는 값은 실패 사유이고, 정상이면 null이다.
        /// </summary>
        private static string ForcePreviewBodySync()
        {
            MethodInfo sync = typeof(ZepetoStudioHelperWindow).GetMethod("SyncPreviewBody", AnyInstance);
            if (sync == null)
            {
                return "SyncPreviewBody was not found, so the helper's stand-in body was left to the deferred "
                    + "OnGUI self-heal. The scene-dirt line below is then a one-frame-early snapshot and does "
                    + "not exercise TryClearSceneDirtiness.";
            }

            ZepetoStudioHelperWindow window = EditorWindow.GetWindow<ZepetoStudioHelperWindow>();
            if (window == null)
            {
                return "no helper window instance was available to sync, so the scene-dirt line below is a "
                    + "one-frame-early snapshot";
            }

            sync.Invoke(window, null);
            return null;
        }

        /// <summary>
        /// 이 묶음이 열린 씬에 남긴 "저장하지 않은 변경" 표시를 보고하고, 사실이 아니면 내린다.
        ///
        /// 이 묶음은 ScriptableObject.CreateInstance&lt;ZepetoStudioHelperWindow&gt;()를 일곱 번쯤 만들고
        /// 지운다. OnEnable마다 SyncPreviewBody가 대역 몸을 Instantiate하고 OnDisable마다
        /// ClearPreviewBody가 그것을 지우는데, 둘 다 dirty 플래그를 세우고 어느 쪽도 내리지 않는다.
        /// 마지막에 여는 Assets/Playground.unity도 헬퍼 창이 대역 몸을 놓으면서 한 번 더 더럽혀진다 -
        /// 다만 곧바로는 아니다. Open()이 이미 열려 있는 창을 그대로 돌려주면 그 일은 OnGUI의 자가
        /// 치유로 넘어가고, 그쪽은 delayCall로 미루므로 Run()이 반환한 뒤에야 일어난다. 그래서 이
        /// 프로브는 반드시 ForcePreviewBodySync 다음에 와야 한다. 그 호출이 미뤄질 일을 앞당겨서, 여기
        /// 보이는 것이 한 프레임 이른 스냅샷이 아니라 Open() 이후의 안정 상태가 되게 한다.
        /// 이 플래그를 남겨 두면 형제 러너 셋이 그 세션 내내 시작을 거부한다 - 이 묶음이
        /// 리그 내보내기 러너에서 고쳤던 바로 그 고장을 자기가 저지르고 있었다.
        ///
        /// 내려도 되는 근거: 마지막에 연 씬은 방금 디스크에서 읽은 그대로이고, 메모리 쪽에서 다른 것은
        /// 헬퍼의 대역 몸 하나뿐인데 그것은 HideFlags.DontSave라 Unity가 .unity 파일에 절대 직렬화하지
        /// 않는다. 즉 메모리와 디스크는 실제로 일치한다. 디스크에는 아무것도 쓰지 않는다 - 사용자의 씬
        /// 파일을 쓰는 것은 이 러너들에게 금지돼 있다. Check가 아니라 NOTE인 이유는 이 이름들이 기록된
        /// 결과 파일과 비교되기 때문이다.
        /// </summary>
        private static void ReportSceneDirtAfterSuite()
        {
            if (ZepetoSelfTestSceneGuard.FirstDirtyScenePath() == null)
            {
                // 이 줄이 "아무 일도 없었다"가 아니라 "재 봤더니 깨끗했다"로 읽혀야 한다. 대역 몸
                // 만들기는 위에서 이미 강제로 끝냈으므로, 여기가 미뤄진 일을 못 본 것이 아니다.
                Note("scene-dirt", "none - the open scene still reads as saved after the helper's stand-in "
                    + "body was synced, so there was nothing to clear");
                return;
            }

            string detail;
            Note("scene-dirt", ZepetoSelfTestSceneGuard.TryClearSceneDirtiness(out detail)
                ? "the helper window's DontSave stand-in body is all that differed from the file, so the "
                    + "unsaved-changes flag was dropped without writing anything to disk (" + detail + ")"
                : "the open scene still reads as modified (" + detail + "), so the rig export, custom motion "
                    + "and live reload runs will refuse to start until it is saved or reverted");
        }

        /// <summary>
        /// 실행된 검사 개수를 ExpectedCheckCount와 맞춰 보고, 다르면 그 사실 자체를 FAIL 한 줄로 남긴다.
        ///
        /// [QC][Invariant:result_file_is_the_last_real_run]
        /// 검사 하나가 진짜로 실패했을 때 그 실패가 SKIPPED 파일 속으로 사라져서는 안 되므로 결과 파일은
        /// 그대로 쓴다. 대신 개수가 모자란다는 사실을 FAIL로 적어서, 도중에 죽은 실행이 'pass=38 fail=0'
        /// 같은 깨끗한 얼굴로 기록되는 일이 없게 한다. 이 줄은 헤더의 fail= 숫자에도 반영된다. 비교가
        /// 끝난 뒤에 세기 때문이다.
        ///
        /// WriteReport 안이 아니라 밖에 있는 이유: 리포트는 한 실행에서 두 번 쓰인다(검사 직후 한 번,
        /// 뒷정리 NOTE를 붙여서 다시 한 번). 비교가 WriteReport 안에 있으면 첫 번째 쓰기가 붙인 FAIL
        /// 때문에 개수가 하나 늘어, 두 번째 쓰기가 같은 FAIL을 또 붙인다. 그래서 Run()이 딱 한 번 부른다.
        ///
        /// 0개는 여기서 건드리지 않는다. 그 경우는 WriteReport의 SKIPPED 경로가 따로 다루고, 여기서
        /// FAIL을 붙이면 개수가 1이 되어 그 경로가 영영 발동하지 않는다.
        /// </summary>
        private static void VerifyExpectedCheckCount()
        {
            int totalChecks = passCount + failCount;
            if (totalChecks == 0 || totalChecks == ExpectedCheckCount)
            {
                return;
            }

            Fail("suite:check-count", totalChecks + " checks ran, expected " + ExpectedCheckCount
                + ". A short run is not a clean run: the most likely cause is a missing "
                + "Assets/Playground.unity, which skips the 22 real-template / playback-slot / catalog / "
                + "live checks. If a check was added or removed on purpose, update ExpectedCheckCount in "
                + "the same commit as the recorded result file.");
        }

        /// <summary>
        /// 지금까지 모인 결과를 결과 파일에 쓴다. 같은 실행에서 두 번 불려도 되고, 두 번째가 첫 번째를
        /// 통째로 대체한다(덧붙이지 않는다). 그래서 뒷정리 도중 죽어도 첫 번째 판이 그대로 남는다.
        /// </summary>
        private static void WriteReport()
        {
            // [QC][Invariant:result_file_is_the_last_real_run]
            // 아무것도 없는 집계는 결과가 아니다. 검사를 하나도 돌리지 않고 여기 닿는 경로는 전부 버그이고,
            // 추적 중인 기록 위에 "pass=0 fail=0"을 쓰면 그 버그가 깨끗한 통과처럼 보이는 것 아래에
            // 묻힌다. 거부는 자기 파일에 SKIPPED라고 적고 ResultPath는 건드리지 않는다.
            if (passCount + failCount == 0)
            {
                ZepetoSelfTestSceneGuard.WriteSkipRecord(
                    SkipPath,
                    "SKIPPED " + RunnerLabel + " :: the report was reached with zero checks run",
                    "결과가 하나도 없어서 결과 파일을 덮어쓰지 않았습니다. 이전 결과 파일이 마지막 실제 실행 기록입니다.");
                Debug.LogWarning(RunnerLabel + ": 검사가 하나도 실행되지 않아 결과 파일을 그대로 두었습니다. "
                    + SkipPath + "를 확인하세요.");
                return;
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
        }

        // ---------- checks ----------

        private static void TestNoHardcodedAccount()
        {
            Type type = typeof(ZepetoStudioHelperWindow);
            FieldInfo legacyConst = type.GetField("BuiltInDefaultZepetoId", AnyStatic);
            // 아래 텍스트 스캔에 포함되는 검사처럼 보이지만 아니다. 스캔은 "누군가의" 아이디를 다시
            // 들고 온 BuiltInDefaultZepetoId를 놓친다. 이 이름 자체가 없어야 한다는 단언은 따로 필요하다.
            Check("no-builtin-id-const", legacyConst == null, "BuiltInDefaultZepetoId still exists");

            // [QC][Invariant:no_personal_account_data_shipped]
            // 예전에는 파일 하나 - Editor/ZepetoStudioHelperWindow.cs - 만 읽었다. 그래서 아이디가
            // Documentation~/QA_AUDIT.md와 README.md로 배포되고 있는데도, 그리고 아이디 처리 자체가 이미
            // ZepetoStudioHelperWindow.Accounts.cs로 옮겨 간 뒤에도 초록불이었다. 파일 하나로는 이
            // 불변식을 지킬 수 없다. 패키지가 배포하는 것 중 사람이 쓴 텍스트가 든 것을 전부 훑는다.
            List<string> shipped = CollectShippedPackageFiles();
            if (shipped.Count == 0)
            {
                // 아무것도 읽지 않은 스캔은 공허하게 통과한다 - 원래 버그의 모양이 정확히 그것이었다.
                // 파일이 없다는 것은 패키지가 깨끗하다는 뜻이 아니라 검사가 고장 났다는 뜻이다.
                Fail("no-personal-id-in-source", "no shipped files were found under " + PackageRoot
                    + ", so the scan read nothing and proves nothing");
                return;
            }

            string offender = FindPersonalToken(shipped);
            Check("no-personal-id-in-source", offender == null,
                "a shipped package file still contains personal account data :: " + offender);
            Note("no-personal-id-in-source:scanned", shipped.Count + " files under " + PackageRoot);
        }

        /// <summary>
        /// 패키지가 배포하는 파일 중 누군가 타이핑한 텍스트가 들어갈 수 있는 것 전부. 에디터 소스와
        /// 패키지 루트/docs/Documentation~ 아래의 마크다운. 끝의 ~는 그 마지막 폴더를 Unity의 에셋
        /// 데이터베이스에서만 숨긴다. 패키지를 내려받는 사람에게는 그대로 보이므로 함께 훑는다.
        /// </summary>
        private static List<string> CollectShippedPackageFiles()
        {
            List<string> files = new List<string>();
            AddFilesWithExtension(files, PackageRoot + "/Editor", ".cs", SearchOption.AllDirectories);
            AddFilesWithExtension(files, PackageRoot, ".md", SearchOption.TopDirectoryOnly);
            AddFilesWithExtension(files, PackageRoot + "/docs", ".md", SearchOption.AllDirectories);
            AddFilesWithExtension(files, PackageRoot + "/Documentation~", ".md", SearchOption.AllDirectories);
            return files;
        }

        private static void AddFilesWithExtension(List<string> into, string folder, string extension, SearchOption option)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            string[] found = Directory.GetFiles(folder, "*" + extension, option);
            for (int i = 0; i < found.Length; i++)
            {
                // 검색 패턴을 믿지 않고 확장자를 다시 확인한다. Windows의 8.3 짧은 이름 때문에 "*.ext"
                // 패턴이 "X.ext.something"에 걸린다 - Blender 애드온이 쓰는 ".fbx.part"를 fbx 열거
                // 지점 두 곳이 모두 건너뛰어야 하는 것과 똑같은 함정이다 - 게다가 .cs.meta를 전부
                // 끌어들여 봐야 잡음만 는다.
                if (found[i].EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    into.Add(found[i].Replace('\\', '/'));
                }
            }
        }

        /// <summary>
        /// 처음 걸린 줄에 대한 "path:line contains 'token'", 전부 깨끗하면 null. 예전 메시지는 파일도
        /// 토큰도 말해 주지 않았는데, 스캔이 30개 파일을 훑는 마당에 그건 쓸모가 없다 - 게다가 정작
        /// 걸렸어야 할 줄들은 그 스캔이 열어 보지도 않던 파일에 있었다.
        /// </summary>
        private static string FindPersonalToken(List<string> files)
        {
            for (int f = 0; f < files.Count; f++)
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(files[f]);
                }
                catch (Exception exception)
                {
                    // 읽을 수 없는 것은 깨끗한 것이 아니다. 지나치지 말고 보고한다.
                    return files[f] + " could not be read (" + exception.Message + ")";
                }

                for (int l = 0; l < lines.Length; l++)
                {
                    for (int t = 0; t < PersonalTokens.Length; t++)
                    {
                        if (lines[l].IndexOf(PersonalTokens[t], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return files[f] + ":" + (l + 1) + " contains '" + PersonalTokens[t] + "'";
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 0.3.0에서 들어냈던 MCP 브리지가 다시 들어오지 않았는지. 이름 다섯 개가 "없다"는 단언 자체가
        /// 그 제거의 영수증이고, QA_AUDIT.md의 해당 줄이 그 산문 쪽 절반이다. 한 번의 되돌리기로 다섯이
        /// 함께 실패하겠지만 각 이름이 기록된 결과 파일의 한 줄이므로 하나로 합치지 않는다.
        /// </summary>
        /// <summary>
        /// 저장소에 커밋되는 리그 fbx가 무엇을 품고 있는지 본다.
        ///
        /// [QC][Invariant:no_personal_account_data_shipped]
        /// no-personal-id-in-source는 패키지의 "텍스트" 파일만 훑는다. 그런데 계정 이름이 새어 나간
        /// 실제 경로는 텍스트가 아니라 이 바이너리였다. Unity FBX Exporter는 재질의 텍스처 경로를
        /// FbxFileTexture에 절대 경로로 한 번, 상대 경로로 한 번 적는데, ZepetoBaseModel의 텍스처는
        /// prefab 안에 끼워 넣어진 서브애셋이라 그 "경로"가 .prefab 파일 자신이었다. 결과는 두 가지다.
        ///   ① Blender가 리그를 불러올 때마다 그 .prefab을 이미지로 열려다 실패해 콘솔에 ERROR를 찍는다
        ///   ② 그 절대 경로에 내보낸 사람의 계정 이름이 들어간 채 커밋된다
        /// 고친 곳은 RigExport.cs의 StripTexturesForExport다. 여기서는 그 수정이 실제 산출물에
        /// 반영됐는지, 그리고 다시 새어 들어오지 않는지를 파일 자체를 읽어서 확인한다.
        ///
        /// 파일이 없으면 건너뛰지 않고 FAIL이다 - 이 fbx는 3번 카드의 산출물이자 저장소에 추적되는
        /// 파일이고, 없다는 것은 검사가 아무것도 증명하지 못했다는 뜻이다.
        /// </summary>
        private static void TestExportedRigArtifact()
        {
            const string rigPath = "Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx";
            string absolute = Path.GetFullPath(rigPath);

            if (!File.Exists(absolute))
            {
                Fail("rig-artifact:present", rigPath + " is missing, so nothing about the shipped rig was verified");
                Fail("rig-artifact:no-prefab-texture", "no file to read");
                Fail("rig-artifact:no-user-path", "no file to read");
                return;
            }

            Check("rig-artifact:present", true, string.Empty);

            // fbx는 바이너리지만 경로 문자열은 압축되지 않은 채 그대로 들어간다. 그래서 바이트 단위로
            // 찾으면 된다. Latin1로 읽는 이유는 어떤 바이트도 잃지 않고 1바이트=1문자로 대응시키기
            // 위해서다 - UTF8로 읽으면 잘못된 시퀀스가 치환 문자로 뭉개져 검색이 빗나간다.
            string bytes = File.ReadAllText(absolute, System.Text.Encoding.GetEncoding("ISO-8859-1"));

            Check("rig-artifact:no-prefab-texture", bytes.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase) < 0,
                "the exported rig still references a .prefab as a texture, so Blender logs "
                + "\"IMB_load_image_from_memory: unknown file-format\" on every import");

            // 이 기계의 계정 이름을 찾는 것이 아니라 ":\Users\" 라는 모양 자체를 찾는다. 다른 사람이
            // 내보낸 파일이 커밋됐을 때도 걸려야 하기 때문이다. 드라이브 문자는 C:일 필요가 없다.
            int userPath = bytes.IndexOf(":\\Users\\", StringComparison.OrdinalIgnoreCase);
            Check("rig-artifact:no-user-path", userPath < 0,
                "the exported rig embeds a user profile path :: "
                + (userPath < 0 ? string.Empty : bytes.Substring(Math.Max(0, userPath - 1), 40)));
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

            // 예전 빌드는 3.2.12를 정확히 요구했다. 이제는 최소 버전 이상이면 통과해야 하고, 이
            // 프로젝트 자신의 3.2.16 업그레이드가 바로 그 경우다.
            // 이 검사는 installed와 같은 식을 다시 계산하므로 둘은 함께만 실패한다. 그래도 이름은
            // 기록된 기준 파일의 한 줄이라 그대로 두고, 실제로 강제되는 최소 버전 값이 리포트에 남도록
            // 아래 NOTE와 짝지어 둔다.
            MethodInfo compare = typeof(ZepetoStudioHelperWindow).GetMethod("CompareVersions", AnyStatic);
            FieldInfo minField = typeof(ZepetoStudioHelperWindow).GetField("MinimumPackageVersion", AnyStatic);
            string minimum = minField == null ? "3.2.12" : (string)minField.GetValue(null);
            bool atLeastMinimum = compare != null
                && (int)compare.Invoke(null, new object[] { detected, minimum }) >= 0;
            Check("sdk-detect:at-least-minimum", atLeastMinimum,
                "detected '" + detected + "' did not compare >= minimum '" + minimum + "'");
            Note("sdk-detect:minimum", minField == null
                ? "MinimumPackageVersion was not found, the check fell back to the literal " + minimum
                : "MinimumPackageVersion = " + minimum);

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
            Check("id-valid:" + PersonalIdSample, string.IsNullOrEmpty((string)formatError.Invoke(null, new object[] { PersonalIdSample })), PersonalIdSample + " should be accepted");
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
                    // 리플렉션 전용이거나 일부만 로드된 어셈블리는 여기서 볼 것이 없다.
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

            // 헬퍼는 LOADER 위의 모든 컴포넌트를 직렬화 필드 이름으로 훑어 바인딩한다. 그래서 결정적인
            // 질문은 zepetoId / AnimationClip / AnimatorController를 실제로 선언한 ZEPETO 타입이 무엇이냐
            // 하는 것이다.
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
                    // 리플렉션이 되지 않는 어셈블리는 무시한다.
                }
            }

            return null;
        }

        /// <summary>
        /// 이 필드를 선언한 MonoBehaviour들. 답해야 하는 질문은 "어느 SDK 타입이 이 필드를 갖고 있는가"다.
        ///
        /// 어셈블리 이름에 ZEPETO가 들어가는 것만 본다. 그런데 이 하네스 자신의 어셈블리 이름
        /// 'Easy.ZepetoHelper.Tests'에도 들어간다. 그래서 NOTE 세 줄이 SDK 타입 옆에 자기 대역
        /// (ZepetoHelperTestLoader, ZepetoHelperTestIdOwner, ZepetoHelperTestClipOwner)까지 ZEPETO 타입인
        /// 것처럼 보고했다. 이 하네스의 어셈블리 접두사를 빼면 남는 것이 정확히 답이다.
        /// </summary>
        private static List<string> FindMonoBehaviourTypesWithField(string fieldName)
        {
            List<string> owners = new List<string>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                string assemblyName = assemblies[i].GetName().Name;
                if (assemblyName.IndexOf("ZEPETO", StringComparison.OrdinalIgnoreCase) < 0
                    || assemblyName.StartsWith("Easy.ZepetoHelper.", StringComparison.OrdinalIgnoreCase))
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
                    // 리플렉션이 되지 않는 어셈블리는 무시한다.
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

                // 저장된 아이디 목록은 0.7.0에서 제거됐다. 아이디는 직접 입력하고, 그것이 보관되는
                // 유일한 곳은 씬의 LOADER다. 멤버가 정말로 사라졌는지 단언해서, 프로젝트보다 오래 사는
                // EditorPrefs 기반 계정이 되돌리기 한 번으로 조용히 돌아오지 못하게 한다.
                Check("saved-ids:removed",
                    type.GetField("savedZepetoIds", AnyInstance) == null
                        && type.GetMethod("RegisterZepetoId", AnyInstance) == null
                        && type.GetMethod("SetActiveZepetoId", AnyInstance) == null,
                    "the saved-id feature is still present");

                findLoader.Invoke(window, null);

                FieldInfo loaderField = type.GetField("loader", AnyInstance);
                Check("loader-bound", loaderField != null && (loaderField.GetValue(window) as GameObject) != null,
                    "helper did not bind to the LOADER GameObject");

                // 계정 1
                applyId.Invoke(window, new object[] { PersonalIdSample });
                Check("apply-id:first", loaderComponent.zepetoId == PersonalIdSample,
                    "LOADER zepetoId was '" + loaderComponent.zepetoId + "', expected " + PersonalIdSample);

                // 계정 2 - 첫 계정만이 아니라 두 번째 계정도 똑같이 동작해야 한다.
                applyId.Invoke(window, new object[] { "sery_2750" });
                Check("apply-id:second", loaderComponent.zepetoId == "sery_2750",
                    "LOADER zepetoId was '" + loaderComponent.zepetoId + "', expected sery_2750");

                // ZEPETO 앱에서 복사한 그대로, @가 붙은 계정 2.
                applyId.Invoke(window, new object[] { "@sery_2750" });
                Check("apply-id:at-prefix", loaderComponent.zepetoId == "sery_2750",
                    "'@sery_2750' did not normalise to sery_2750, got '" + loaderComponent.zepetoId + "'");

                // 되돌아오기 - 계정 사이를 오가는 왕복은 손실이 없어야 한다.
                applyId.Invoke(window, new object[] { PersonalIdSample });
                Check("apply-id:switch-back", loaderComponent.zepetoId == PersonalIdSample,
                    "switching back failed, got '" + loaderComponent.zepetoId + "'");

                // 형식이 틀린 아이디는 씬의 값을 망가뜨리지 않고 거부돼야 한다.
                applyId.Invoke(window, new object[] { "bad id!" });
                Check("apply-id:reject-invalid", loaderComponent.zepetoId == PersonalIdSample,
                    "invalid id overwrote the LOADER value, got '" + loaderComponent.zepetoId + "'");

                // 새로 만든 창은 아이디를 EditorPrefs가 아니라 씬에서 다시 읽어 와야 한다.
                ZepetoStudioHelperWindow second = ScriptableObject.CreateInstance<ZepetoStudioHelperWindow>();
                try
                {
                    Type secondType = typeof(ZepetoStudioHelperWindow);
                    secondType.GetMethod("FindLoaderAndSerializedFields", AnyInstance).Invoke(second, null);
                    secondType.GetMethod("LoadZepetoIdSettings", AnyInstance).Invoke(second, null);

                    FieldInfo textField = secondType.GetField("zepetoIdText", AnyInstance);
                    string seeded = textField == null ? null : textField.GetValue(second) as string;
                    Check("id-from-scene", seeded == PersonalIdSample,
                        "a new window seeded the id field with '" + (seeded ?? "null")
                        + "', expected the scene value " + PersonalIdSample);
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

                // 탐색은 이름이 아니라 내용 기반이어야 한다. 예전 빌드는 하드코딩된
                // Assets/Playground.unity만 봤다. Playground라는 이름이 "아닌" LOADER 씬을 찾아내는 것이
                // 그 변경의 증거다.
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
        /// SDK는 zepetoId와 AnimationClip/AnimatorController를 두 컴포넌트에 나눠 두고, 템플릿은 그 둘을
        /// 서로 다른 GameObject에 붙여도 된다. 그래도 바인딩은 셋 다 찾아내야 한다.
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

                // 아이디 필드가 클립 필드들과 다른 컴포넌트에 있어도 아이디 적용은 되어야 한다.
                type.GetMethod("ApplyZepetoId", AnyInstance).Invoke(window, new object[] { "sery_2750" });
                Check("split-binding:apply-id", idOwner.zepetoId == "sery_2750",
                    "id apply failed on a split layout, got '" + idOwner.zepetoId + "'");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            // 형제 배치(컴포넌트가 별도의 루트 오브젝트에 있는 경우)는 씬 전체를 훑는 단계에서 풀려야 한다.
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
        /// [AUDIT][Risk:Critical] 표시가 붙은 헬퍼 쪽 가드 세 가지를 지킨다. 셋 다 "없어도 아무 검사가
        /// 빨개지지 않는" 상태였고, 그래서 조용히 사라질 수 있는 코드였다.
        ///   1. .fbx.part 건너뛰기 - Blender 애드온과의 양방향 계약
        ///   2. 아바타 오염 게이트와 그 수리 경로
        ///   3. CanEnterPlayMode의 차단 술어
        /// 템플릿 씬이 없어도 돌 수 있도록 일부러 TestRealTemplateScene보다 앞에 둔다.
        /// </summary>
        private static void TestCriticalGuards()
        {
            // 셋 다 헬퍼의 private 멤버를 리플렉션으로 부른다. 시그니처가 바뀌면 Invoke가 예외를
            // 던지는데, 그것이 Run()의 바깥 try까지 올라가면 남은 검사가 통째로 실행되지 않는다.
            // 여기서 잡아 두면 고장이 FAIL 한 줄로 남고 나머지 묶음은 계속 돈다.
            RunGuarded(TestPartSuffixSkip, "live-part");
            RunGuarded(TestAvatarPoisoningGuard, "avatar-guard");
            RunGuarded(TestPlayModeGate, "play-gate");
        }

        private static void RunGuarded(Action check, string family)
        {
            try
            {
                check();
            }
            catch (Exception exception)
            {
                Fail(family + ":crashed", exception.ToString());
            }
        }

        /// <summary>
        /// [AUDIT][Risk:Critical][Scope:live_reload]
        /// Blender 애드온은 "&lt;name&gt;.fbx.part"로 쓴 다음 이름을 바꾼다. Windows의 8.3 짧은 이름 때문에
        /// "*.fbx" 글롭이 그 부분 파일에도 걸리고, 절반만 쓰인 fbx를 임포트하면 망가진 클립이 구워진다.
        /// 그래서 열거 지점 두 곳(TryFindNewestMotionFile, ConfigureMotionFolderForLivePreview)이 모두
        /// .part를 건너뛴다.
        ///
        /// 첫 지점은 동작으로 확인한다. 감시 폴더에 방금 만든 - 즉 가장 최신인 - .part 파일을 두고
        /// 그것이 선택되지 않는지 본다. 두 번째 지점은 폴더의 모든 fbx 임포터를 다시 쓰는 함수라 자기
        /// 테스트가 불러서는 안 되므로 소스로만 확인하고, 그것이 이 묶음에서 유일하게 동작이 아니라
        /// 텍스트를 보는 단언이다. 그래서 최소한 "어느 메서드 몸통 안에서" 세는지는 지킨다 -
        /// live-part:both-enumeration-sites 쪽 주석 참고.
        /// </summary>
        private static void TestPartSuffixSkip()
        {
            Type type = typeof(ZepetoStudioHelperWindow);
            FieldInfo watchRootField = type.GetField("LiveWatchRoot", AnyStatic);
            MethodInfo findNewest = type.GetMethod("TryFindNewestMotionFile", AnyStatic);
            string watchRoot = watchRootField == null ? null : watchRootField.GetValue(null) as string;

            if (findNewest == null || string.IsNullOrEmpty(watchRoot))
            {
                Fail("live-part:newest-skips-part",
                    "TryFindNewestMotionFile/LiveWatchRoot not found, so the .part skip could not be exercised");
            }
            else
            {
                string absoluteFolder = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", watchRoot));
                string probe = Path.Combine(absoluteFolder, "__selftest_part_probe.fbx.part");
                try
                {
                    Directory.CreateDirectory(absoluteFolder);
                    // 애드온이 반쯤 쓴 내보내기와 같은 모양이면 충분하다. 내용은 읽히지 않는다 -
                    // 건너뛰기는 이름만 보고 결정되고, 그것이 요점이다.
                    File.WriteAllText(probe, "partial export, must never be picked");

                    object[] args = new object[4];
                    bool found = (bool)findNewest.Invoke(null, args);
                    string picked = args[0] as string;
                    Check("live-part:newest-skips-part",
                        found && !string.IsNullOrEmpty(picked)
                            && !picked.EndsWith(".part", StringComparison.OrdinalIgnoreCase),
                        "the newest file in " + watchRoot + " was a half-written '.fbx.part' and the watcher "
                        + "picked it: found=" + found + " path='" + picked + "'");
                }
                catch (Exception exception)
                {
                    Fail("live-part:newest-skips-part", "the probe could not be placed: " + exception.Message);
                }
                finally
                {
                    try { File.Delete(probe); } catch { }
                }
            }

            // 두 번째 지점은 소스로 확인하되, 파일 전체를 세지는 않는다. 그 형태는 검사 이름이 주장하는
            // 것보다 훨씬 약했다: 한쪽 건너뛰기가 사라지고 다른 쪽에 두 개가 남아도 초록이고, 지워진
            // 자리에 그 문자열이 든 주석 한 줄만 남아도 초록이었다. 그래서 두 메서드의 몸통을 각각 잘라
            // 내고, 주석만 있는 줄을 걷어낸 다음, 양쪽에서 하나씩 나오는지를 본다. 이제 이 이름은 실제로
            // "열거 지점 두 곳 모두"를 뜻한다.
            string liveSource = StripCommentOnlyLines(ReadPackageSource(LivePreviewSourcePath));
            int newestSkips = CountOccurrences(
                ExtractMethodBody(liveSource, "bool TryFindNewestMotionFile("), PartSkipNeedle);
            int folderSkips = CountOccurrences(
                ExtractMethodBody(liveSource, "int ConfigureMotionFolderForLivePreview("), PartSkipNeedle);
            Check("live-part:both-enumeration-sites", newestSkips >= 1 && folderSkips >= 1,
                "the '.part' skip is not in both enumeration sites of " + LivePreviewSourcePath
                + ": TryFindNewestMotionFile has " + newestSkips + ", ConfigureMotionFolderForLivePreview has "
                + folderSkips + ". Both are the add-on's write-then-rename contract; dropping either imports a "
                + "partial fbx. This counts '" + PartSkipNeedle + "' inside each method body with comment-only "
                + "lines removed, so 0 can also mean the method signature moved or its braces did not balance. "
                + "If the literal becomes a named constant, PartSkipNeedle must change in the same commit.");
        }

        /// <summary>
        /// [AUDIT][Risk:Critical][Scope:avatar_poisoning]
        /// sourceAvatar를 대입하면 ZEPETO의 humanDescription이 대상의 .meta에 쓰인다. 대상의 뼈대가 그
        /// 이름들을 갖고 있지 않으면 그 에셋은 오염되고, 이후 모든 리임포트가
        /// "Avatar creation failed: Transform 'hips' for human bone 'Hips' not found"로 실패한다. 나쁜
        /// 매핑이 이제 이 실행이 아니라 에셋의 일부가 됐기 때문이다. Assets/CustomMotions의 두 파일이
        /// 정확히 그 상태였고, .meta를 손으로 지우는 것이 유일한 탈출구였다.
        ///
        /// 그래서 복사는 게이트를 통과해야만 하고, 이미 오염된 에셋을 되살리는 수리 경로도 있어야 한다.
        /// 여기서는 게이트가 "증명하지 못하면 거부한다"는 쪽으로 기울어 있는지와 수리 경로가 존재하는지를
        /// 본다. 실제 fbx의 .meta를 다시 쓰는 것은 자기 테스트가 할 일이 아니다.
        /// </summary>
        private static void TestAvatarPoisoningGuard()
        {
            Type type = typeof(ZepetoStudioHelperWindow);

            MethodInfo canCopy = type.GetMethod("CanCopyRigAvatarTo", AnyStatic);
            if (canCopy == null)
            {
                Fail("avatar-guard:refuses-without-map", "CanCopyRigAvatarTo not found");
            }
            else
            {
                // 사람 뼈 매핑이 없는 Avatar로는 아무것도 증명할 수 없다. 그럴 때 기본값은 거부여야 한다.
                object[] args = new object[] { "Assets/ZepetoHelper/Rig/ZepetoBaseModel.fbx", null, null };
                bool allowed = (bool)canCopy.Invoke(null, args);
                Check("avatar-guard:refuses-without-map",
                    !allowed && !string.IsNullOrEmpty(args[2] as string),
                    "CanCopyRigAvatarTo allowed the copy with no source Avatar (allowed=" + allowed
                    + ", reason='" + args[2] + "'). Copying an unverified map is what poisons the .meta.");
            }

            MethodInfo countMissing = type.GetMethod("CountMissingHumanBones", AnyStatic);
            if (countMissing == null)
            {
                Fail("avatar-guard:counts-missing-bones", "CountMissingHumanBones not found");
            }
            else
            {
                // 게이트가 기대는 산술 그 자체. 매핑이 요구하는 이름 중 모델에 없는 것을 세고, 이름이
                // 빈 항목은 매핑되지 않은 선택 뼈이므로 "없는 뼈"가 아니다.
                HumanBone present = new HumanBone();
                present.boneName = "hips";
                present.humanName = "Hips";
                HumanBone missing = new HumanBone();
                missing.boneName = "upperArm_R";
                missing.humanName = "RightUpperArm";
                HumanBone optional = new HumanBone();
                optional.boneName = string.Empty;
                optional.humanName = "Jaw";

                HashSet<string> modelBones = new HashSet<string>(StringComparer.Ordinal);
                modelBones.Add("hips");

                object[] args = new object[]
                {
                    new[] { present, missing, optional },
                    modelBones,
                    null,
                    null,
                };
                countMissing.Invoke(null, args);
                int missingCount = (int)args[2];
                string firstMissing = args[3] as string;
                Check("avatar-guard:counts-missing-bones",
                    missingCount == 1 && firstMissing == "upperArm_R",
                    "expected 1 missing bone (upperArm_R) against a model that only has 'hips', got "
                    + missingCount + " / '" + firstMissing + "'. An empty boneName is an unmapped optional "
                    + "bone and must not count as missing, or every rig would look foreign.");
            }

            // 게이트만으로는 아직 오염되지 않은 fbx만 지킨다. .meta에 이미 남의 humanDescription이 든
            // 에셋은 수리해야 하고, 이 버튼이 사용자에게 있는 유일한 수리 경로다. 없으면 남은 방법은
            // ".meta를 손으로 지우세요"뿐인데 아무도 그것을 찾아내지 못한다.
            Check("avatar-guard:repair-available",
                type.GetMethod("NeedsForeignAvatarMapCleared", AnyStatic) != null
                    && type.GetMethod("ClearForeignAvatarMap", AnyStatic) != null,
                "the un-poison path is gone. Refusing the copy only protects an asset that is not poisoned "
                + "yet; an asset whose .meta already holds a foreign human map has no other way back.");
        }

        /// <summary>
        /// [AUDIT][Risk:Critical][Scope:play_stability]
        /// CanEnterPlayMode는 Play 버튼 하나짜리 게이트가 아니다. RequestPlayMode와
        /// RequestLivePreviewPlay가 에셋을 건드리기 "전에" 묻는 곳이고, 카드의 활성/비활성도 여기서
        /// 나온다. HardBlock 스냅샷에서 통과시키면 이 도구가 존재하는 이유인 그 크래시로 그대로 들어간다.
        ///
        /// 두 방향을 다 본다. 막혀야 할 때 막는가, 그리고 - 에디터가 조용할 때 - 열려야 할 때 여는가.
        /// 뒤쪽은 게이트가 영영 거부하게 되는 반대 방향 고장을 잡는다. 컴파일/임포트 중일 수 있는
        /// 나머지 세 술어는 검사 시점의 실제 값과 함께 비교해서 이 검사가 깜빡이지 않게 한다.
        /// </summary>
        private static void TestPlayModeGate()
        {
            Type type = typeof(ZepetoStudioHelperWindow);
            Type snapshotType = type.GetNestedType("SafetySnapshot", BindingFlags.NonPublic);
            MethodInfo canEnter = type.GetMethod("CanEnterPlayMode", AnyStatic);
            MethodInfo blocked = snapshotType == null ? null : snapshotType.GetMethod("Blocked", AnyStatic);
            MethodInfo ok = snapshotType == null ? null : snapshotType.GetMethod("Ok", AnyStatic);

            if (canEnter == null || blocked == null || ok == null)
            {
                Fail("play-gate:blocked-refuses", "CanEnterPlayMode/SafetySnapshot were not found");
                Fail("play-gate:allows-when-quiet", "CanEnterPlayMode/SafetySnapshot were not found");
                return;
            }

            object blockedSnapshot = BuildSnapshot(blocked);
            Check("play-gate:blocked-refuses", !(bool)canEnter.Invoke(null, new object[] { blockedSnapshot }),
                "a HardBlock safety snapshot did not stop Play. The snapshot is built from the editor log, so "
                + "this is the only thing standing between a known-bad state and the crash it produces.");

            object okSnapshot = BuildSnapshot(ok);
            bool quiet = !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && !EditorApplication.isPlayingOrWillChangePlaymode;
            Check("play-gate:allows-when-quiet",
                (bool)canEnter.Invoke(null, new object[] { okSnapshot }) == quiet,
                "an Ok snapshot did not agree with the editor's own state (isCompiling="
                + EditorApplication.isCompiling + ", isUpdating=" + EditorApplication.isUpdating
                + "). A gate that refuses on a clean snapshot disables every Play button in the helper.");
        }

        /// <summary>
        /// SafetySnapshot 팩토리를 그 시점의 파라미터 목록에 맞춰 부른다.
        ///
        /// 인자를 손으로 나열하지 않는 이유: SafetySnapshot은 헬퍼의 private 중첩 struct이고 그 팩토리의
        /// 인자는 실제로 바뀌어 왔다(matchedKeyword와 logLastWriteUtc가 빠졌다). 개수를 박아 두면 그런
        /// 변경이 TargetParameterCountException이 되어 이 검사가 아니라 검사 묶음 전체를 죽인다. 값은
        /// 중요하지 않다 - CanEnterPlayMode가 보는 것은 팩토리가 정하는 Level뿐이다.
        /// </summary>
        private static object BuildSnapshot(MethodInfo factory)
        {
            ParameterInfo[] parameters = factory.GetParameters();
            object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                args[i] = parameterType == typeof(string)
                    ? "self test"
                    : (parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null);
            }

            return factory.Invoke(null, args);
        }

        /// <summary>
        /// 패키지 소스 한 파일의 텍스트, 읽지 못하면 빈 문자열. 소스 스캔은 no-personal-id-in-source와
        /// 같은 모양이다. 부를 수 없는 함수 - 폴더의 모든 fbx 임포터를 다시 쓰는 것 같은 - 안의 가드는
        /// 이렇게밖에 확인할 수 없다.
        /// </summary>
        private static string ReadPackageSource(string assetPath)
        {
            try
            {
                return File.Exists(assetPath) ? File.ReadAllText(assetPath) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 줄 전체가 주석인 줄만 없앤다. 코드 줄 뒤에 붙은 주석은 그대로 둔다.
        ///
        /// 뒤에 붙은 주석까지 자르려면 "//"를 찾아야 하는데, 문자열 리터럴 안의 "//"를 주석으로 오인하면
        /// 그 줄의 진짜 코드를 지워 버린다 - 소스를 읽어 판정하는 검사에서 그것은 거짓 음성이 아니라
        /// 거짓 양성 쪽으로도 갈 수 있는 사고다. 이 헬퍼가 막으려는 것은 "지워진 가드 자리에 남은 주석이
        /// 검사를 초록으로 유지하는 것"이고, 이 코드베이스의 주석은 전부 줄 단위라 이걸로 충분하다.
        /// </summary>
        private static string StripCommentOnlyLines(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            string[] lines = source.Split('\n');
            StringBuilder kept = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    // 줄 자체는 지우되 개행은 남긴다. 아래 중괄호 세기는 위치가 아니라 짝만 보므로
                    // 상관없지만, 사람이 이 결과를 찍어 볼 때 줄 번호가 밀리지 않는 편이 낫다.
                    kept.Append('\n');
                    continue;
                }

                kept.Append(lines[i]).Append('\n');
            }

            return kept.ToString();
        }

        /// <summary>
        /// `signature`로 시작하는 메서드의 몸통을 중괄호 짝을 세어 잘라 낸다. 찾지 못하거나 짝이 맞지
        /// 않으면 빈 문자열이고, 그러면 호출자는 0을 세어 빨개진다 - 못 읽은 것을 통과로 넘기는 것보다
        /// 낫다.
        ///
        /// 이렇게까지 하는 이유는 파일 전체를 세는 것과 "메서드 안에 있는가"가 다른 질문이기 때문이다.
        /// 전자는 한쪽 열거 지점이 통째로 사라지고 다른 쪽에 사본이 둘 남아도 만족된다.
        /// 주석 줄을 걷어낸 소스를 받는 것을 전제로 한다. 그래야 문서 주석 안의 중괄호가 짝 세기를
        /// 어긋내지 못한다. 문자열 리터럴 안의 중괄호는 여전히 이 세기를 속일 수 있는데, 그때는 짝이
        /// 맞지 않아 빈 문자열이 나오고 검사는 빨개지는 쪽으로 넘어진다.
        /// </summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.Empty;
            }

            int at = source.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0)
            {
                return string.Empty;
            }

            int open = source.IndexOf('{', at);
            if (open < 0)
            {
                return string.Empty;
            }

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(open, i - open + 1);
                    }
                }
            }

            return string.Empty;
        }

        private static int CountOccurrences(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }

            int count = 0;
            int at = text.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>
        /// 프로젝트에 있을 때, 공식 ZEPETO Studio 템플릿 씬을 상대로 하는 끝에서 끝까지 검사. 헬퍼가
        /// 대역이 아니라 진짜 SDK 컴포넌트에 바인딩한다는 것을 증명하는 유일한 검사다. 템플릿 씬이
        /// 에디터에 열린 채로 남도록 마지막에 돌린다.
        ///
        /// 이 씬이 없으면 아래에서 22개 검사가 통째로 건너뛰어진다. 그 부분 실행은 WriteReport의
        /// ExpectedCheckCount 비교가 FAIL로 잡는다. 여기 NOTE는 이유를 적어 둘 뿐이다.
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

            // SDK가 실제로 노출하는 직렬화 프로퍼티를 전부 찍어 둔다. 바인딩 이름을 짐작이 아니라 현실에
            // 대고 확인할 수 있게 하려는 것이다.
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

                // 곧 덮어쓸 아이디는 사용자 자신의 작업 씬에 든 진짜 아이디다. 잡아 두고 무슨 일이
                // 있어도 되돌린다. 계정 전환 도중에 단언이 예외를 던지면 작성자의 아이디가
                // Assets/Playground.unity의 LOADER에 그대로 남곤 했다.
                string originalId = idProp.stringValue;
                try
                {
                    applyId.Invoke(window, new object[] { PersonalIdSample });
                    Check("real-template:apply-first", PersonalIdSample.Equals(currentId.Invoke(window, null)),
                        "first account did not stick, got '" + currentId.Invoke(window, null) + "'");

                    applyId.Invoke(window, new object[] { "sery_2750" });
                    Check("real-template:apply-second", "sery_2750".Equals(currentId.Invoke(window, null)),
                        "second account did not stick, got '" + currentId.Invoke(window, null) + "'");

                    applyId.Invoke(window, new object[] { PersonalIdSample });
                    Check("real-template:switch-back", PersonalIdSample.Equals(currentId.Invoke(window, null)),
                        "switching back failed, got '" + currentId.Invoke(window, null) + "'");
                }
                finally
                {
                    RestoreStringProperty(window, "zepetoIdProperty", originalId, "real-template:id-restored");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            // 진짜 Contents 폴더에서 의상 프리팹을 찾아낸다. Assets/Contents/TRANSPARENT_1은 "예시일 뿐"
            // 처럼 보이지만 이 검사가 그 폴더의 프리팹 존재에 걸려 있다.
            string[] prefabGuids = AssetDatabase.IsValidFolder("Assets/Contents")
                ? AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Contents" })
                : new string[0];
            Check("real-template:outfit-prefab", prefabGuids.Length > 0,
                "no outfit prefab found under Assets/Contents");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                Note("real-template:outfit", AssetDatabase.GUIDToAssetPath(prefabGuids[i]));
            }

            // 헬퍼가 2번 단계에서 보여 주는 SDK 모션 목록.
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
        /// 카탈로그는 SDK 모션과 사용자 모션을 합쳐야 하고, 무엇보다 진짜 모션과 SDK가 함께 배포하는
        /// 한 프레임짜리 포즈를 구분해야 한다. 아바타가 고장 난 것처럼 보였던 원인이 정확히 그 혼동이다.
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

                // SDK 클립은 전부 Humanoid다. Blender/Mixamo 경로 전체가 그 전제 위에 서 있다.
                AnimationClip pose = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Packages/zepeto.studio/resources/Animation/A_pose.anim");
                AnimationClip motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Packages/zepeto.studio/resources/Animation/Videobooth_282.anim");

                Check("catalog:sdk-is-humanoid", motion != null && motion.isHumanMotion,
                    "SDK motion is not a Humanoid clip, retargeting assumption is wrong");
                Check("catalog:pose-detected", pose != null && pose.length <= PoseMaxLength(),
                    "A_pose was expected to be a single-frame pose (<= " + PoseMaxLength().ToString("0.00") + "s)");

                // 처음 들어왔을 때의 기본 선택이 정지 포즈여서는 안 된다.
                FieldInfo selectedField = type.GetField("selectedAnimationIndex", AnyInstance);
                int selected = (int)selectedField.GetValue(window);
                MethodInfo getSelected = type.GetMethod("GetSelectedPackageAnimation", AnyInstance);
                AnimationClip selectedClip = getSelected.Invoke(window, null) as AnimationClip;
                Check("catalog:default-not-pose",
                    selectedClip != null && selectedClip.length > PoseMaxLength(),
                    "default selection is '" + (selectedClip == null ? "NULL" : selectedClip.name) + "', a static pose");
                Note("catalog:default", (selectedClip == null ? "NULL" : selectedClip.name) + " index=" + selected);

                // 포즈는 조용히 연결되는 대신 이유와 함께 거부돼야 한다.
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
        /// 아바타가 재생하는 것은 AnimatorOverrideController 슬롯이 가리키는 클립이지
        /// PlaygroundController.AnimationClip에 든 값이 아니다. SDK는 그 슬롯을 A_pose(0.04초)에 매핑해
        /// 배포하므로, 필드만 쓰는 헬퍼는 아바타를 가만히 서 있게 만든다. 여기서 진짜 재생 경로를
        /// 못박아 둔다.
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

                // 아래는 사용자의 작업 씬과 프로젝트 에셋을 함께 다시 배선한다. AssignAnimationClip은
                // ApplyClipToOverrideController로 끝나고 그것이 AssetDatabase.SaveAssets를 부르므로,
                // 오버라이드 표는 바뀌는 순간 디스크에 올라간다. 손대기 전에 LOADER 참조 둘을 스냅샷한다.
                AnimationClip clipBefore = GetObjectReference(window, "animationClipProperty") as AnimationClip;
                UnityEngine.Object controllerBefore = GetObjectReference(window, "animatorControllerProperty");

                MethodInfo ensureLocal = type.GetMethod("EnsureLocalAnimatorController", AnyInstance);
                object[] ensureArgs = new object[] { null };
                bool localOk = (bool)ensureLocal.Invoke(window, ensureArgs);
                Check("playback-slot:local-controller", localOk,
                    "could not create a project-local AnimatorOverrideController: " + ensureArgs[0]);

                // 지금에서야 스냅샷하는 이유: 바로 위의 로컬 복사 단계가 "어느" 컨트롤러의 표가 다시
                // 쓰일지를 정한다. 더 일찍 떴으면 엉뚱한 에셋을 되돌리게 된다.
                AnimatorOverrideController touchedController =
                    GetObjectReference(window, "animatorControllerProperty") as AnimatorOverrideController;
                List<KeyValuePair<AnimationClip, AnimationClip>> overridesBefore = null;
                if (touchedController != null)
                {
                    overridesBefore = new List<KeyValuePair<AnimationClip, AnimationClip>>(touchedController.overridesCount);
                    touchedController.GetOverrides(overridesBefore);
                }

                try
                {
                    MethodInfo assign = type.GetMethod("AssignAnimationClip", AnyInstance);
                    bool assigned = (bool)assign.Invoke(window, new object[] { motion, false });
                    Check("playback-slot:assign", assigned, "AssignAnimationClip returned false");

                    MethodInfo getPlayback = type.GetMethod("GetPlaybackClip", AnyInstance);
                    AnimationClip playback = getPlayback.Invoke(window, null) as AnimationClip;

                    Check("playback-slot:updated", playback == motion,
                        "override slot holds '" + (playback == null ? "NULL" : playback.name) + "', expected " + motion.name);
                    // 0.1f를 그대로 쓰지 않고 헬퍼의 StaticPoseMaxLength를 읽는다. "포즈인가"의 정의는
                    // 한 곳에만 있어야 하고, 헬퍼가 그 문턱을 옮기면 이 검사도 따라가야 한다.
                    Check("playback-slot:not-a-pose", playback != null && playback.length > PoseMaxLength(),
                        "override slot still holds a static pose (<= " + PoseMaxLength().ToString("0.00")
                        + "s), the avatar would not move");
                    Note("playback-slot:value", playback == null ? "NULL" : playback.name + " " + playback.length.ToString("0.00") + "s");
                }
                finally
                {
                    RestorePlaybackState(window, touchedController, overridesBefore, clipBefore, controllerBefore);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// 헬퍼가 "정지 포즈"라고 부르는 길이의 문턱. 헬퍼의 StaticPoseMaxLength를 읽고, 없으면 그
        /// 상수의 현재 값과 같은 0.1f로 물러선다.
        /// </summary>
        private static float PoseMaxLength()
        {
            FieldInfo field = typeof(ZepetoStudioHelperWindow).GetField("StaticPoseMaxLength", AnyStatic);
            object value = field == null ? null : field.GetValue(null);
            return value is float ? (float)value : 0.1f;
        }

        /// <summary>
        /// 작업 씬과 오버라이드 컨트롤러를 playback-slot 검사가 발견했던 상태로 되돌린다.
        ///
        /// 오버라이드 표를 먼저 되돌린다. 이미 디스크에 쓰인 유일한 부분이기 때문이다. 그리고 일부러
        /// SDK가 거기 매핑해 두었던 것 - 보통 A_pose - 을 그대로 되살린다. 포즈가 바람직해서가 아니라
        /// 그것이 검사 전 상태이기 때문이다.
        /// </summary>
        private static void RestorePlaybackState(
            ZepetoStudioHelperWindow window,
            AnimatorOverrideController touchedController,
            List<KeyValuePair<AnimationClip, AnimationClip>> overridesBefore,
            AnimationClip clipBefore,
            UnityEngine.Object controllerBefore)
        {
            if (touchedController != null && overridesBefore != null)
            {
                touchedController.ApplyOverrides(overridesBefore);
                EditorUtility.SetDirty(touchedController);
                AssetDatabase.SaveAssets();
                Note("playback-slot:overrides-restored",
                    overridesBefore.Count + " slot(s) on " + touchedController.name);
            }

            RestoreObjectProperty(window, "animationClipProperty", clipBefore, "playback-slot:clip-restored");
            RestoreObjectProperty(window, "animatorControllerProperty", controllerBefore, "playback-slot:controller-restored");
        }

        private static SerializedProperty GetWindowProperty(ZepetoStudioHelperWindow window, string fieldName)
        {
            FieldInfo field = typeof(ZepetoStudioHelperWindow).GetField(fieldName, AnyInstance);
            return field == null ? null : field.GetValue(window) as SerializedProperty;
        }

        private static UnityEngine.Object GetObjectReference(ZepetoStudioHelperWindow window, string fieldName)
        {
            SerializedProperty property = GetWindowProperty(window, fieldName);
            return property == null ? null : property.objectReferenceValue;
        }

        /// <summary>
        /// 값을 헬퍼 자신의 SerializedProperty를 통해 그대로 되돌려 쓴다.
        ///
        /// 검사 전에 잡아 둔 것을 재사용하지 않고 창에서 프로퍼티를 다시 읽는다. 헬퍼는 자기
        /// SerializedObject가 낡을 때마다 다시 바인딩하므로, 앞서 잡아 둔 참조는 아무도 갱신하지 않는
        /// SerializedObject를 가리키고 있을 수 있다. Update()를 먼저 부르는 이유는 컴포넌트의 다른 모든
        /// 필드에 대한 낡은 스냅샷을 재생하는 대신 현재 상태 위에 쓰기 위해서다.
        /// </summary>
        private static void RestoreStringProperty(ZepetoStudioHelperWindow window, string fieldName, string value, string resultName)
        {
            SerializedProperty property = GetWindowProperty(window, fieldName);
            if (property == null || property.serializedObject == null || property.serializedObject.targetObject == null)
            {
                Fail(resultName, "could not rebind " + fieldName + " to restore '" + value + "'");
                return;
            }

            property.serializedObject.Update();
            property.stringValue = value;
            property.serializedObject.ApplyModifiedProperties();
            Note(resultName, "'" + value + "'");
        }

        // ApplyZepetoId / AssignAnimationClip을 거치지 않는 것은 의도다. 둘 다 이 복원이 반드시 쓸 수
        // 있어야 하는 값을 거부한다. ApplyZepetoId는 비었거나 형식이 틀린 아이디를 거부하는데, 손대지
        // 않은 템플릿 LOADER가 들고 있는 값이 정확히 그것이다. AssignAnimationClip은 null을 거부한다.
        // 그것들을 통과시키면 검사가 넣은 값이 씬에 조용히 남는다.
        // AssignAnimationClip을 우회해도 재생 슬롯과 클립 필드가 어긋나지는 않는다.
        // RestorePlaybackState가 AnimatorOverrideController의 표를 먼저 되돌려서 슬롯과 필드를 함께
        // 되돌리기 때문이고, 그것이 바로 AssignAnimationClip이 지키려고 존재하는 불변식이다.
        private static void RestoreObjectProperty(ZepetoStudioHelperWindow window, string fieldName, UnityEngine.Object value, string resultName)
        {
            SerializedProperty property = GetWindowProperty(window, fieldName);
            if (property == null || property.serializedObject == null || property.serializedObject.targetObject == null)
            {
                Fail(resultName, "could not rebind " + fieldName + " to restore '"
                    + (value == null ? "NULL" : value.name) + "'");
                return;
            }

            property.serializedObject.Update();
            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedProperties();
            Note(resultName, value == null ? "NULL" : value.name);
        }

        /// <summary>
        /// 라이브 리로드 루프는 불변식 하나 위에 서 있다. EditorUtility.CopySerialized는 AnimationClip
        /// 에셋의 GUID와 instanceID를 유지한 채 "내용"만 갈아치우고, 그래서
        /// AnimatorOverrideController의 참조가 살아남아 돌고 있는 Animator가 rebind 없이 새 모션을
        /// 집어 든다. 그것이 거짓이면 설계 전체가 "매번 컨트롤러를 다시 바인딩한다"로 무너지므로 가장
        /// 먼저 단언한다.
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

                // CopySerialized는 소스의 이름까지 가져오므로 라이브 대상 클립은 자기 이름을 잃는다.
                // 그래서 프로덕션 쪽 TryPushMotionToLiveAvatar도 여기와 똑같이 이름을 잡아 두었다가
                // 되돌린다. 아래 live:name-restored는 그 "필요성"을 재현할 뿐 프로덕션의 그 줄을
                // 실행하지는 않는다. TryPushMotionToLiveAvatar는 fbx를 임포트하고 사용자의
                // LiveFromBlender.anim을 덮어쓰므로 편집 모드 자기 테스트가 부를 수 있는 물건이 아니다.
                // 그 줄을 실제로 지키는 것은 Play 안에서 도는 ZepetoLiveReloadRun이다.
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

                    // [QC][UnitySerialization]
                    // wrapMode만 보면 안 된다. ApplyClipLoopSetting 자신의 주석이 이미 그렇게 말한다.
                    // 임포트된 .anim의 루프 상태에서 Unity가 실제로 읽는 것은
                    // m_AnimationClipSettings/m_LoopTime이고, 헬퍼의 GetClipLoopTime도 그 필드를
                    // AnimationUtility.GetAnimationClipSettings().loopTime으로 되읽는다. wrapMode만
                    // 단언하던 동안에는 m_LoopTime을 쓰는 블록을 통째로 지워도 60/60이 그대로 나왔다 -
                    // 그동안 라이브로 갈아 낀 모션과 클립 조정 결과가 전부 루프를 잃는데도.
                    AnimationClipSettings loopSettings = AnimationUtility.GetAnimationClipSettings(loaded);
                    Check("live:loop-applied",
                        loaded.wrapMode == WrapMode.Loop && loopSettings != null && loopSettings.loopTime,
                        "loop flag did not apply: wrapMode=" + loaded.wrapMode + ", m_LoopTime="
                        + (loopSettings == null ? "(no settings)" : loopSettings.loopTime.ToString())
                        + ". m_LoopTime is the one Unity reads back, so the motion would play once and freeze.");
                }

                // guid는 일부러 적지 않는다. 프로브 에셋은 매 실행마다 지웠다 다시 만들어져서 GUID가
                // 새로 나오고, 그러면 추적 중인 결과 파일이 절대 같은 내용으로 재현되지 않는다.
                // CopySerialized를 가로질러 GUID가 유지된다는 사실은 이미 live:guid-preserved가 실행
                // 안에서 전후로 비교해 단언한다.
                Note("live:probe", "len=" + loaded.length.ToString("0.00") + "s");
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
