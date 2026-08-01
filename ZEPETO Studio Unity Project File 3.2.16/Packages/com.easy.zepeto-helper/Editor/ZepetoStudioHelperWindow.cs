using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 창의 뼈대: 모든 partial이 공유하는 상태, Unity 생명주기, 그리고 최상위 repaint 진입점.
    /// 다른 partial이 참조하는 상수와 규칙의 원본은 이 파일에 있다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow : EditorWindow
    {
        // [QC][Invariant:no_personal_defaults]
        // 패키지에는 내장 ZEPETO 계정이 없다. 아이디는 사용자가 직접 입력하고 scene의 LOADER 안에만 존재하며,
        // 어떤 아바타가 로드되는지는 오직 그 값이 결정한다.
        //
        // 아래 키들은 아이디를 프로젝트 사이에서 기억하던 0.5.x 이하 버전이 남긴 것이다. LoadZepetoIdSettings가
        // 한 번 지워 주기 때문에, 기능이 제거된 뒤에도 예전 계정이 되살아나지 않는다.
        private static readonly string[] ObsoleteZepetoIdPrefKeys =
        {
            "com.easy.zepeto-helper.activeZepetoId",
            "com.easy.zepeto-helper.savedZepetoIds",
            "com.easy.zepeto-helper.defaultZepetoId"
        };

        private const int MaxZepetoIdLength = 64;

        // ------------------------------------------------------------------- SDK 소유 경로 (읽기 전용)
        // Packages/ 아래는 SDK가 소유하고 Library/PackageCache/를 통해 이 머신의 모든 프로젝트가 공유한다.
        // 헬퍼는 여기에 절대 쓰지 않는다. 여기서 고른 클립은 반드시 AnimationCopyRoot로 복사한 뒤 편집한다.
        private const string RequiredPackage = "zepeto.studio";
        private const string MinimumPackageVersion = "3.2.12";
        private const string PackageAnimationFolder = "Packages/zepeto.studio/resources/Animation";
        private const string SdkPlaygroundControllerPath = "Packages/zepeto.studio/resources/PlaygroundAnimatorController.overrideController";
        // 목록에서 알파벳 순으로 가장 앞에 오는 SDK 클립은 A_pose, 즉 한 프레임짜리 정지 포즈다.
        // 그대로 기본값이 되면 사용자 눈에는 "헬퍼가 고장 났다"로 보이므로, 실제로 움직이는 클립을 지목해 둔다.
        private const string PreferredDefaultAnimationName = "Videobooth_282";
        private const string ExportMenuPath = "Assets/Zepeto Studio/Export as .zepeto";
        // 의상 아이템 템플릿이 프리팹을 두는 곳. SDK 것도 헬퍼 것도 아닌, 프로젝트 템플릿 소유 경로다.
        private const string ContentsRoot = "Assets/Contents";

        // ------------------------------------------------------------------- 헬퍼 소유 경로
        // 모션이 지나가는 뿌리 폴더는 셋이고, 소유자가 달라서 절대 합치거나 뭉뚱그리면 안 된다.
        //   CustomMotionRoot  "Assets/ZepetoHelper/Motions"    - Unity 밖에서 만든 모션(Mixamo 내려받기, Blender
        //                     내보내기)에서 뽑아낸 .anim이 사는 곳이자 라이브 프리뷰가 덮어쓰는 고정 대상
        //                     (LiveFromBlender.anim). 2단계 목록의 "내 모션"이 바로 이 폴더다.
        //   AnimationCopyRoot "Assets/ZepetoHelper/Animations" - 2단계가 만드는 "_editable" 복사본 전용.
        //   LiveWatchRoot     "Assets/CustomMotions" (LivePreview.cs) - 헬퍼는 여기에 쓰지 않는다.
        //                     Blender 애드온이 내보내는 폴더이고 헬퍼는 감시만 한다. 애드온 쪽에서 이 경로를
        //                     만드는 것은 상수가 아니라 zepeto_motion_helper.py의 refresh_paths()다. 그것이
        //                     os.path.join(project, "Assets", "CustomMotions")를 scene.zepeto_export_dir에
        //                     써 넣는다(사용자가 패널에서 고른 값은 덮어쓰지 않는다). 원본 설명은
        //                     LivePreview.cs의 LiveWatchRoot 주석이다.
        // 감시 대상을 CustomMotionRoot로 "정리"하면 라이브 프리뷰는 오류 하나 없이 영원히 발동하지 않는다.
        private const string CustomMotionRoot = "Assets/ZepetoHelper/Motions";
        private const string AnimationCopyRoot = "Assets/ZepetoHelper/Animations";
        private const string ClipEditRoot = "Assets/ZepetoHelper/Animations/ClipEdits";
        private const string ClipPreviewRoot = "Assets/ZepetoHelper/Animations/Preview";
        private const string ClipAdjustPreviewPath = ClipPreviewRoot + "/clip_adjust_preview.anim";
        private const string ControllerCopyRoot = "Assets/ZepetoHelper/Controllers";
        private const string LocalPlaygroundControllerPath = ControllerCopyRoot + "/PlaygroundAnimatorController_local.overrideController";
        // 2단계 복사본의 이름 규칙. 쓰는 쪽은 CopySelectedAnimation(Motion.cs), 되읽는 쪽은 Export.cs의
        // GetReadableMotionName이다 - 그쪽은 아직 같은 문자열을 메서드 지역 const로 따로 들고 있으므로,
        // 이 값을 바꾸려면 두 곳을 함께 바꿔야 한다. 한쪽만 바꾸면 내보내기 이름이 조용히 어긋난다.
        private const string EditableClipSuffix = "_editable";

        // 길이가 이보다 짧으면 동작이 아니라 한 프레임짜리 포즈다. SDK에 그런 클립이 여럿 있고 재생 슬롯의
        // 기본값도 그중 하나(A_pose)라서, 사용자에게는 "아바타가 아무것도 안 한다"로 보인다.
        private const float StaticPoseMaxLength = 0.1f;

        // ------------------------------------------------------------------- SessionState 키
        // Play 진입은 곧 도메인 리로드다. 직렬화되지 않은 인스턴스 필드는 전부 초기값으로 돌아가고
        // HideFlags.DontSave 오브젝트는 파괴된다. 그래서 Play를 건너뛰어야 하는 상태는 SessionState에 둔다.
        // 범위 선택이 핵심이다.
        //   SessionState  - 에디터 세션과 함께 죽는다. "빌려간 클립을 되돌릴 경로"처럼 지금 이 세션에서만
        //                   뜻이 있는 값의 자리다.
        //   EditorPrefs   - 머신 단위로 남는다. 세션 상태를 여기 두면 다음에 에디터를 켰을 때 지난 세션의 경로를
        //                   지금 scene의 LOADER에 써 넣게 되므로, 이 용도로는 항상 틀린 선택이다.
        //   [SerializeField] - 프로젝트 에셋 참조만 커버한다(clothingPrefab 참고). scene 오브젝트는 어느 쪽으로도
        //                   살아남지 못하고, 리로드 뒤 이름으로 다시 찾아야 한다(FindLoaderAndSerializedFields).
        // 키 하나는 SessionState의 이름 하나를 세션 내내 예약하는 비용이므로, 리로드를 넘겨야 하는 값에만 쓴다.
        private const string AvatarOutfitStageCompleteSessionKey = "Easy.ZepetoHelper.StageComplete.AvatarOutfit";
        private const string MotionSelectStageCompleteSessionKey = "Easy.ZepetoHelper.StageComplete.MotionSelect";
        private const string ClipStageCompleteSessionKey = "Easy.ZepetoHelper.StageComplete.Clip";
        private const string ActivePreviewStageSessionKey = "Easy.ZepetoHelper.ActivePreviewStage";
        private const string ClipAdjustPreviewActiveSessionKey = "Easy.ZepetoHelper.ClipAdjustPreview.Active";
        private const string ClipAdjustPreviewRestorePathSessionKey = "Easy.ZepetoHelper.ClipAdjustPreview.RestorePath";
        private const string ClipAdjustSourcePathSessionKey = "Easy.ZepetoHelper.ClipAdjust.SourcePath";
        private const string ClipAdjustSpeedSessionKey = "Easy.ZepetoHelper.ClipAdjust.Speed";
        private const string ClipAdjustStartSessionKey = "Easy.ZepetoHelper.ClipAdjust.Start";
        private const string ClipAdjustEndSessionKey = "Easy.ZepetoHelper.ClipAdjust.End";
        private const string ClipAdjustLoopSessionKey = "Easy.ZepetoHelper.ClipAdjust.Loop";
        private const string MotionPreviewTemporarySessionKey = "Easy.ZepetoHelper.MotionPreview.Temporary";
        private const string MotionPreviewRestorePathSessionKey = "Easy.ZepetoHelper.MotionPreview.RestorePath";
        private const string MotionPreviewRestoreNameSessionKey = "Easy.ZepetoHelper.MotionPreview.RestoreName";

        // [AUDIT][Risk:Critical][Scope:play_stability]
        // Play가 도는 중에 도메인 리로드가 일어나면 ZEPETO SDK의 UniRx 구독과 네이티브 상태가 주인을 잃는다.
        // 그다음부터는 매 프레임 ZepetoContext.UpdateContext / SwingBoneProcessor에서 예외가 터지고 아바타는
        // 완전히 멈춘다. Unity 기본값인 "Recompile And Continue Playing"이면 스크립트를 한 줄만 고쳐도
        // 그 상황이 된다.
        private const string ScriptCompilationDuringPlayPrefKey = "ScriptCompilationDuringPlay";
        private const int RecompileAndContinuePlaying = 0;
        private const int RecompileAfterFinishedPlaying = 1;
        // 안전 스냅샷이 보는 것은 로그 파일의 절대 크기가 아니라 safetyLogBaselineBytes 이후의 증가분이다.
        // 매 프레임 예외를 뱉는 Play는 그 증가분으로 드러나고, 100MB를 넘으면 위험한 동작을 막는다.
        private const long LogGrowthBlockBytes = 100L * 1024L * 1024L;
        private const int RecentLogTailBytes = 64 * 1024;
        // [QC][Guard:repaint_cost]
        // OnGUI는 초당 여러 번 돈다. 스냅샷은 로그 파일을 읽으므로 매 프레임 다시 만들면 그리기가 디스크 I/O에
        // 묶인다. 그래서 2초 캐시를 두는데, 그 대가로 스냅샷은 "그리는 도중에" 바뀔 수 있다 - OnGUI의 규칙이
        // 컨트롤의 존재 여부를 스냅샷에 걸지 말라고 하는 이유가 이것이다.
        private const double SafetyRefreshIntervalSeconds = 2d;
        private const double LoaderSearchIntervalSeconds = 1d;
        private static readonly Color ReadyGreen = new Color(0.16f, 0.70f, 0.36f);
        private static readonly Color NeededAmber = new Color(0.82f, 0.58f, 0.18f);
        private static readonly Color ActionBlue = new Color(0.24f, 0.48f, 0.88f);
        private static readonly Color PlayGreen = new Color(0.24f, 0.66f, 0.38f);
        private static readonly Color StopRed = new Color(0.82f, 0.28f, 0.24f);
        private static readonly Color WaitingGray = new Color(0.36f, 0.36f, 0.36f);

        private static readonly string[] CriticalLoopKeywords =
        {
            "SwingBoneProcessor",
            "ZepetoContext.UpdateContext",
            "Zepeto.ZepetoContext.UpdateContext",
            "Zepeto.ZepetoContext.PreUpdateContext",
            "ZepetoRoom3DSpace",
            "m_CurrentEntriesPtr"
        };

        private static readonly string[] KnownSdkCleanupKeywords =
        {
            "Zepeto.ZepetoContext.OnDestroy",
            "ZepetoContext.OnDestroy"
        };

        private static bool isLogCollectorSubscribed;
        private static int sessionWarningCount;
        private static int sessionErrorCount;
        private static string lastConsoleMessage = string.Empty;
        private readonly List<AnimationClip> packageAnimations = new List<AnimationClip>();
        private readonly List<MotionEntry> motionEntries = new List<MotionEntry>();
        private readonly List<ValidationMessage> validationMessages = new List<ValidationMessage>();
        private Vector2 scrollPosition;
        private GameObject loader;

        // 의상 선택이 Play 진입의 도메인 리로드를 넘기도록 [SerializeField]로 둔다.
        //
        // 없을 때는 Play를 누를 때마다 선택이 사라졌다. FindDefaultClothingPrefab이 이 필드를 null로 보고
        // "직접 선택하세요"로 떨어지고, HasOutfit이 false가 되고, 1단계가 Ready에서 풀리고, 2/3/4단계가 한꺼번에
        // 대기중으로 접혔다. 그 바람에 Game 뷰가 눈앞에서 돌아가는데 라이브 프리뷰 패널과 그 안의 Stop 버튼이
        // 화면에서 사라졌다. loader를 직렬화하지 않는 이유는 그것이 scene 오브젝트라서
        // 리로드 뒤 FindLoaderAndSerializedFields가 이름으로 다시 찾기 때문이다. 이 둘은 프로젝트 에셋이므로
        // 리로드를 정상적으로 넘어간다.
        [SerializeField] private GameObject clothingPrefab;
        [SerializeField] private GameObject pendingClothingPrefab;
        private AnimationClip copiedAnimationClip;
        private AnimationClip lastClipEditedClip;
        private string clipAdjustSourcePath = string.Empty;
        private string[] packageAnimationNames = new string[0];
        private int selectedAnimationIndex = -1;
        private float motionPreviewSpeed = 1f;
        private bool clipLoop = true;
        private float clipTrimStart;
        private float clipTrimEnd;
        private string zepetoIdText = string.Empty;
        private double lastLoaderSearchTime = -1000d;
        private string[] workSceneGuids = new string[0];
        private string[] workSceneOptions = new string[0];
        private int selectedWorkSceneIndex;
        private string statusMessage = string.Empty;
        private DateTime safetyStartedUtc;
        private long safetyLogBaselineBytes;
        private double lastSafetyRefreshTime = -1000d;
        private bool showSafetyAdvanced;
        private bool showDiagnosticsAdvanced;
        private bool showDetailedWorkflow;
        private bool showClipAdvancedOptions;
        private bool showPublishRecipe = true;
        private bool showManualImport;
        private bool avatarOutfitStageComplete;
        private bool motionSelectStageComplete;
        private bool clipStageComplete;
        // 지금 어떤 프리뷰 Play 세션이 도는지: PreviewStage* 상수(Workflow.cs) 중 하나이거나, 없으면 -1.
        //
        // 이 숫자는 창에 보이는 카드 번호(1~7)가 아니라 내부 스테이지 번호(1~4)다. 두 체계는 겹치지 않는다.
        // 스테이지 3 = 카드 6(클립 조정), 스테이지 4 = 카드 7(내보내기)이고, 스테이지 2 하나를 카드 2와 카드 5가
        // 나눠 쓴다. 값 자체가 계약이라 카드 번호에 맞춰 다시 매기면 안 된다. 클립 조정 프리뷰의 Play 경로가
        // 스테이지 3이라는 값 하나에만 걸려 있어서(Safety.cs의 RequestPlayMode), 번호를 옮기면 그 프리뷰가
        // 오류 없이 죽는다. 전체 대응표는 Workflow.cs의 PreviewStage* 상수 위 주석이 원본이다.
        private int activePreviewStage = -1;
        private SerializedObject zepetoIdObject;
        private SerializedObject animationClipObject;
        private SerializedObject animatorControllerObject;
        private SerializedProperty zepetoIdProperty;
        // 아바타가 실제로 무엇을 재생하는지는 이 필드가 정하지 않는다. SDK는 AnimatorOverrideController의
        // "dynamic" 슬롯 하나만 재생하고, 그 슬롯의 출고 기본값은 A_pose(0.04초 정지 포즈)다. 즉 여기에 클립을
        // 써넣어도 재생은 그대로이고, 재생을 바꾸는 것은 ApplyClipToOverrideController(Loader.cs) 뿐이다.
        // 이 필드는 scene에 남는 "무엇을 골랐는가"의 기록이고, 슬롯은 "무엇이 나오는가"다.
        // 둘은 항상 함께 바뀌어야 한다.
        private SerializedProperty animationClipProperty;
        private SerializedProperty animatorControllerProperty;

        // [AUDIT][Risk:High][Scope:step2_preview]
        // 이 둘은 원래 평범한 인스턴스 필드였고, 그래서 Play 진입이라는 도메인 리로드를 넘기지 못했다.
        // 실제 순서는 이랬다. PlaySelectedMotionPreview가 LOADER의 클립을 빌려 두고 플래그를 세운다 -> Play가
        // 시작된다 -> 도메인 리로드가 플래그를 지운다 -> Stop이 RestoreTemporarySelectedMotionPreview를 부른다
        // (Safety.cs, EnteredEditMode) -> `!isTemporarySelectedMotionPreview` 가드에서 그대로 return.
        // 미리보기 클립이 LOADER에도 오버라이드 컨트롤러에도 그대로 남고, 사용자의 작업 동작은 끝내 돌아오지
        // 않는데 그 사실을 알리는 메시지조차 없었다. 형제 격인 다른 두 프리뷰 흐름은 같은 이유로 이미
        // SessionState를 쓰고 있었다: ClipAdjustPreviewActiveSessionKey / ClipAdjustPreviewRestorePathSessionKey
        // (ClipEdit.cs), LiveRestoreActiveSessionKey / LiveRestorePathSessionKey (LivePreview.cs).
        //
        // 필드 이름을 그대로 둔 채 프로퍼티로만 바꿨기 때문에 호출부는 하나도 손대지 않았다. Motion.cs
        // (PlaySelectedMotionPreview, RestoreTemporarySelectedMotionPreview)와 Steps.cs("미리보기 Stop" 라벨)는
        // 예전과 똑같이 읽고 쓴다.
        //
        // 범위는 EditorPrefs가 아니라 SessionState가 맞다. 빌림은 에디터가 닫힐 때까지만 뜻이 있고, 지난 세션에서
        // 남은 복원 경로는 지금 열려 있는 scene의 LOADER에 엉뚱한 클립을 써 넣게 된다.
        private bool isTemporarySelectedMotionPreview
        {
            get { return SessionState.GetBool(MotionPreviewTemporarySessionKey, false); }
            set { SessionState.SetBool(MotionPreviewTemporarySessionKey, value); }
        }

        private AnimationClip motionPreviewRestoreClip
        {
            get
            {
                string path = SessionState.GetString(MotionPreviewRestorePathSessionKey, string.Empty);
                if (string.IsNullOrEmpty(path))
                {
                    // 빈 경로는 "미리보기 전에 LOADER에 클립이 없었다"는 경우이기도 하다. 그때는 일부러
                    // null로 되돌린다. 빌림이 있었는지를 말하는 것은 이 값이 아니라
                    // isTemporarySelectedMotionPreview다.
                    return null;
                }

                string clipName = SessionState.GetString(MotionPreviewRestoreNameSessionKey, string.Empty);
                AnimationClip direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (direct != null && (string.IsNullOrEmpty(clipName) || direct.name == clipName))
                {
                    return direct;
                }

                // 빌린 클립이 서브 에셋일 수 있다. FBX 안의 take거나, 클립이 여러 개 들어 있는 SDK 에셋의 하나인
                // 경우다. 그럴 때 LoadAssetAtPath는 빌린 그 클립이 아니라 맨 앞의 클립을 돌려준다.
                // 그래서 경로만이 아니라 이름까지 저장해 두고, 경로 안의 에셋을 훑어 이름이 같은 것을 찾는다.
                UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int i = 0; i < assetsAtPath.Length; i++)
                {
                    AnimationClip candidate = assetsAtPath[i] as AnimationClip;
                    if (candidate != null && candidate.name == clipName)
                    {
                        return candidate;
                    }
                }

                return direct;
            }

            set
            {
                SessionState.SetString(
                    MotionPreviewRestorePathSessionKey,
                    value == null ? string.Empty : AssetDatabase.GetAssetPath(value));
                SessionState.SetString(
                    MotionPreviewRestoreNameSessionKey,
                    value == null ? string.Empty : value.name);
            }
        }

        [MenuItem("Window/Easy/ZEPETO Studio Helper")]
        public static void Open()
        {
            ZepetoStudioHelperWindow window = GetWindow<ZepetoStudioHelperWindow>("ZEPETO Helper");
            window.minSize = new Vector2(480f, 360f);
            window.RefreshAll();
            window.Show();
        }

        private void OnEnable()
        {
            SubscribeLogCollector();
            SceneView.duringSceneGui += OnSceneViewGui;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SubscribeLiveReload();
            safetyStartedUtc = DateTime.UtcNow;
            // 지금 로그 크기를 0점으로 잡아 둔다. 안전 스냅샷이 보는 것은 로그의 절대 크기가 아니라 이 창이 열린
            // 뒤에 얼마나 불어났는가이고, 폭주하는 Play는 그 증가분으로 드러난다.
            safetyLogBaselineBytes = GetCurrentLogSize();
            LoadZepetoIdSettings();
            LoadWorkflowStageProgress();
            RefreshAll();

            // RefreshAll 뒤여야 한다. 그래야 `loader`가 묶여 있어서 대역 몸체가 제자리에 놓인다.
            SyncPreviewBody();
        }

        // OnDisable은 OnEnable의 거울이 아니다. 일부러 되돌리지 않는 것이 둘 있다.
        //   - scene 감시 해제는 여기서 하지 않는다. LivePreview.cs의 OnDestroy가 담당한다. 창을 잠깐 닫았다 여는
        //     것과 창을 버리는 것은 다르고, 감시는 전자를 넘겨야 한다.
        //   - 라이브 프리뷰가 빌려 간 재생 슬롯도 여기서 돌려주지 않는다(LivePreview.cs 참고). Play 도중의
        //     도메인 리로드도 OnDisable을 부르기 때문에, 여기서 되돌리면 살아 있는 Play의 슬롯을 뺏게 된다.
        private void OnDisable()
        {
            UnsubscribeLogCollector();
            SceneView.duringSceneGui -= OnSceneViewGui;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnsubscribeLiveReload();
            // 대역 몸체는 이 창의 소유물이다. 그냥 두면 아무도 주인이 아니고 어떤 UI로도 지울 수 없는 몸체가
            // Scene에 남는다.
            ClearPreviewBody();
            SaveWorkflowStageProgress();
        }

        // 한 번의 OnGUI 안에서 Layout 패스와 Repaint 패스는 컨트롤의 개수와 순서가 완전히 같아야 한다.
        // 그래서 이 창의 카드들은 컨트롤을 조건 없이, 늘 같은 순서로 그린다. 프레임마다 달라져도 되는 것은
        // enabled 여부, 라벨과 문구, MessageType 뿐이다.
        //
        // 이건 스타일 규칙이 아니라 이 창이 감시하는 상태의 성질 때문이다. isPlaying은 프레임 중간에 뒤집히고,
        // sessionErrorCount는 백그라운드 로그 콜백에서 증가하며, 안전 스냅샷은 그리는 도중에 2초 타이머로
        // 갱신된다. 그중 하나로 컨트롤의 "존재" 여부를 정하면 두 패스의 GUILayout 그룹이 어긋나 컨트롤이
        // 깜빡이며 사라진다. 하필 그렇게 사라지는 것이 Play에서 빠져나오는 Stop 버튼이라, 사용자는 갇힌다.
        // BeginVertical 앞의 이른 return도 같은 사고다. 비활성으로 그리되, 비활성일 때는 반드시 비어 있지 않은
        // 이유 문구를 함께 보여 준다.
        //
        // 같은 이유로 이 호출 트리 안에서 scene을 건드리지 않는다. 그리는 도중에 오브젝트가 생기거나 사라지면
        // 남은 패스가 다른 세계를 보게 되므로, scene을 바꾸는 일은 EditorApplication.delayCall로 미룬다.
        private void OnGUI()
        {
            DrawToolbar();

            float contentWidth = Mathf.Max(320f, position.width - 28f);
            scrollPosition = EditorGUILayout.BeginScrollView(
                scrollPosition,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUIStyle.none);
            EditorGUILayout.BeginVertical(GUILayout.MaxWidth(contentWidth));
            DrawMotionWorkspace();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        // 호출 순서가 곧 의존 순서다. 안전 -> LOADER -> scene 목록 -> 의상 -> 동작 -> 검증.
        // 뒤의 단계는 앞의 단계가 채운 상태를 읽는다. 특히 ValidateState는 맨 끝이어야 하는데, 그것이 판정하는
        // 대상이 앞의 다섯 줄이 방금 만들어 놓은 상태 전부이기 때문이다.
        private void RefreshAll()
        {
            RefreshSafetySnapshot();
            // 앞뒤로 타이머를 조작하는 건 실수가 아니라 캐시 우회다. FindLoaderAndSerializedFields는 평소
            // EnsureLoaderBinding의 1초 간격에 눌려 있는데, "새로고침"은 사용자가 방금 scene을 바꿨다는 뜻이므로
            // 이번 한 번만은 반드시 다시 찾아야 한다. 그리고 곧바로 지금 시각을 넣어 다음 1초를 다시 잠근다.
            lastLoaderSearchTime = -1000d;
            FindLoaderAndSerializedFields();
            lastLoaderSearchTime = EditorApplication.timeSinceStartup;
            RefreshWorkSceneCandidates();
            FindDefaultClothingPrefab();
            LoadPackageAnimations();
            FindExistingCopiedAnimation();
            ValidateState();
        }
    }
}
