using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Window shell: shared state, Unity lifecycle and the top-level repaint entry point.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow : EditorWindow
    {
        // [QC][Invariant:no_personal_defaults]
        // The package ships with no built-in ZEPETO account. The id is typed in and lives in the scene's
        // LOADER, which is the only thing that decides which avatar loads.
        //
        // Written by helper 0.5.x and earlier, when ids were remembered across projects. LoadZepetoIdSettings
        // deletes these once so a stale account cannot come back after the feature was removed.
        private static readonly string[] ObsoleteZepetoIdPrefKeys =
        {
            "com.easy.zepeto-helper.activeZepetoId",
            "com.easy.zepeto-helper.savedZepetoIds",
            "com.easy.zepeto-helper.defaultZepetoId"
        };

        private const int MaxZepetoIdLength = 64;
        private const string RequiredPackage = "zepeto.studio";
        private const string MinimumPackageVersion = "3.2.12";
        private const string PackageAnimationFolder = "Packages/zepeto.studio/resources/Animation";
        // Custom motions authored outside Unity (Mixamo download, Blender export) land here.
        private const string CustomMotionRoot = "Assets/ZepetoHelper/Motions";
        // Anything this short is a single-frame pose, not a motion. The SDK ships several of them and the
        // playback slot defaults to one, which reads to the user as "the avatar does nothing".
        private const float StaticPoseMaxLength = 0.1f;
        private const string SdkPlaygroundControllerPath = "Packages/zepeto.studio/resources/PlaygroundAnimatorController.overrideController";
        private const string PreferredDefaultAnimationName = "Videobooth_282";
        private const string ContentsRoot = "Assets/Contents";
        private const string AnimationCopyRoot = "Assets/ZepetoHelper/Animations";
        private const string ClipEditRoot = "Assets/ZepetoHelper/Animations/ClipEdits";
        private const string ClipPreviewRoot = "Assets/ZepetoHelper/Animations/Preview";
        private const string ClipAdjustPreviewPath = ClipPreviewRoot + "/clip_adjust_preview.anim";
        private const string ControllerCopyRoot = "Assets/ZepetoHelper/Controllers";
        private const string LocalPlaygroundControllerPath = ControllerCopyRoot + "/PlaygroundAnimatorController_local.overrideController";
        private const string ExportMenuPath = "Assets/Zepeto Studio/Export as .zepeto";
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
        // A domain reload while Play is running orphans the ZEPETO SDK's UniRx subscriptions and native state.
        // Every following frame then throws from ZepetoContext.UpdateContext / SwingBoneProcessor and the avatar
        // stops moving entirely. Unity's default "Recompile And Continue Playing" makes any script edit trigger it.
        private const string ScriptCompilationDuringPlayPrefKey = "ScriptCompilationDuringPlay";
        private const int RecompileAndContinuePlaying = 0;
        private const int RecompileAfterFinishedPlaying = 1;
        private const long LogGrowthBlockBytes = 100L * 1024L * 1024L;
        private const int RecentLogTailBytes = 64 * 1024;
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

        // [SerializeField] so the outfit choice survives the domain reload that entering Play triggers.
        //
        // Without it the selection was lost on every single Play: FindDefaultClothingPrefab found the field
        // null and fell through to "직접 선택하세요", HasOutfit went false, stage 1 stopped being Ready, and
        // steps 2/3/4 collapsed to 대기중 - taking the live-preview panel and its Stop button off screen while
        // the Game view was visibly running. loader is not serialized because it is a scene object that
        // FindLoaderAndSerializedFields rebinds by name after every reload; these two are project assets, which
        // serialize across a reload correctly.
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
        // Which preview Play session is running: one of the PreviewStage* constants (Workflow.cs), or -1 for
        // "none". Those are internal stage numbers, NOT card numbers - stage 3 is card 6, stage 4 is card 7.
        private int activePreviewStage = -1;
        private SerializedObject zepetoIdObject;
        private SerializedObject animationClipObject;
        private SerializedObject animatorControllerObject;
        private SerializedProperty zepetoIdProperty;
        private SerializedProperty animationClipProperty;
        private SerializedProperty animatorControllerProperty;

        // [AUDIT][Risk:High][Scope:step2_preview]
        // These two were plain instance fields, which cannot survive the domain reload that entering Play is.
        // The sequence was: PlaySelectedMotionPreview borrows the LOADER's clip and sets the flag -> Play starts
        // -> domain reload wipes the flag -> Stop calls RestoreTemporarySelectedMotionPreview (Safety.cs, on
        // EnteredEditMode) -> it returns immediately at the `!isTemporarySelectedMotionPreview` guard. The
        // previewed clip stayed in the LOADER *and* in the override controller, the user's work motion was never
        // put back, and no message said so. The two sibling preview flows already use SessionState for exactly
        // this reason: ClipAdjustPreviewActiveSessionKey / ClipAdjustPreviewRestorePathSessionKey (ClipEdit.cs)
        // and LiveRestoreActiveSessionKey / LiveRestorePathSessionKey (LivePreview.cs).
        //
        // They stay properties with the original field names so every call site is unchanged - Motion.cs
        // (PlaySelectedMotionPreview, RestoreTemporarySelectedMotionPreview) and Steps.cs (the "미리보기 Stop"
        // label) keep reading and writing them exactly as before.
        //
        // SessionState is the right scope, not EditorPrefs: the borrow is only meaningful until the editor
        // closes, and a stale restore path from a previous session would write the wrong clip into the LOADER.
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
                    // Also the "the LOADER had no clip before the preview" case, which restores to null on
                    // purpose. isTemporarySelectedMotionPreview is what says a borrow happened, not this.
                    return null;
                }

                string clipName = SessionState.GetString(MotionPreviewRestoreNameSessionKey, string.Empty);
                AnimationClip direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (direct != null && (string.IsNullOrEmpty(clipName) || direct.name == clipName))
                {
                    return direct;
                }

                // The borrowed clip can be a sub-asset - an FBX take, or one clip of several in an SDK asset -
                // where LoadAssetAtPath returns whichever clip comes first rather than the one we borrowed.
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
            safetyLogBaselineBytes = GetCurrentLogSize();
            safetySnapshot = SafetySnapshot.Unknown("Safety status will update after the helper opens.");
            lastSafetyRefreshTime = -1000d;
            LoadZepetoIdSettings();
            LoadWorkflowStageProgress();
            RefreshAll();

            // After RefreshAll, so `loader` is bound and the stand-in lands at the right place.
            SyncPreviewBody();
        }

        private void OnDisable()
        {
            UnsubscribeLogCollector();
            SceneView.duringSceneGui -= OnSceneViewGui;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnsubscribeLiveReload();
            // The stand-in belongs to this window. Leaving it behind would put an unexplained body in the Scene
            // that nothing owns and no UI can remove.
            ClearPreviewBody();
            SaveWorkflowStageProgress();
        }

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

        private void RefreshAll()
        {
            RefreshSafetySnapshot();
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
