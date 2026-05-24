using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    public sealed class ZepetoStudioHelperWindow : EditorWindow
    {
        private const string BuiltInDefaultZepetoId = "darbams77";
        private const string DefaultZepetoIdEditorPrefsKey = "com.easy.zepeto-helper.defaultZepetoId";
        private const string RequiredPackage = "zepeto.studio";
        private const string RequiredPackageVersion = "3.2.12";
        private const string PackageAnimationFolder = "Packages/zepeto.studio/resources/Animation";
        private const string ContentsRoot = "Assets/Contents";
        private const string AnimationCopyRoot = "Assets/ZepetoHelper/Animations";
        private const string ClipEditRoot = "Assets/ZepetoHelper/Animations/ClipEdits";
        private const string ClipPreviewRoot = "Assets/ZepetoHelper/Animations/Preview";
        private const string ClipAdjustPreviewPath = ClipPreviewRoot + "/clip_adjust_preview.anim";
        private const string ControllerCopyRoot = "Assets/ZepetoHelper/Controllers";
        private const string LocalPlaygroundControllerPath = ControllerCopyRoot + "/PlaygroundAnimatorController_local.overrideController";
        private const string ExportMenuPath = "Assets/Zepeto Studio/Export as .zepeto";
        private const string McpAutoRestartSessionKey = "Easy.ZepetoHelper.McpAutoRestartAttempted.v2";
        private const string StageUnlockSessionKeyPrefix = "Easy.ZepetoHelper.StageUnlocked.";
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
        private const long LogGrowthBlockBytes = 100L * 1024L * 1024L;
        private const int RecentLogTailBytes = 64 * 1024;
        private const double SafetyRefreshIntervalSeconds = 2d;

        private static readonly Color ReadyGreen = new Color(0.16f, 0.70f, 0.36f);
        private static readonly Color NeededAmber = new Color(0.82f, 0.58f, 0.18f);
        private static readonly Color BlockedRed = new Color(0.84f, 0.23f, 0.20f);
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
        private readonly List<ValidationMessage> validationMessages = new List<ValidationMessage>();

        private Vector2 scrollPosition;
        private GameObject loader;
        private GameObject clothingPrefab;
        private GameObject pendingClothingPrefab;
        private AnimationClip copiedAnimationClip;
        private AnimationClip lastClipEditedClip;
        private AnimationClip clipAdjustSource;
        private string clipAdjustSourcePath = string.Empty;
        private string[] packageAnimationNames = new string[0];
        private int selectedAnimationIndex = -1;
        private float motionPreviewSpeed = 1f;
        private bool clipLoop = true;
        private float clipTrimStart;
        private float clipTrimEnd;
        private string defaultZepetoId = BuiltInDefaultZepetoId;
        private string zepetoIdText = BuiltInDefaultZepetoId;
        private string statusMessage = string.Empty;
        private SafetySnapshot safetySnapshot = SafetySnapshot.Unknown("Safety status has not been checked yet.");
        private DateTime safetyStartedUtc;
        private long safetyLogBaselineBytes;
        private double lastSafetyRefreshTime = -1000d;
        private bool isSafeRefreshQueued;
        private bool showSafetyAdvanced;
        private bool showSetupAdvanced;
        private bool showDiagnosticsAdvanced;
        private bool showDetailedWorkflow;
        private bool showClipAdvancedOptions;
        private bool showScenePreviewOverlay = true;
        private readonly bool[] unlockedReadyStages = new bool[8];
        private bool avatarOutfitStageComplete;
        private bool motionSelectStageComplete;
        private bool clipStageComplete;
        private int activePreviewStage = -1;
        private bool isTemporarySelectedMotionPreview;
        private AnimationClip motionPreviewRestoreClip;

        private SerializedObject zepetoIdObject;
        private SerializedObject animationClipObject;
        private SerializedObject animatorControllerObject;
        private SerializedProperty zepetoIdProperty;
        private SerializedProperty animationClipProperty;
        private SerializedProperty animatorControllerProperty;

        private enum StepState
        {
            Ready,
            InProgress,
            Needed,
            Waiting,
            Blocked
        }

        private enum SafetyLevel
        {
            Ok,
            Recoverable,
            HardBlock
        }

        private struct WorkflowStatus
        {
            public SafetySnapshot Safety;
            public string CurrentZepetoId;
            public bool HasLoader;
            public bool HasZepetoIdField;
            public bool HasZepetoId;
            public bool HasOutfit;
            public bool OutfitIsUnderContents;
            public bool HasSelectedPackageAnimation;
            public bool HasCopiedAnimation;
            public bool HasAssignedAnimation;
            public bool HasEditableAssignedAnimation;
            public bool HasLocalAnimatorController;
            public bool HasPreviewInputs;
            public bool HasAvatarPlayInputs;
            public bool HasMotionPlayInputs;
            public bool CanPlay;
            public bool CanPlayAvatarOutfit;
            public bool CanPlayMotion;
            public bool CanClipEdit;
            public string OutfitPath;
            public string AssignedAnimationPath;
            public string AnimatorControllerPath;
            public AnimationClip AssignedAnimation;
        }

        [MenuItem("Window/Easy/ZEPETO Studio Helper")]
        public static void Open()
        {
            ZepetoStudioHelperWindow window = GetWindow<ZepetoStudioHelperWindow>("ZEPETO Helper");
            window.minSize = new Vector2(480f, 360f);
            window.RefreshAll();
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void AutoRecoverUnityMcpBridgeOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(McpAutoRestartSessionKey, false))
                {
                    return;
                }

                int port = GetUnityMcpBridgePort();
                if (port > 0 && CanPingUnityMcpBridge(port))
                {
                    return;
                }

                string message;
                if (TryRestartUnityMcpBridge(out message))
                {
                    SessionState.SetBool(McpAutoRestartSessionKey, true);
                    Debug.Log("Easy ZEPETO Helper requested Unity MCP bridge restart. " + message);
                }
            };
        }

        private void OnEnable()
        {
            SubscribeLogCollector();
            SceneView.duringSceneGui += OnSceneViewGui;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            safetySnapshot = SafetySnapshot.Unknown("Safety status will update after the helper opens.");
            lastSafetyRefreshTime = -1000d;
            LoadDefaultZepetoId();
            LoadUnlockedReadyStages();
            LoadWorkflowStageProgress();
            RefreshAll();
        }

        private void OnDisable()
        {
            UnsubscribeLogCollector();
            SceneView.duringSceneGui -= OnSceneViewGui;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            SaveUnlockedReadyStages();
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

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                RestoreTemporarySelectedMotionPreview();
                RestoreTemporaryClipAdjustPreview();
                activePreviewStage = -1;
                SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
                return;
            }

            if (change != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                FindLoaderAndSerializedFields();
                FrameLoaderForScenePreview();
                Repaint();
            };
        }

        private void OnSceneViewGui(SceneView sceneView)
        {
            if (!showScenePreviewOverlay || sceneView == null)
            {
                return;
            }

            if (!EditorApplication.isPlaying && !LoaderHasPreviewRenderers())
            {
                return;
            }

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 292f, 108f), EditorStyles.helpBox);
            GUILayout.Label("Scene 보조 / Preview Focus", EditorStyles.boldLabel);
            GUILayout.Label(loader == null ? "LOADER: 없음 / Missing" : "LOADER: " + loader.name, EditorStyles.miniLabel);
            GUILayout.Label(EditorApplication.isPlaying ? "Play 중: 실제 아바타 확인" : "정지 중: LOADER 초점 확인", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loader == null))
            {
                if (GUILayout.Button("선택 / Select", EditorStyles.miniButtonLeft))
                {
                    SelectAndFrameLoader();
                }

                if (GUILayout.Button("초점 / Focus", EditorStyles.miniButtonRight))
                {
                    FrameLoaderForScenePreview();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!LoaderHasPreviewRenderers())
            {
                GUILayout.Label(EditorApplication.isPlaying
                    ? "아바타 로딩 전/실패: ID와 SDK 상태 확인"
                    : "정지 중에는 아바타 mesh가 없을 수 있음", EditorStyles.miniLabel);
            }

            GUILayout.Label(EditorApplication.isPlaying
                ? "Stop 후에만 저장/Export 가능"
                : "Play 버튼으로 실제 움직임 확인", EditorStyles.miniLabel);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Easy ZEPETO Studio Helper", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }
        }
        private void OpenPlaygroundScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 scene을 바꾸지 않습니다. 먼저 Stop을 누르세요.";
                return;
            }

            const string playgroundPath = "Assets/Playground.unity";
            if (!File.Exists(playgroundPath))
            {
                statusMessage = "Playground scene을 찾지 못했습니다: " + playgroundPath;
                return;
            }

            UnityEngine.SceneManagement.Scene activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (activeScene.isDirty
                && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                statusMessage = "Scene 변경을 취소했습니다. / Open scene canceled.";
                return;
            }

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                playgroundPath,
                UnityEditor.SceneManagement.OpenSceneMode.Single
            );
            RefreshAll();
            statusMessage = "Playground scene을 열었습니다. / Playground scene opened.";
        }
        private void DrawWarningCleanupPanel()
        {
            SafetySnapshot snapshot = GetSafetySnapshot(false);
            bool hasPackageController = IsPackageOrPackageCachePath(GetAnimatorControllerPath());
            bool shouldShow = snapshot.HasWarning
                || snapshot.HasBlockingRisk
                || hasPackageController
                || sessionErrorCount > 0;
            if (!shouldShow)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("경고 복구 / Warning Cleanup", EditorStyles.boldLabel);

            if (snapshot.HasBlockingRisk)
            {
                DrawMiniHelp("Play가 막힌 이유: " + snapshot.Message, MessageType.Error);
            }
            else if (snapshot.HasWarning)
            {
                DrawMiniHelp("복구 가능한 경고입니다. 복구 후 다시 시도하세요: " + snapshot.Message, MessageType.Warning);
            }
            else if (hasPackageController)
            {
                DrawMiniHelp("AnimatorController가 package cache를 가리킵니다. local copy로 바꿔야 package cache warning을 피할 수 있습니다.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Stop Play", GUILayout.Height(26f)))
                {
                    StopPlayMode();
                }
            }

            if (DrawBlueActionButton("Recover", true, GUILayout.Height(26f)))
            {
                RecoverSafetyState();
            }

            if (DrawSecondaryButton("Clear Console", GUILayout.Height(26f)))
            {
                ClearConsoleAndSessionSummary();
            }

            if (DrawSecondaryButton("Fresh Log Check", GUILayout.Height(26f)))
            {
                RefreshSafetySnapshot();
                ValidateState();
                statusMessage = "Fresh log check complete. / 새 로그 상태를 다시 확인했습니다.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("MCP Recheck", GUILayout.Height(24f)))
            {
                int port = GetUnityMcpBridgePort();
                statusMessage = port > 0 && CanPingUnityMcpBridge(port)
                    ? "MCP bridge ping OK: " + port
                    : "MCP bridge ping failed. Restart MCP Bridge를 눌러 복구하세요.";
                RefreshSafetySnapshot();
            }

            if (DrawSecondaryButton("Restart MCP Bridge", GUILayout.Height(24f)))
            {
                string mcpMessage;
                if (TryRestartUnityMcpBridge(out mcpMessage))
                {
                    statusMessage = "MCP bridge restart requested. " + mcpMessage;
                }
                else
                {
                    statusMessage = "MCP bridge restart failed: " + mcpMessage;
                }
            }

            using (new EditorGUI.DisabledScope(animatorControllerProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Local Controller Fix", GUILayout.Height(24f)))
                {
                    string controllerMessage;
                    if (EnsureLocalAnimatorController(out controllerMessage))
                    {
                        statusMessage = controllerMessage;
                    }
                    else
                    {
                        statusMessage = "Local controller fix failed: " + controllerMessage;
                    }

                    ValidateState();
                }
            }

            if (DrawSecondaryButton("Package Cache Guide", GUILayout.Height(24f)))
            {
                statusMessage = "package cache warning이 이미 찍혔다면 Unity 종료 후 Library/PackageCache/zepeto.studio@3.2.12를 재생성하면 됩니다. helper는 이후 Assets/ZepetoHelper local asset만 수정합니다.";
            }
            EditorGUILayout.EndHorizontal();

            if (snapshot.HasBlockingRisk || snapshot.LogSizeBytes >= LogGrowthBlockBytes)
            {
                if (DrawPrimaryButton("Emergency Stop Preview / 긴급 정지", true))
                {
                    StopPlayMode();
                    ClearConsoleAndSessionSummary();
                    statusMessage = "Emergency stop complete. / 미리보기와 세션 경고를 정리했습니다.";
                }
            }

            showSafetyAdvanced = DrawAdvancedFoldout(showSafetyAdvanced);
            if (showSafetyAdvanced)
            {
                DrawStatusRow("Log Size", FormatBytes(snapshot.LogSizeBytes));
                DrawStatusRow("MCP 상태 / MCP", BuildMcpStatusText(snapshot));
                DrawStatusRow("AnimatorController", string.IsNullOrEmpty(GetAnimatorControllerPath()) ? "없음 / Missing" : GetAnimatorControllerPath());

                if (!string.IsNullOrEmpty(snapshot.Detail))
                {
                    DrawMiniHelp(snapshot.Detail, MessageType.None);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(snapshot.LogPath)))
                {
                    if (DrawSecondaryButton("로그 위치 열기 / Open Log Folder", GUILayout.Height(24f)))
                    {
                        OpenLogLocation(snapshot.LogPath);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMotionWorkspace()
        {
            SafetySnapshot snapshot = GetSafetySnapshot(false);
            WorkflowStatus workflow = BuildWorkflowStatus(snapshot);

            DrawV7WorkbenchHeader(workflow);
            DrawWarningCleanupPanel();
            DrawAvatarOutfitStep(workflow);
            DrawMotionBrowser(workflow);
            DrawClipAdjust(workflow);
            DrawSaveExportStep(workflow);
            DrawSetupFoldout(workflow);
            DrawDiagnosticsFoldout(workflow);
        }

        private void DrawV7WorkbenchHeader(WorkflowStatus workflow)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("v7 ZEPETO 작업대", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isPlaying ? "Play 중" : "Stop 상태", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();

            DrawMiniHelp(
                "1번부터 아래로 진행하세요. 각 단계의 Play/Stop으로 확인하고, 필요 없는 버튼은 자동으로 잠깁니다.",
                MessageType.None);

            DrawWorkflowStatusLine(workflow);
            DrawV7StageRail(workflow);

            if (workflow.Safety.HasBlockingRisk)
            {
                DrawMiniHelp("막힌 이유: " + workflow.Safety.Message, MessageType.Error);
            }
            else if (workflow.Safety.HasWarning)
            {
                DrawMiniHelp("경고: " + workflow.Safety.Message, MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWorkflowStatusLine(WorkflowStatus workflow)
        {
            DrawStatusRow("현재 작업", GetCurrentStageText(workflow));
            DrawStatusRow("다음 행동", GetWorkflowHint(workflow));
            GUILayout.Label(BuildWorkflowPath(workflow), EditorStyles.wordWrappedMiniLabel);
        }

        private string GetWorkflowHint(WorkflowStatus workflow)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                return "복구 / Recover";
            }

            if (!workflow.HasAvatarPlayInputs)
            {
                return "1번에서 아이디와 의상을 확인한 뒤 Play";
            }

            if (!workflow.HasOutfit || !workflow.OutfitIsUnderContents)
            {
                return "1-2에서 의상을 고른 뒤 파란 의상 적용 버튼";
            }

            if (!avatarOutfitStageComplete)
            {
                return "1번 맨 아래에서 확인 후 1번 적용 / 다음 단계";
            }

            if (!workflow.HasEditableAssignedAnimation || !motionSelectStageComplete)
            {
                return "2번에서 동작을 고르고 미리보기 Play 후 작업 동작으로 사용";
            }

            if (!clipStageComplete)
            {
                return HasClipAdjustInput(workflow.AssignedAnimation)
                    ? "3번에서 클립 조정을 저장한 뒤 다음 단계"
                    : "3번에서 배속/길이를 확인하고 다음 단계";
            }

            return "4번에서 최종 Play 확인 후 Export";
        }

        private string BuildWorkflowPath(WorkflowStatus workflow)
        {
            return FormatWorkflowStep("1 아바타+의상", GetSequentialStageLabel(workflow, 1))
                + " > " + FormatWorkflowStep("2 동작", GetSequentialStageLabel(workflow, 2))
                + " > " + FormatWorkflowStep("3 클립", GetSequentialStageLabel(workflow, 3))
                + " > " + FormatWorkflowStep("4 Export", GetSequentialStageLabel(workflow, 4));
        }

        private static string FormatWorkflowStep(string label, string state)
        {
            return label + "(" + state + ")";
        }

        private void DrawV7StageRail(WorkflowStatus workflow)
        {
            EditorGUILayout.Space(4f);
            bool useTwoRows = position.width < 620f;
            int activeStage = GetActiveStageNumber(workflow);
            StepState avatarState = GetSequentialStageState(workflow, 1);
            StepState motionState = GetSequentialStageState(workflow, 2);
            StepState clipState = GetSequentialStageState(workflow, 3);
            StepState exportState = GetSequentialStageState(workflow, 4);

            if (useTwoRows)
            {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                DrawV7StagePill("1", "아바타", GetStageLabel(avatarState, "완료"), avatarState, activeStage == 1);
                DrawV7StagePill("2", "동작", GetStageLabel(motionState, "완료"), motionState, activeStage == 2);
                DrawV7StagePill("3", "클립", GetStageLabel(clipState, "완료"), clipState, activeStage == 3);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                DrawV7StagePill("4", "Export", GetStageLabel(exportState, "준비됨"), exportState, activeStage == 4);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawV7StagePill("1", "아바타", GetStageLabel(avatarState, "완료"), avatarState, activeStage == 1);
            DrawV7StagePill("2", "동작", GetStageLabel(motionState, "완료"), motionState, activeStage == 2);
            DrawV7StagePill("3", "클립", GetStageLabel(clipState, "완료"), clipState, activeStage == 3);
            DrawV7StagePill("4", "Export", GetStageLabel(exportState, "준비됨"), exportState, activeStage == 4);
            EditorGUILayout.EndHorizontal();
        }

        private int GetActiveStageNumber(WorkflowStatus workflow)
        {
            if (!workflow.HasAvatarPlayInputs || !workflow.HasOutfit || !avatarOutfitStageComplete)
            {
                return 1;
            }

            if (!workflow.HasEditableAssignedAnimation || !motionSelectStageComplete)
            {
                return 2;
            }

            if (!clipStageComplete)
            {
                return 3;
            }

            return 4;
        }

        private string GetCurrentStageText(WorkflowStatus workflow)
        {
            switch (GetActiveStageNumber(workflow))
            {
                case 1:
                    return "1. 아바타+의상 준비";
                case 2:
                    return "2. 동작 선택";
                case 3:
                    return "3. 클립 조정";
                default:
                    return "4. 저장/Export";
            }
        }
        private bool IsStageComplete(WorkflowStatus workflow, int stage)
        {
            switch (stage)
            {
                case 1:
                    return workflow.HasAvatarPlayInputs && workflow.HasOutfit && avatarOutfitStageComplete;
                case 2:
                    return workflow.HasEditableAssignedAnimation && motionSelectStageComplete;
                case 3:
                    return workflow.CanClipEdit && clipStageComplete;
                case 4:
                    return workflow.HasEditableAssignedAnimation && workflow.HasOutfit && avatarOutfitStageComplete && motionSelectStageComplete && clipStageComplete;
                default:
                    return false;
            }
        }

        private bool IsStageWaiting(WorkflowStatus workflow, int stage)
        {
            return stage > GetActiveStageNumber(workflow);
        }

        private StepState GetSequentialStageState(WorkflowStatus workflow, int stage)
        {
            if (workflow.Safety.HasBlockingRisk && stage == GetActiveStageNumber(workflow))
            {
                return StepState.Blocked;
            }

            // [QC][Invariant:sequential_unlock]
            // Later stages can have stale completed assets from a previous run, but they must stay locked
            // while an earlier required step is incomplete. Check waiting before completed state.
            if (IsStageWaiting(workflow, stage))
            {
                return StepState.Waiting;
            }

            if (IsStageComplete(workflow, stage))
            {
                return StepState.Ready;
            }

            return StepState.InProgress;
        }

        private string GetSequentialStageLabel(WorkflowStatus workflow, int stage)
        {
            StepState state = GetSequentialStageState(workflow, stage);
            if (state == StepState.Ready)
            {
                return stage == 4 ? "준비됨" : "완료";
            }

            return GetStageLabel(state, "완료");
        }

        private static string GetStageLabel(StepState state, string readyLabel)
        {
            if (state == StepState.Ready)
            {
                return readyLabel;
            }

            if (state == StepState.InProgress)
            {
                return "진행 필요";
            }

            if (state == StepState.Waiting)
            {
                return "대기중";
            }

            return state == StepState.Blocked ? "차단" : "필요";
        }

        private static void DrawV7StagePill(string number, string title, string detail, StepState state, bool isActive)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? ActionBlue : GetStepStateColor(state);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(42f));
            GUI.backgroundColor = previousBackground;
            DrawStepStateBorderBar(isActive ? StepState.InProgress : state);
            GUILayout.Label(number + ". " + title, EditorStyles.miniBoldLabel);
            GUILayout.Label(isActive ? "현재 작업" : detail, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawAvatarOutfitStep(WorkflowStatus workflow)
        {
            string currentId = string.IsNullOrEmpty(workflow.CurrentZepetoId) ? "ID 없음" : workflow.CurrentZepetoId;
            StepState state = GetSequentialStageState(workflow, 1);
            string summary = "아이디를 적용하고, 테스트할 의상 prefab을 직접 고른 뒤 Play로 한 번 확인합니다.";

            if (!BeginStep(1, "아바타와 의상 준비", "Avatar & Outfit", state, summary, state == StepState.Ready ? "완료" : null, true, GetActiveStageNumber(workflow) == 1, IsStageWaiting(workflow, 1)))
            {
                EndStep();
                return;
            }

            StepState idState = workflow.HasZepetoId ? StepState.Ready : StepState.Needed;
            BeginWorkflowBlock("1-1. 아이디 입력", idState);
            DrawStatusRow("현재 아이디", currentId);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(zepetoIdProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                zepetoIdText = EditorGUILayout.TextField("아이디", zepetoIdText);
            }
            if (EditorGUI.EndChangeCheck())
            {
                zepetoIdText = SanitizeZepetoId(zepetoIdText);
            }

            bool canApplyId = zepetoIdProperty != null
                && !string.IsNullOrEmpty(SanitizeZepetoId(zepetoIdText))
                && !string.Equals(SanitizeZepetoId(zepetoIdText), SanitizeZepetoId(workflow.CurrentZepetoId), StringComparison.OrdinalIgnoreCase)
                && !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(!canApplyId))
            {
                if (DrawBlueActionButton("ID 적용", canApplyId, GUILayout.Height(30f)))
                {
                    ApplyZepetoId(zepetoIdText);
                }
            }

            if (loader == null)
            {
                DrawMiniHelp("LOADER가 없으면 ID를 적용할 대상이 없습니다. 먼저 LOADER를 찾습니다.", MessageType.Warning);
                if (DrawBlueActionButton("LOADER 찾기", !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(30f)))
                {
                    FindLoaderAndSerializedFields();
                    ValidateState();
                }
            }
            else
            {
                GUILayout.Label("LOADER 연결됨. 여기서는 아이디만 확인하면 됩니다.", EditorStyles.wordWrappedMiniLabel);
            }
            EndWorkflowBlock(idState);

            StepState outfitState = GetOutfitChoiceState(workflow);
            BeginWorkflowBlock("1-2. 의상 선택", outfitState);
            DrawOutfitChoiceRow(workflow);

            if (!workflow.HasOutfit)
            {
                GUILayout.Label("목록에서 prefab을 고른 뒤 파란 의상 적용 버튼을 눌러야 1-2가 완료됩니다.", EditorStyles.wordWrappedMiniLabel);
            }
            else if (!workflow.OutfitIsUnderContents)
            {
                DrawMiniHelp("의상 prefab은 보통 Assets/Contents 아래에 있어야 export 흐름이 안전합니다: " + workflow.OutfitPath, MessageType.Warning);
            }
            else
            {
                GUILayout.Label("선택된 prefab을 Play에서 아바타에 입혀 확인합니다.", EditorStyles.wordWrappedMiniLabel);
            }
            EndWorkflowBlock(outfitState);

            StepState previewState = workflow.CanPlayAvatarOutfit ? StepState.Ready : StepState.Needed;
            BeginWorkflowBlock("1-3. Play 확인", previewState);
            DrawStagePlayStopButtons(
                workflow.HasOutfit ? "Play로 아바타+의상 확인" : "Play로 아바타 확인",
                CanUseStagePlay(workflow, 1, workflow.CanPlayAvatarOutfit),
                "Stop",
                GetPlayDisabledReason(workflow, false),
                1);
            GUILayout.Label("Play로 실제 ZEPETO 로딩을 확인하고, 확인이 끝나면 Stop을 누릅니다.", EditorStyles.wordWrappedMiniLabel);
            EndWorkflowBlock(previewState);

            DrawAvatarOutfitApplyButton(workflow);

            EndStep();
        }

        private StepState GetOutfitChoiceState(WorkflowStatus workflow)
        {
            if (workflow.HasOutfit && pendingClothingPrefab == clothingPrefab)
            {
                return StepState.Ready;
            }

            return pendingClothingPrefab == null ? StepState.Needed : StepState.InProgress;
        }

        private void DrawOutfitChoiceRow(WorkflowStatus workflow)
        {
            List<GameObject> prefabs = FindAllOutfitPrefabs();
            if (prefabs.Count == 0)
            {
                DrawStatusRow("의상 / Outfit", "Assets/Contents 아래 prefab 없음");
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (DrawBlueActionButton("의상 목록 새로고침", !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(28f)))
                    {
                        FindDefaultClothingPrefab();
                        ValidateState();
                    }
                }

                return;
            }

            if (pendingClothingPrefab == null && clothingPrefab != null)
            {
                pendingClothingPrefab = clothingPrefab;
            }

            string[] options = BuildOutfitPopupOptions(prefabs);
            int currentIndex = GetOutfitPopupIndex(prefabs, pendingClothingPrefab);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                currentIndex = EditorGUILayout.Popup("의상 선택", currentIndex, options);
            }

            if (EditorGUI.EndChangeCheck())
            {
                pendingClothingPrefab = currentIndex <= 0 ? null : prefabs[currentIndex - 1];
                SetAvatarOutfitStageComplete(false);
                ValidateState();
                Repaint();
            }

            GUILayout.Label("목록에서 테스트할 prefab을 직접 선택합니다.", EditorStyles.wordWrappedMiniLabel);

            bool hasPendingOutfit = pendingClothingPrefab != null;
            bool isSameAsApplied = pendingClothingPrefab != null && pendingClothingPrefab == clothingPrefab;
            string applyLabel = isSameAsApplied ? "의상 적용됨" : "의상 적용";
            if (DrawBlueActionButton(applyLabel, hasPendingOutfit && !isSameAsApplied && !EditorApplication.isPlayingOrWillChangePlaymode, GUILayout.Height(30f)))
            {
                ApplySelectedOutfitPrefab();
            }

            if (workflow.HasOutfit)
            {
                DrawStatusRow("적용된 의상", clothingPrefab.name);
            }
        }

        private void ApplySelectedOutfitPrefab()
        {
            if (pendingClothingPrefab == null)
            {
                statusMessage = "적용할 의상 prefab을 먼저 선택하세요.";
                ValidateState();
                return;
            }

            clothingPrefab = pendingClothingPrefab;
            SetAvatarOutfitStageComplete(false);
            SetClipStageComplete(false);
            statusMessage = "의상 적용됨: " + clothingPrefab.name + ". Play로 확인한 뒤 1번 적용 / 다음 단계를 누르세요.";
            ValidateState();
            Repaint();
        }

        private void DrawAvatarOutfitApplyButton(WorkflowStatus workflow)
        {
            bool canComplete = workflow.HasAvatarPlayInputs
                && workflow.HasOutfit
                && workflow.OutfitIsUnderContents
                && !EditorApplication.isPlayingOrWillChangePlaymode;

            bool canLockCompletedStage = avatarOutfitStageComplete && GetReadyStageUnlocked(1);
            if (DrawBlueActionButton(avatarOutfitStageComplete ? "1번 적용 완료 / 잠금" : "1번 적용 / 다음 단계", canComplete && (!avatarOutfitStageComplete || canLockCompletedStage), GUILayout.Height(34f)))
            {
                SetAvatarOutfitStageComplete(true);
                statusMessage = "1번 아바타와 의상 준비가 완료되었습니다. 이제 2번 동작 선택으로 진행하세요.";
                ValidateState();
                Repaint();
            }

            if (!canComplete && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DrawMiniHelp("1번 완료 조건: ID 적용, 의상 적용, Assets/Contents 아래 의상 prefab.", MessageType.None);
            }
        }

        private static string[] BuildOutfitPopupOptions(List<GameObject> prefabs)
        {
            string[] options = new string[prefabs.Count + 1];
            options[0] = "선택 안 함";
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                string path = AssetDatabase.GetAssetPath(prefab);
                options[i + 1] = string.IsNullOrEmpty(path) ? prefab.name : path.Substring(ContentsRoot.Length).TrimStart('/');
            }

            return options;
        }

        private static int GetOutfitPopupIndex(List<GameObject> prefabs, GameObject selectedPrefab)
        {
            if (selectedPrefab == null)
            {
                return 0;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == selectedPrefab)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private List<GameObject> FindAllOutfitPrefabs()
        {
            List<GameObject> prefabs = new List<GameObject>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ContentsRoot });
            Array.Sort(guids, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            return prefabs;
        }

        private static string GetPlayDisabledReason(WorkflowStatus workflow, bool requireAnimation)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                return workflow.Safety.Message;
            }

            if (!workflow.HasLoader)
            {
                return "LOADER를 먼저 찾아야 합니다.";
            }

            if (!workflow.HasZepetoId)
            {
                return "아이디를 먼저 적용해야 합니다.";
            }

            if (requireAnimation && !workflow.HasAssignedAnimation)
            {
                return "2단계에서 동작을 선택하고 사용해야 합니다.";
            }

            return string.Empty;
        }

        private bool CanUseStagePlay(WorkflowStatus workflow, int stage, bool baseCanPlay)
        {
            return baseCanPlay && GetActiveStageNumber(workflow) == stage;
        }

        private void DrawStagePlayStopButtons(string playLabel, bool canPlay, string stopLabel, string disabledReason = "", int stageToKeepOpen = -1)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            bool isThisStagePreview = stageToKeepOpen >= 0 && activePreviewStage == stageToKeepOpen;
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton(playLabel, canPlay && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                RequestPlayMode(stageToKeepOpen);
            }

            string visibleStopLabel = stopLabel.StartsWith("■", StringComparison.Ordinal) ? stopLabel : "■ " + stopLabel;
            if (DrawColoredActionButton(visibleStopLabel, isPlaying && isThisStagePreview, StopRed, GUILayout.Height(34f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            if (!canPlay && !isPlaying && !string.IsNullOrEmpty(disabledReason))
            {
                DrawMiniHelp("Play 비활성화: " + disabledReason, MessageType.None);
            }

            GUILayout.Label(
                isPlaying
                    ? "빨간 Stop을 누르면 Play 확인을 끝내고 편집/저장을 다시 할 수 있습니다."
                    : "Play로 확인한 뒤에는 빨간 Stop을 눌러 편집 상태로 돌아오세요.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSelectedMotionPlayStopButtons(WorkflowStatus workflow, bool isActiveStage)
        {
            bool isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            bool isStageAccessible = isActiveStage || !IsStageWaiting(workflow, 2);
            bool canPlaySelected = isStageAccessible && CanPlaySelectedMotion(workflow);
            bool isThisStagePreview = activePreviewStage == 2;
            EditorGUILayout.BeginHorizontal();
            if (DrawColoredActionButton("미리보기 Play", canPlaySelected && !isPlaying, PlayGreen, GUILayout.Height(34f)))
            {
                PlaySelectedMotionPreview();
            }

            string stopLabel = isTemporarySelectedMotionPreview ? "미리보기 Stop" : "Stop";
            string visibleStopLabel = stopLabel.StartsWith("■", StringComparison.Ordinal) ? stopLabel : "■ " + stopLabel;
            if (DrawColoredActionButton(visibleStopLabel, isPlaying && isThisStagePreview, StopRed, GUILayout.Height(34f)))
            {
                StopPlayMode();
            }
            EditorGUILayout.EndHorizontal();

            if (!canPlaySelected && !isPlaying)
            {
                string disabledReason = isStageAccessible
                    ? GetSelectedMotionPlayDisabledReason(workflow)
                    : "먼저 1번 아바타/의상 적용을 완료해야 합니다.";
                DrawMiniHelp("Play 비활성화: " + disabledReason, MessageType.None);
            }

            GUILayout.Label(
                isPlaying
                    ? "빨간 미리보기 Stop을 누르면 동작 확인을 끝내고 이전 작업 동작으로 돌아갑니다."
                    : "미리보기 Play로 확인한 뒤 빨간 Stop을 누르고, 마음에 들면 작업 동작으로 사용하세요.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static bool CanPlaySelectedMotion(WorkflowStatus workflow)
        {
            return workflow.HasAvatarPlayInputs
                && workflow.HasSelectedPackageAnimation
                && CanEnterPlayMode(workflow.Safety);
        }

        private static string GetSelectedMotionPlayDisabledReason(WorkflowStatus workflow)
        {
            if (workflow.Safety.HasBlockingRisk)
            {
                return workflow.Safety.Message;
            }

            if (!workflow.HasLoader)
            {
                return "LOADER를 먼저 찾아야 합니다.";
            }

            if (!workflow.HasZepetoId)
            {
                return "아이디를 먼저 적용해야 합니다.";
            }

            if (!workflow.HasSelectedPackageAnimation)
            {
                return "검색하거나 v 버튼으로 재생할 동작을 먼저 선택하세요.";
            }

            return "Unity가 컴파일/갱신 중이면 끝난 뒤 다시 누르세요.";
        }

        private void DrawMotionBrowser(WorkflowStatus workflow)
        {
            StepState state = GetSequentialStageState(workflow, 2);
            string summary = workflow.HasEditableAssignedAnimation
                ? "편집 가능한 동작이 LOADER에 연결되어 있습니다: " + workflow.AssignedAnimation.name
                : "동작 dropdown에서 고른 뒤 미리보기 Play로 먼저 확인합니다. 확정은 작업 동작으로 사용을 누르세요.";
            if (!BeginStep(2, "동작 선택", "Motion Select", state, summary, state == StepState.Ready ? "완료" : null, true, GetActiveStageNumber(workflow) == 2, IsStageWaiting(workflow, 2)))
            {
                EndStep();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(packageAnimations.Count + " clips", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            DrawMotionChoiceRow(workflow);

            if (packageAnimations.Count == 0)
            {
                DrawMiniHelp("ZEPETO 기본 동작을 찾지 못했습니다. Scan을 눌러 다시 확인하세요.", MessageType.Warning);
                if (DrawPrimaryButton("다시 찾기", true))
                {
                    LoadPackageAnimations();
                }

                EndStep();
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected != null)
            {
                DrawStatusRow("선택 / Selected", selected.name + "  " + FormatClipLength(selected));
            }

            DrawSelectedMotionPlayStopButtons(workflow, GetActiveStageNumber(workflow) == 2);
            DrawUseSelectedMotionButton(workflow);
            EndStep();
        }

        private void DrawMotionChoiceRow(WorkflowStatus workflow)
        {
            if (selectedAnimationIndex < 0 && packageAnimations.Count > 0)
            {
                SelectPackageAnimation(0);
            }

            if (packageAnimationNames.Length == 0)
            {
                DrawStatusRow("동작 / Motion", "선택 가능한 기본 동작이 없습니다.");
                return;
            }

            int currentIndex = Mathf.Clamp(selectedAnimationIndex, 0, packageAnimationNames.Length - 1);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                currentIndex = EditorGUILayout.Popup("동작 / Motion", currentIndex, packageAnimationNames);
            }
            if (EditorGUI.EndChangeCheck())
            {
                SelectPackageAnimation(currentIndex);
            }

            DrawStatusRow("연결된 동작", workflow.AssignedAnimation == null ? "없음" : workflow.AssignedAnimation.name);

            DrawMiniHelp("동작을 먼저 고른 뒤 미리보기 Play로 확인하세요. 마음에 들면 아래에서 작업 동작으로 확정합니다.", MessageType.None);
        }

        private void DrawUseSelectedMotionButton(WorkflowStatus workflow)
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            bool selectedAlreadyAssigned = selected != null
                && workflow.AssignedAnimation != null
                && IsClipDerivedFromPackage(workflow.AssignedAnimation, selected);
            bool canCompleteAssignedStage = selectedAlreadyAssigned && !motionSelectStageComplete && !EditorApplication.isPlayingOrWillChangePlaymode;
            bool canLockCompletedStage = selectedAlreadyAssigned && motionSelectStageComplete && GetReadyStageUnlocked(2) && !EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUI.DisabledScope(selected == null || (selectedAlreadyAssigned && !canCompleteAssignedStage && !canLockCompletedStage) || animationClipProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawColoredActionButton(selectedAlreadyAssigned && motionSelectStageComplete ? "2번 적용 완료 / 잠금" : "2번 적용 / 작업 동작으로 사용", selected != null && (!selectedAlreadyAssigned || canCompleteAssignedStage || canLockCompletedStage) && animationClipProperty != null && !EditorApplication.isPlayingOrWillChangePlaymode, ActionBlue, GUILayout.Height(34f)))
                {
                    if (selectedAlreadyAssigned)
                    {
                        SetMotionSelectStageComplete(true);
                        statusMessage = "2번 동작 선택이 완료되어 잠겼습니다. 이제 3번 클립 조정으로 진행하세요.";
                        Repaint();
                    }
                    else
                    {
                        UseSelectedAnimation();
                    }
                }
            }
        }
        private void DrawClipAdjust(WorkflowStatus workflow)
        {
            AnimationClip assignedClip = GetAssignedAnimationClip();
            EnsureClipAdjustDefaults(assignedClip);

            StepState state = GetSequentialStageState(workflow, 3);
            string summary = workflow.CanClipEdit
                ? "배속, 반복, 시작/끝 시간을 조정합니다. Play 중에는 화면 확인만 하고 Stop 후 새 .anim으로 저장합니다."
                : "먼저 2단계에서 동작을 사용해서 Assets/ZepetoHelper/Animations 아래 복사본을 연결하세요.";

            if (!BeginStep(3, "클립 조정", "Clip Adjust", state, summary, state == StepState.Ready ? "완료" : null, true, GetActiveStageNumber(workflow) == 3, IsStageWaiting(workflow, 3)))
            {
                EndStep();
                return;
            }

            float clipLength = assignedClip == null ? 0f : Mathf.Max(0.01f, assignedClip.length);
            DrawStatusRow("대상 clip / Target", assignedClip == null ? "없음" : assignedClip.name + "  " + FormatClipLength(assignedClip));

            using (new EditorGUI.DisabledScope(assignedClip == null))
            {
                EditorGUI.BeginChangeCheck();
                motionPreviewSpeed = EditorGUILayout.Slider("재생 속도 / Speed", Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f), 0.25f, 2f);
                clipTrimStart = EditorGUILayout.Slider("시작 시간 / Start", Mathf.Clamp(clipTrimStart, 0f, clipLength), 0f, clipLength);
                clipTrimEnd = EditorGUILayout.Slider("끝 시간 / End", Mathf.Clamp(clipTrimEnd, clipTrimStart + 0.01f, clipLength), 0f, clipLength);
                clipLoop = EditorGUILayout.Toggle("저장된 clip 반복 재생 / Loop Saved Clip", clipLoop);
                if (clipTrimEnd <= clipTrimStart)
                {
                    clipTrimEnd = Mathf.Min(clipLength, clipTrimStart + 0.01f);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // [QA][State:dirty_stage]
                    // Any speed/range/loop change invalidates the previously completed clip step.
                    // The user must press the blue step-3 apply button again so the saved .anim matches the UI.
                    SetClipStageComplete(false);
                    SaveClipAdjustSessionState(GetClipAdjustStatePath(assignedClip));
                }
            }

            DrawMiniHelp("저장 결과: 2.0x는 길이가 절반, 0.5x는 길이가 두 배가 됩니다. 원본 package와 기존 복사본은 직접 수정하지 않습니다. 반복 재생은 저장될 clip의 Loop 설정입니다.", MessageType.None);

            DrawStagePlayStopButtons("Play로 배속 확인", CanUseStagePlay(workflow, 3, workflow.CanPlayMotion), "Stop", GetPlayDisabledReason(workflow, true), 3);

            bool hasClipAdjustInput = HasClipAdjustInput(assignedClip);
            string applyLabel = hasClipAdjustInput ? "3번 적용 / 저장 후 다음 단계" : "3번 적용 / 다음 단계";
            bool canApply = workflow.CanClipEdit && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (DrawColoredActionButton(applyLabel, canApply, ActionBlue, GUILayout.Height(34f)))
            {
                if (hasClipAdjustInput)
                {
                    SaveClipAdjustToCurrentClip();
                }
                else
                {
                    SetClipStageComplete(true);
                    statusMessage = "클립 조정을 완료했습니다. 이제 4번 저장/Export 단계로 이동하세요.";
                    Repaint();
                }
            }

            showClipAdvancedOptions = EditorGUILayout.Foldout(showClipAdvancedOptions, "고급 / Advanced", true);
            if (showClipAdvancedOptions)
            {
                DrawStatusRow("저장 위치 / Save Folder", ClipEditRoot);
                if (lastClipEditedClip != null)
                {
                    DrawStatusRow("마지막 clip edit", lastClipEditedClip.name);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (DrawSecondaryButton("열기", GUILayout.Width(64f)))
                    {
                        SelectAndPing(lastClipEditedClip);
                        EditorApplication.ExecuteMenuItem("Window/Animation/Animation");
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EndStep();
        }
        private void DrawSaveExportStep(WorkflowStatus workflow)
        {
            StepState state = GetSequentialStageState(workflow, 4);
            string summary = workflow.HasEditableAssignedAnimation
                ? "저장된 결과를 다시 Play로 확인한 뒤 Stop 상태에서 공식 export 메뉴를 엽니다."
                : "먼저 동작을 선택하고 필요한 clip 조정을 저장하세요.";

            if (!BeginStep(4, "저장과 내보내기", "Save & Export", state, summary, null, false, GetActiveStageNumber(workflow) == 4, IsStageWaiting(workflow, 4)))
            {
                EndStep();
                return;
            }

            string exportPackagePath = GetExpectedZepetoPackagePath();
            DrawStatusRow("Export 대상 동작", workflow.AssignedAnimation == null ? "없음" : workflow.AssignedAnimation.name);
            DrawStatusRow("출력 파일", GetExportPackageStatusText(exportPackagePath));
            using (new EditorGUI.DisabledScope(!ExportPackageExists(exportPackagePath)))
            {
                if (DrawSecondaryButton("출력 파일 선택", GUILayout.Height(26f)))
                {
                    SelectAndPing(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportPackagePath));
                }
            }

            DrawStagePlayStopButtons("Play로 저장 결과 확인", CanUseStagePlay(workflow, 4, workflow.CanPlayMotion), "Stop", GetPlayDisabledReason(workflow, true), 4);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !workflow.HasOutfit))
            {
                if (DrawColoredActionButton("4번 완료 / .zepeto 생성", workflow.HasOutfit && !EditorApplication.isPlayingOrWillChangePlaymode, ActionBlue, GUILayout.Height(34f)))
                {
                    OpenExportMenu();
                }
            }

            DrawMiniHelp("Export는 Unity Play가 꺼진 상태에서만 실행합니다. 실제 업로드와 로그인은 공식 ZEPETO Studio 웹 흐름에서 진행합니다.", MessageType.None);
            EndStep();
        }

        private void DrawSetupFoldout(WorkflowStatus workflow)
        {
            showDetailedWorkflow = EditorGUILayout.Foldout(showDetailedWorkflow, "작업 준비 / Setup", true);
            if (!showDetailedWorkflow)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            DrawStatusRow("아바타 / Avatar", string.IsNullOrEmpty(workflow.CurrentZepetoId) ? "ID 없음" : workflow.CurrentZepetoId);
            DrawStatusRow("의상 / Outfit", workflow.HasOutfit ? clothingPrefab.name : "없음");
            DrawStatusRow("동작 / Motion", workflow.HasAssignedAnimation ? workflow.AssignedAnimation.name : "없음");

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("씬 열기", GUILayout.Height(28f)))
            {
                OpenPlaygroundScene();
            }

            if (DrawSecondaryButton("LOADER", GUILayout.Height(28f)))
            {
                FindLoaderAndSerializedFields();
                ValidateState();
            }

            using (new EditorGUI.DisabledScope(zepetoIdProperty == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("ID 적용", GUILayout.Height(28f)))
                {
                    zepetoIdText = GetDefaultZepetoIdForAction();
                    ApplyZepetoId(zepetoIdText);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("의상 찾기", GUILayout.Height(28f)))
            {
                FindDefaultClothingPrefab();
                ValidateState();
            }

            using (new EditorGUI.DisabledScope(animatorControllerProperty == null || EditorApplication.isPlayingOrWillChangePlaymode || workflow.HasLocalAnimatorController))
            {
                if (DrawSecondaryButton("Controller Fix", GUILayout.Height(28f)))
                {
                    string controllerMessage;
                    statusMessage = EnsureLocalAnimatorController(out controllerMessage)
                        ? controllerMessage
                        : "Local controller fix failed: " + controllerMessage;
                    ValidateState();
                }
            }

            using (new EditorGUI.DisabledScope(!workflow.HasOutfit || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (DrawSecondaryButton("Export", GUILayout.Height(28f)))
                {
                    OpenExportMenu();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiagnosticsFoldout(WorkflowStatus workflow)
        {
            showDiagnosticsAdvanced = EditorGUILayout.Foldout(showDiagnosticsAdvanced, "문제 해결 / Diagnostics", true);
            if (!showDiagnosticsAdvanced)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            if (DrawSecondaryButton("복구", GUILayout.Height(28f)))
            {
                RecoverSafetyState();
            }

            if (DrawSecondaryButton("MCP 복구", GUILayout.Height(28f)))
            {
                string mcpMessage;
                statusMessage = TryRestartUnityMcpBridge(out mcpMessage)
                    ? "MCP bridge restart requested. " + mcpMessage
                    : "MCP bridge restart failed: " + mcpMessage;
                RefreshSafetySnapshot();
            }

            if (DrawSecondaryButton("Console 정리", GUILayout.Height(28f)))
            {
                ClearConsoleAndSessionSummary();
            }

            if (DrawSecondaryButton("검증", GUILayout.Height(28f)))
            {
                ValidateState();
                statusMessage = "Validation complete. / 검증 완료";
            }
            EditorGUILayout.EndHorizontal();

            DrawStatusRow("MCP", BuildMcpStatusText(workflow.Safety));
            DrawStatusRow("Console", string.Format("Warnings {0} / Errors {1}", sessionWarningCount, sessionErrorCount));
            DrawStatusRow("Controller", string.IsNullOrEmpty(workflow.AnimatorControllerPath) ? "없음" : workflow.AnimatorControllerPath);

            if (validationMessages.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(5, validationMessages.Count); i++)
                {
                    DrawMiniHelp(validationMessages[i].Text, validationMessages[i].Type);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void SelectPackageAnimation(int animationIndex)
        {
            if (animationIndex < 0 || animationIndex >= packageAnimations.Count)
            {
                return;
            }

            selectedAnimationIndex = animationIndex;
            copiedAnimationClip = FindCopiedAnimationForPackage(packageAnimations[animationIndex]);
            SetMotionSelectStageComplete(false);
        }

        private AnimationClip FindCopiedAnimationForPackage(AnimationClip packageClip)
        {
            if (packageClip == null || !AssetDatabase.IsValidFolder(AnimationCopyRoot))
            {
                return null;
            }

            string expectedName = packageClip.name + "_editable";
            string[] guids = AssetDatabase.FindAssets(expectedName + " t:AnimationClip", new[] { AnimationCopyRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(fileName)
                    && fileName.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private bool UseSelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작을 복사하거나 연결하지 않습니다. 먼저 Stop을 누르세요.";
                return false;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "사용할 동작을 먼저 선택하세요.";
                return false;
            }

            AnimationClip existingCopy = FindCopiedAnimationForPackage(selected);
            if (existingCopy != null)
            {
                copiedAnimationClip = existingCopy;
                if (AssignAnimationClip(existingCopy))
                {
                    if (completeStageAfterAssign)
                    {
                        SetMotionSelectStageComplete(true);
                    }

                    SelectAndPing(existingCopy);
                    statusMessage = "복사된 동작을 사용합니다: " + existingCopy.name;
                    return true;
                }

                return false;
            }

            CopySelectedAnimation(completeStageAfterAssign);
            AnimationClip assignedClip = GetAssignedAnimationClip();
            return assignedClip != null && IsClipDerivedFromPackage(assignedClip, selected);
        }

        private void PlaySelectedMotionPreview()
        {
            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                statusMessage = "Play할 동작을 먼저 선택하세요.";
                return;
            }

            AnimationClip assignedClip = GetAssignedAnimationClip();
            if (!IsClipDerivedFromPackage(assignedClip, selected))
            {
                motionPreviewRestoreClip = assignedClip;
                isTemporarySelectedMotionPreview = true;
                if (!UseSelectedAnimation(false))
                {
                    isTemporarySelectedMotionPreview = false;
                    motionPreviewRestoreClip = null;
                    statusMessage = "선택한 동작을 LOADER에 연결하지 못했습니다. Console과 Validation을 확인하세요.";
                    ValidateState();
                    return;
                }
            }
            else
            {
                isTemporarySelectedMotionPreview = false;
                motionPreviewRestoreClip = null;
            }

            RequestPlayMode(2);
            if (!EditorApplication.isPlayingOrWillChangePlaymode && isTemporarySelectedMotionPreview)
            {
                RestoreTemporarySelectedMotionPreview();
            }
        }

        private void RestoreTemporarySelectedMotionPreview()
        {
            if (!isTemporarySelectedMotionPreview)
            {
                return;
            }

            AnimationClip restoreClip = motionPreviewRestoreClip;
            isTemporarySelectedMotionPreview = false;
            motionPreviewRestoreClip = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode || animationClipProperty == null)
            {
                return;
            }

            Undo.RecordObject(animationClipObject.targetObject, "Restore ZEPETO Preview Animation");
            animationClipObject.Update();
            animationClipProperty.objectReferenceValue = restoreClip;
            animationClipObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animationClipObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            statusMessage = restoreClip == null
                ? "미리보기 종료: 이전 작업 동작이 없어 연결을 비웠습니다."
                : "미리보기 종료: 이전 작업 동작으로 되돌렸습니다. (" + restoreClip.name + ")";
            ValidateState();
        }

        private bool PrepareClipAdjustPreviewBeforePlay()
        {
            AnimationClip sourceClip = GetAssignedAnimationClip();
            if (!HasClipAdjustInput(sourceClip))
            {
                RestoreTemporaryClipAdjustPreview();
                return true;
            }

            RestoreTemporaryClipAdjustPreview();
            sourceClip = GetAssignedAnimationClip();

            string reason;
            if (!CanEditAnimationClip(sourceClip, out reason))
            {
                statusMessage = reason;
                ValidateState();
                return false;
            }

            string restorePath = AssetDatabase.GetAssetPath(sourceClip);
            SaveClipAdjustSessionState(restorePath);
            ClipEditSettings settings = BuildClipEditSettings(sourceClip);
            // [AUDIT][Risk:Major][Scope:play_preview]
            // Preview Play must never mutate the working clip. A temporary asset is assigned to LOADER,
            // and OnPlayModeStateChanged restores restorePath after Play exits.
            ClipEditResult result = ClipEditUtility.CreateClipAdjustedPreviewClip(sourceClip, settings, ClipAdjustPreviewPath);
            if (!result.Success || result.Clip == null)
            {
                statusMessage = "배속 미리보기 clip을 만들지 못했습니다: " + result.Message;
                ValidateState();
                return false;
            }

            if (!AssignAnimationClip(result.Clip, true))
            {
                AssetDatabase.DeleteAsset(ClipAdjustPreviewPath);
                statusMessage = "배속 미리보기 clip을 LOADER에 연결하지 못했습니다.";
                ValidateState();
                return false;
            }

            SessionState.SetBool(ClipAdjustPreviewActiveSessionKey, true);
            SessionState.SetString(ClipAdjustPreviewRestorePathSessionKey, restorePath ?? string.Empty);
            statusMessage = "배속 미리보기 clip을 임시 연결했습니다. Stop 후 원래 작업 clip으로 돌아갑니다.";
            return true;
        }

        private void RestoreTemporaryClipAdjustPreview()
        {
            bool isPreviewActive = SessionState.GetBool(ClipAdjustPreviewActiveSessionKey, false);
            if (!isPreviewActive && AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipAdjustPreviewPath) == null)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string restorePath = SessionState.GetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
            AnimationClip restoreClip = string.IsNullOrEmpty(restorePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<AnimationClip>(restorePath);

            if (restoreClip != null)
            {
                FindLoaderAndSerializedFields();
                AssignAnimationClip(restoreClip, true);
            }

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipAdjustPreviewPath) != null)
            {
                AssetDatabase.DeleteAsset(ClipAdjustPreviewPath);
            }

            SessionState.SetBool(ClipAdjustPreviewActiveSessionKey, false);
            SessionState.SetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
            statusMessage = restoreClip == null
                ? "배속 미리보기 종료: 복구할 원래 clip을 찾지 못했습니다."
                : "배속 미리보기 종료: 원래 작업 clip으로 되돌렸습니다. (" + restoreClip.name + ")";
            ValidateState();
        }

        private void SelectAndFrameLoader()
        {
            if (loader == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (loader == null)
            {
                statusMessage = "LOADER를 찾지 못했습니다. 작업 준비 / Setup에서 LOADER를 다시 찾으세요.";
                return;
            }

            FrameLoaderForScenePreview();

            statusMessage = "Scene View에서 LOADER를 선택했습니다. 아바타와 의상 관통을 확인하세요.";
        }
        private static bool IsClipDerivedFromPackage(AnimationClip clip, AnimationClip packageClip)
        {
            if (clip == null || packageClip == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase)
                && clip.name.StartsWith(packageClip.name, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatClipLength(AnimationClip clip)
        {
            return clip == null ? "0.00s" : clip.length.ToString("0.00") + "s";
        }
        private static string BuildMcpStatusText(SafetySnapshot snapshot)
        {
            int port = GetUnityMcpBridgePort();
            if (port > 0)
            {
                return CanPingUnityMcpBridge(port)
                    ? "Bridge ping OK: " + port
                    : "Bridge port " + port + " is not responding";
            }

            string detail = (snapshot.Detail ?? string.Empty) + "\n" + (lastConsoleMessage ?? string.Empty);
            if (detail.IndexOf("No Unity Editor instances found", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("instance_count\":0", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("instance_count: 0", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "MCP disconnected: instances=0";
            }

            if (detail.IndexOf("UnityMcpBridge started", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("port 6401", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Bridge log detected: 6401";
            }

            return "Not required for helper / helper 필수 아님";
        }

        private static int GetUnityMcpBridgePort()
        {
            try
            {
                Type bridgeType = Type.GetType("UnityMcpBridge.Editor.UnityMcpBridge, UnityMcpBridge.Editor");
                MethodInfo getCurrentPort = bridgeType == null
                    ? null
                    : bridgeType.GetMethod("GetCurrentPort", BindingFlags.Public | BindingFlags.Static);
                if (getCurrentPort == null)
                {
                    return 0;
                }

                object value = getCurrentPort.Invoke(null, null);
                return value is int ? (int)value : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool CanPingUnityMcpBridge(int port)
        {
            if (port <= 0)
            {
                return false;
            }

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect("127.0.0.1", port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(300))
                    {
                        return false;
                    }

                    client.EndConnect(connect);
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] ping = System.Text.Encoding.UTF8.GetBytes("ping");
                        stream.Write(ping, 0, ping.Length);
                        stream.Flush();

                        byte[] buffer = new byte[512];
                        stream.ReadTimeout = 500;
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead <= 0)
                        {
                            return false;
                        }

                        string text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        return text.IndexOf("\"message\":\"pong\"", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf("pong", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRestartUnityMcpBridge(out string message)
        {
            try
            {
                Type bridgeType = Type.GetType("UnityMcpBridge.Editor.UnityMcpBridge, UnityMcpBridge.Editor");
                if (bridgeType == null)
                {
                    message = "UnityMcpBridge.Editor assembly was not found.";
                    return false;
                }

                MethodInfo stop = bridgeType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static);
                MethodInfo startAutoConnect = bridgeType.GetMethod("StartAutoConnect", BindingFlags.Public | BindingFlags.Static);
                if (startAutoConnect == null)
                {
                    message = "StartAutoConnect method was not found.";
                    return false;
                }

                try
                {
                    if (stop != null)
                    {
                        stop.Invoke(null, null);
                    }
                }
                catch (Exception stopException)
                {
                    Debug.LogWarning("Easy ZEPETO Helper could not stop Unity MCP bridge before restart: " + stopException.Message);
                }

                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        startAutoConnect.Invoke(null, null);
                    }
                    catch (Exception startException)
                    {
                        Debug.LogWarning("Easy ZEPETO Helper could not start Unity MCP bridge: " + startException.Message);
                    }
                };

                message = "Stop now, StartAutoConnect on next editor tick.";
                return true;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }
        }
        private bool BeginStep(int number, string koreanTitle, string englishTitle, StepState state, string summary, string stateLabelOverride = null, bool autoCollapseWhenReady = false, bool isActiveStage = false, bool isWaitingLocked = false)
        {
            DrawStageDivider();
            EditorGUILayout.Space(8f);
            return DrawStepCard(number, koreanTitle, englishTitle, state, summary, stateLabelOverride, autoCollapseWhenReady, isActiveStage, isWaitingLocked);
        }

        private bool DrawStepCard(int number, string koreanTitle, string englishTitle, StepState state, string summary, string stateLabelOverride = null, bool autoCollapseWhenReady = false, bool isActiveStage = false, bool isWaitingLocked = false)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isActiveStage ? ActionBlue : GetStepStateColor(state);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBackground;
            DrawStepStateBorderBar(isActiveStage ? StepState.InProgress : state);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(number + ". " + koreanTitle + " / " + englishTitle, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (isActiveStage)
            {
                DrawColoredBadge("현재 작업", ActionBlue, 78f);
                GUILayout.Space(4f);
            }
            DrawStateBadge(string.IsNullOrEmpty(stateLabelOverride) ? GetStepStateLabel(state) : stateLabelOverride, state, 76f);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(summary))
            {
                if (state == StepState.Ready)
                {
                    GUILayout.Label(summary, EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    DrawMiniHelp(summary, GetStepStateMessageType(state));
                }
            }

            if (isWaitingLocked)
            {
                DrawMiniHelp("이전 단계를 완료하면 열립니다. 지금은 이 단계의 버튼을 누를 수 없습니다.", MessageType.None);
                return false;
            }

            if (autoCollapseWhenReady && state == StepState.Ready && !isActiveStage)
            {
                int index = Mathf.Clamp(number, 0, unlockedReadyStages.Length - 1);
                bool isUnlocked = GetReadyStageUnlocked(index);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                string lockLabel = isUnlocked ? "수정 다시 잠금" : "수정 잠금 해제";
                Color lockColor = isUnlocked ? NeededAmber : ActionBlue;
                if (DrawColoredActionButton(lockLabel, true, lockColor, GUILayout.Width(128f), GUILayout.Height(26f)))
                {
                    SetReadyStageUnlocked(index, !isUnlocked);
                    isUnlocked = GetReadyStageUnlocked(index);
                }
                EditorGUILayout.EndHorizontal();

                if (!isUnlocked)
                {
                    GUILayout.Label("완료된 단계라 잠겨 있습니다. 바꾸려면 잠금 해제를 누르세요.", EditorStyles.wordWrappedMiniLabel);
                }

                return isUnlocked;
            }

            return true;
        }

        private void LoadUnlockedReadyStages()
        {
            for (int i = 0; i < unlockedReadyStages.Length; i++)
            {
                unlockedReadyStages[i] = SessionState.GetBool(StageUnlockSessionKeyPrefix + i, false);
            }
        }

        private void SaveUnlockedReadyStages()
        {
            for (int i = 0; i < unlockedReadyStages.Length; i++)
            {
                SessionState.SetBool(StageUnlockSessionKeyPrefix + i, unlockedReadyStages[i]);
            }
        }

        private bool GetReadyStageUnlocked(int index)
        {
            int clampedIndex = Mathf.Clamp(index, 0, unlockedReadyStages.Length - 1);
            bool storedValue = SessionState.GetBool(StageUnlockSessionKeyPrefix + clampedIndex, unlockedReadyStages[clampedIndex]);
            unlockedReadyStages[clampedIndex] = storedValue;
            return storedValue;
        }

        private void SetReadyStageUnlocked(int index, bool isUnlocked)
        {
            int clampedIndex = Mathf.Clamp(index, 0, unlockedReadyStages.Length - 1);
            unlockedReadyStages[clampedIndex] = isUnlocked;
            SessionState.SetBool(StageUnlockSessionKeyPrefix + clampedIndex, isUnlocked);
        }

        private void LoadWorkflowStageProgress()
        {
            avatarOutfitStageComplete = SessionState.GetBool(AvatarOutfitStageCompleteSessionKey, false);
            motionSelectStageComplete = SessionState.GetBool(MotionSelectStageCompleteSessionKey, false);
            clipStageComplete = SessionState.GetBool(ClipStageCompleteSessionKey, false);
            activePreviewStage = SessionState.GetInt(ActivePreviewStageSessionKey, -1);
        }

        private void SaveWorkflowStageProgress()
        {
            SessionState.SetBool(AvatarOutfitStageCompleteSessionKey, avatarOutfitStageComplete);
            SessionState.SetBool(MotionSelectStageCompleteSessionKey, motionSelectStageComplete);
            SessionState.SetBool(ClipStageCompleteSessionKey, clipStageComplete);
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
        }

        private void SetAvatarOutfitStageComplete(bool isComplete)
        {
            avatarOutfitStageComplete = isComplete;
            SessionState.SetBool(AvatarOutfitStageCompleteSessionKey, isComplete);
            if (isComplete)
            {
                SetReadyStageUnlocked(1, false);
            }

            if (!isComplete)
            {
                SetMotionSelectStageComplete(false);
                SetClipStageComplete(false);
            }
        }

        private void SetMotionSelectStageComplete(bool isComplete)
        {
            motionSelectStageComplete = isComplete;
            SessionState.SetBool(MotionSelectStageCompleteSessionKey, isComplete);
            if (isComplete)
            {
                SetReadyStageUnlocked(2, false);
            }

            if (!isComplete)
            {
                SetClipStageComplete(false);
            }
        }

        private void SetClipStageComplete(bool isComplete)
        {
            clipStageComplete = isComplete;
            SessionState.SetBool(ClipStageCompleteSessionKey, isComplete);
            if (isComplete)
            {
                SetReadyStageUnlocked(3, false);
            }
        }

        private static void DrawStepStateBorderBar(StepState state)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, GetStepStateColor(state));
        }

        private static void DrawStageDivider()
        {
            EditorGUILayout.Space(9f);
            Rect outer = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(outer, new Color(0.10f, 0.10f, 0.10f, 1f));
            Rect inner = new Rect(outer.x, outer.y + 2f, outer.width, 1f);
            EditorGUI.DrawRect(inner, new Color(0.42f, 0.42f, 0.42f, 1f));
        }

        private static void BeginWorkflowBlock(string title, StepState state)
        {
            EditorGUILayout.Space(5f);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = GetStepStateColor(state);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBackground;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawStateBadge(GetWorkflowBlockStateLabel(state), state, 62f);
            EditorGUILayout.EndHorizontal();
        }

        private static void EndWorkflowBlock(StepState state)
        {
            EditorGUILayout.EndVertical();
        }

        private static string GetWorkflowBlockStateLabel(StepState state)
        {
            if (state == StepState.Ready)
            {
                return "완료";
            }

            if (state == StepState.InProgress)
            {
                return "진행 필요";
            }

            if (state == StepState.Waiting)
            {
                return "대기중";
            }

            return state == StepState.Blocked ? "차단" : "필요";
        }

        private static void DrawStateBadge(string label, StepState state, float width)
        {
            DrawColoredBadge(label, GetStepStateColor(state), width);
        }

        private static void DrawColoredBadge(string label, Color color, float width)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedHeight = 20f
            };
            GUILayout.Label(label, style, GUILayout.Width(width), GUILayout.Height(20f));
            GUI.backgroundColor = previousBackground;
        }

        private static void EndStep()
        {
            EditorGUILayout.EndVertical();
        }

        private static bool DrawPrimaryButton(string label, bool enabled)
        {
            Color previousBackground = GUI.backgroundColor;
            if (enabled)
            {
                GUI.backgroundColor = ActionBlue;
            }

            bool clicked;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                clicked = GUILayout.Button(label, GUILayout.Height(30f));
            }

            GUI.backgroundColor = previousBackground;
            return clicked;
        }
        private static bool DrawSecondaryButton(string label, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, options);
        }

        private static bool DrawBlueActionButton(string label, bool enabled, params GUILayoutOption[] options)
        {
            return DrawColoredActionButton(label, enabled, ActionBlue, options);
        }

        private static bool DrawColoredActionButton(string label, bool enabled, Color activeColor, params GUILayoutOption[] options)
        {
            Color previousBackground = GUI.backgroundColor;
            if (enabled)
            {
                GUI.backgroundColor = activeColor;
            }

            bool clicked;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                clicked = GUILayout.Button(label, options);
            }

            GUI.backgroundColor = previousBackground;
            return clicked;
        }
        private static bool DrawAdvancedFoldout(bool value)
        {
            return EditorGUILayout.Foldout(value, "고급 / Advanced", true);
        }

        private static void DrawMiniHelp(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }

        private static void DrawStatusRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150f));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private WorkflowStatus BuildWorkflowStatus(SafetySnapshot snapshot)
        {
            WorkflowStatus workflow = new WorkflowStatus();
            workflow.Safety = snapshot;
            workflow.CurrentZepetoId = GetCurrentZepetoId();
            workflow.HasLoader = loader != null;
            workflow.HasZepetoIdField = zepetoIdProperty != null;
            workflow.HasZepetoId = !string.IsNullOrEmpty(workflow.CurrentZepetoId);
            workflow.HasOutfit = clothingPrefab != null;
            workflow.OutfitPath = clothingPrefab == null ? string.Empty : AssetDatabase.GetAssetPath(clothingPrefab);
            workflow.OutfitIsUnderContents = !string.IsNullOrEmpty(workflow.OutfitPath)
                && workflow.OutfitPath.StartsWith(ContentsRoot + "/", StringComparison.OrdinalIgnoreCase);
            workflow.HasSelectedPackageAnimation = GetSelectedPackageAnimation() != null;
            workflow.HasCopiedAnimation = copiedAnimationClip != null;
            workflow.AssignedAnimation = GetAssignedAnimationClip();
            workflow.HasAssignedAnimation = workflow.AssignedAnimation != null;
            workflow.AssignedAnimationPath = workflow.AssignedAnimation == null ? string.Empty : AssetDatabase.GetAssetPath(workflow.AssignedAnimation);
            workflow.HasEditableAssignedAnimation = workflow.HasAssignedAnimation
                && workflow.AssignedAnimationPath.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase);
            workflow.AnimatorControllerPath = GetAnimatorControllerPath();
            workflow.HasLocalAnimatorController = !string.IsNullOrEmpty(workflow.AnimatorControllerPath)
                && !IsPackageOrPackageCachePath(workflow.AnimatorControllerPath);
            workflow.HasPreviewInputs = workflow.HasLoader && workflow.HasOutfit && workflow.HasAssignedAnimation && workflow.HasLocalAnimatorController;
            workflow.HasAvatarPlayInputs = workflow.HasLoader && workflow.HasZepetoId;
            workflow.HasMotionPlayInputs = workflow.HasAvatarPlayInputs && workflow.HasAssignedAnimation;
            workflow.CanPlay = workflow.HasPreviewInputs && CanEnterPlayMode(snapshot);
            workflow.CanPlayAvatarOutfit = workflow.HasAvatarPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanPlayMotion = workflow.HasMotionPlayInputs && CanEnterPlayMode(snapshot);
            workflow.CanClipEdit = workflow.HasEditableAssignedAnimation && !snapshot.HasBlockingRisk;
            return workflow;
        }
        private string GetDefaultZepetoIdForAction()
        {
            string id = SanitizeZepetoId(defaultZepetoId);
            return string.IsNullOrEmpty(id) ? BuiltInDefaultZepetoId : id;
        }
        private void OpenExportMenu()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 export 메뉴를 열지 않습니다. 먼저 Stop을 눌러주세요.";
                return;
            }

            if (clothingPrefab != null)
            {
                SelectAndPing(clothingPrefab);
            }

            string officialPackagePath = GetOfficialZepetoPackagePath();
            string expectedPackagePath = GetExpectedZepetoPackagePath();
            // [AUDIT][Risk:Major][Scope:zepeto_export]
            // The official SDK menu writes <outfit>.zepeto beside the prefab. The helper only post-processes that
            // local output into a readable filename and reports the final path in the UI.
            if (!EditorApplication.ExecuteMenuItem(ExportMenuPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not execute menu item: " + ExportMenuPath);
                statusMessage = "ZEPETO Export 메뉴를 실행하지 못했습니다: " + ExportMenuPath;
                return;
            }

            AssetDatabase.Refresh();
            string finalPackagePath = MoveOfficialExportToFriendlyName(officialPackagePath, expectedPackagePath);
            AssetDatabase.Refresh();
            FindLoaderAndSerializedFields();

            if (!string.IsNullOrEmpty(finalPackagePath) && File.Exists(ToAbsoluteProjectPath(finalPackagePath)))
            {
                ResetHelperConsoleSummaryAfterSuccessfulExport();
                statusMessage = "ZEPETO export 파일을 만들었습니다: " + finalPackagePath;
                UnityEngine.Object packageAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPackagePath);
                if (packageAsset != null)
                {
                    SelectAndPing(packageAsset);
                }
            }
            else
            {
                statusMessage = "ZEPETO Export 메뉴를 실행했습니다. Console의 ZEPETO archive 결과를 확인하세요.";
            }

            Repaint();
        }

        private string MoveOfficialExportToFriendlyName(string officialPackagePath, string friendlyPackagePath)
        {
            // [QC][Invariant:export_rename]
            // Rename only after the official output exists, and delete only the expected friendly target.
            // This prevents a failed SDK export from being reported as a successful helper export.
            if (string.IsNullOrEmpty(officialPackagePath))
            {
                return friendlyPackagePath;
            }

            if (string.IsNullOrEmpty(friendlyPackagePath)
                || string.Equals(officialPackagePath, friendlyPackagePath, StringComparison.OrdinalIgnoreCase))
            {
                return officialPackagePath;
            }

            if (!File.Exists(ToAbsoluteProjectPath(officialPackagePath)))
            {
                return friendlyPackagePath;
            }

            if (File.Exists(ToAbsoluteProjectPath(friendlyPackagePath)))
            {
                AssetDatabase.DeleteAsset(friendlyPackagePath);
            }

            string moveError = AssetDatabase.MoveAsset(officialPackagePath, friendlyPackagePath);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not rename export file: " + moveError);
                return officialPackagePath;
            }

            return friendlyPackagePath;
        }

        private void ResetHelperConsoleSummaryAfterSuccessfulExport()
        {
            sessionWarningCount = 0;
            sessionErrorCount = 0;
            lastConsoleMessage = string.Empty;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            lastSafetyRefreshTime = -1000d;
        }

        private string GetExpectedZepetoPackagePath()
        {
            // [QA][Acceptance:visible_output_path]
            // Step 4 reads this path before and after export so users can see exactly where the .zepeto file should be.
            string officialPath = GetOfficialZepetoPackagePath();
            if (string.IsNullOrEmpty(officialPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(officialPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            return folder + "/" + BuildFriendlyExportFileName();
        }

        private string GetOfficialZepetoPackagePath()
        {
            if (clothingPrefab == null)
            {
                return string.Empty;
            }

            string prefabPath = AssetDatabase.GetAssetPath(clothingPrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return string.Empty;
            }

            string folder = Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            folder = folder.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(prefabPath) + ".zepeto";
            return folder + "/" + fileName;
        }

        private string BuildFriendlyExportFileName()
        {
            string outfitName = clothingPrefab == null ? "outfit" : clothingPrefab.name;
            AnimationClip clip = GetAssignedAnimationClip();
            string motionName = clip == null ? "motion" : GetReadableMotionName(clip.name);
            // [QC][Invariant:filename]
            // Include both outfit and motion so exported files stay recognizable outside Unity's Project window.
            return MakeExportSafeFileName("ZEPETO_" + outfitName + "_" + motionName) + ".zepeto";
        }

        private static string GetReadableMotionName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "motion";
            }

            string name = rawName;
            int clipEditIndex = name.IndexOf("_clipedit", StringComparison.OrdinalIgnoreCase);
            if (clipEditIndex > 0)
            {
                name = name.Substring(0, clipEditIndex);
            }

            const string editableSuffix = "_editable";
            if (name.EndsWith(editableSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - editableSuffix.Length);
            }

            return string.IsNullOrEmpty(name) ? "motion" : name;
        }

        private static string MakeExportSafeFileName(string value)
        {
            string safeName = string.IsNullOrEmpty(value) ? "ZEPETO_export" : value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            safeName = safeName.Replace(' ', '_');
            while (safeName.IndexOf("__", StringComparison.Ordinal) >= 0)
            {
                safeName = safeName.Replace("__", "_");
            }

            return string.IsNullOrEmpty(safeName) ? "ZEPETO_export" : safeName;
        }

        private static string GetExportPackageStatusText(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return "의상 선택 필요";
            }

            string absolutePath = ToAbsoluteProjectPath(projectRelativePath);
            if (!File.Exists(absolutePath))
            {
                return projectRelativePath + " (아직 생성 전)";
            }

            FileInfo fileInfo = new FileInfo(absolutePath);
            return projectRelativePath
                + " (저장됨, "
                + FormatBytes(fileInfo.Length)
                + ", "
                + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                + ")";
        }

        private static bool ExportPackageExists(string projectRelativePath)
        {
            return !string.IsNullOrEmpty(projectRelativePath)
                && File.Exists(ToAbsoluteProjectPath(projectRelativePath));
        }

        private static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelativePath));
        }

        private static string GetStepStateLabel(StepState state)
        {
            if (state == StepState.Ready)
            {
                return "준비됨";
            }

            if (state == StepState.InProgress)
            {
                return "진행 필요";
            }

            if (state == StepState.Waiting)
            {
                return "대기중";
            }

            return state == StepState.Blocked ? "차단" : "필요";
        }

        private static MessageType GetStepStateMessageType(StepState state)
        {
            if (state == StepState.Ready)
            {
                return MessageType.Info;
            }

            if (state == StepState.InProgress)
            {
                return MessageType.Info;
            }

            if (state == StepState.Waiting)
            {
                return MessageType.None;
            }

            return state == StepState.Blocked ? MessageType.Error : MessageType.Warning;
        }

        private static Color GetStepStateColor(StepState state)
        {
            if (state == StepState.Ready)
            {
                return ReadyGreen;
            }

            if (state == StepState.InProgress)
            {
                return ActionBlue;
            }

            if (state == StepState.Blocked)
            {
                return BlockedRed;
            }

            if (state == StepState.Waiting)
            {
                return WaitingGray;
            }

            return NeededAmber;
        }
        private static bool CanEnterPlayMode(SafetySnapshot snapshot)
        {
            return !snapshot.HasBlockingRisk
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private void StopPlayMode()
        {
            activePreviewStage = -1;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                statusMessage = "Play Mode stop requested. / 실행 정지를 요청했습니다.";
            }
        }

        private void RequestPlayMode(int previewStage = -1)
        {
            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (!CanEnterPlayMode(snapshot))
            {
                statusMessage = "Play is blocked by Safe Status. / 안전 상태 때문에 실행을 막았습니다.";
                return;
            }

            if (IsPackageOrPackageCachePath(GetAnimatorControllerPath()))
            {
                string controllerMessage;
                if (!EnsureLocalAnimatorController(out controllerMessage))
                {
                    statusMessage = "Play 전에 local AnimatorController가 필요합니다. " + controllerMessage;
                    ValidateState();
                    return;
                }
            }

            if (previewStage == 3 && !PrepareClipAdjustPreviewBeforePlay())
            {
                return;
            }

            activePreviewStage = previewStage;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            EditorApplication.isPlaying = true;
            statusMessage = "Play Mode requested. Scene View will focus on LOADER after Play starts. / 실행 후 Scene View를 LOADER에 맞춥니다.";
        }

        private void RecoverSafetyState()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                StopPlayMode();
            }

            string clearMessage;
            bool didClearConsole = TryClearUnityConsole(out clearMessage);
            sessionWarningCount = 0;
            sessionErrorCount = 0;
            lastConsoleMessage = string.Empty;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            lastSafetyRefreshTime = -1000d;

            RefreshSafetySnapshot();
            ValidateState();

            if (safetySnapshot.HasBlockingRisk)
            {
                statusMessage = "복구를 시도했지만 아직 차단 상태입니다: " + safetySnapshot.Message;
                if (!string.IsNullOrEmpty(safetySnapshot.Detail))
                {
                    statusMessage += " / " + safetySnapshot.Detail;
                }
            }
            else
            {
                statusMessage = didClearConsole
                    ? "복구 완료. 다시 Play/Edit을 시도할 수 있습니다. / Recovery complete."
                    : "복구 상태를 초기화했습니다. Console clear failed: " + clearMessage;
            }
        }
        private void ClearConsoleAndSessionSummary()
        {
            string clearMessage;
            bool didClearConsole = TryClearUnityConsole(out clearMessage);
            sessionWarningCount = 0;
            sessionErrorCount = 0;
            lastConsoleMessage = string.Empty;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            lastSafetyRefreshTime = -1000d;
            statusMessage = didClearConsole
                ? "Console and helper session summary cleared. / 콘솔과 헬퍼 세션 요약을 정리했습니다."
                : "Helper session summary cleared. Unity Console clear failed: " + clearMessage;
            RefreshSafetySnapshot();
            ValidateState();
        }

        private static bool TryClearUnityConsole(out string message)
        {
            try
            {
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
                MethodInfo clearMethod = logEntriesType == null
                    ? null
                    : logEntriesType.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clearMethod == null)
                {
                    message = "Unity LogEntries.Clear was not found.";
                    return false;
                }

                clearMethod.Invoke(null, null);
                message = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }
        }

        private static void OpenLogLocation(string logPath)
        {
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                EditorUtility.RevealInFinder(logPath);
                return;
            }

            if (!string.IsNullOrEmpty(logPath))
            {
                string directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    EditorUtility.RevealInFinder(directory);
                    return;
                }
            }

            EditorUtility.RevealInFinder(Directory.GetCurrentDirectory());
        }
        private void SaveClipAdjustToCurrentClip()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 clip 파일을 저장하지 않습니다. 먼저 Stop을 눌러주세요.";
                ValidateState();
                return;
            }

            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (snapshot.HasBlockingRisk)
            {
                statusMessage = "Clip edit save is blocked by Safety. Press Recover first. / 안전 상태 때문에 저장을 막았습니다.";
                ValidateState();
                return;
            }

            AnimationClip sourceClip = GetAssignedAnimationClip();
            string reason;
            if (!CanEditAnimationClip(sourceClip, out reason))
            {
                statusMessage = reason;
                ValidateState();
                return;
            }

            if (!HasClipAdjustInput(sourceClip))
            {
                statusMessage = "저장할 클립 조정값이 없습니다. 배속, 시작/끝 시간, 반복 옵션 중 하나를 바꾼 뒤 저장하세요.";
                ValidateState();
                return;
            }

            ClipEditSettings settings = BuildClipEditSettings(sourceClip);
            // [AUDIT][Risk:High][Scope:file_io]
            // Clip adjustment is copy-on-write: package/cache clips and existing working clips are not edited in place.
            // Verification target: new .anim under ClipEditRoot, then LOADER.AnimationClip points to that new asset.
            ClipEditResult result = ClipEditUtility.CreateClipAdjustedClip(sourceClip, settings, ClipEditRoot);
            if (!result.Success)
            {
                statusMessage = result.Message;
                ValidateState();
                return;
            }

            lastClipEditedClip = result.Clip;
            copiedAnimationClip = result.Clip;
            if (!AssignAnimationClip(result.Clip))
            {
                SelectAndPing(result.Clip);
                statusMessage = "clip edit 파일은 저장했지만 LOADER에 연결하지 못했습니다: " + result.Path;
                ValidateState();
                return;
            }

            SetClipStageComplete(true);
            SelectAndPing(result.Clip);
            statusMessage = "clip 조정을 저장하고 LOADER에 연결했습니다. 이제 4번 저장/Export 단계로 이동하세요: " + result.Path + " / Retimed curves: " + result.ModifiedCurveCount;
            if (!string.IsNullOrEmpty(result.WarningSummary))
            {
                Debug.LogWarning("ZEPETO Studio Helper clip edit warning: " + result.WarningSummary);
                statusMessage += " / Warnings: " + result.WarningSummary;
            }
        }

        private void EnsureClipAdjustDefaults(AnimationClip clip)
        {
            if (clip == null)
            {
                clipAdjustSource = null;
                clipAdjustSourcePath = string.Empty;
                return;
            }

            string sourcePath = GetClipAdjustStatePath(clip);
            if (!string.IsNullOrEmpty(clipAdjustSourcePath)
                && string.Equals(clipAdjustSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            clipAdjustSource = clip;
            clipAdjustSourcePath = sourcePath;
            if (TryRestoreClipAdjustSessionState(sourcePath, clip))
            {
                return;
            }

            motionPreviewSpeed = 1f;
            clipLoop = true;
            clipTrimStart = 0f;
            clipTrimEnd = Mathf.Max(0.01f, clip.length);
            SaveClipAdjustSessionState(sourcePath);
        }

        private string GetClipAdjustStatePath(AnimationClip clip)
        {
            if (clip == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.Equals(path, ClipAdjustPreviewPath, StringComparison.OrdinalIgnoreCase))
            {
                string restorePath = SessionState.GetString(ClipAdjustPreviewRestorePathSessionKey, string.Empty);
                if (!string.IsNullOrEmpty(restorePath))
                {
                    return restorePath;
                }
            }

            return string.IsNullOrEmpty(path)
                ? "instance:" + clip.GetInstanceID().ToString()
                : path;
        }

        private bool TryRestoreClipAdjustSessionState(string sourcePath, AnimationClip clip)
        {
            string savedSourcePath = SessionState.GetString(ClipAdjustSourcePathSessionKey, string.Empty);
            if (string.IsNullOrEmpty(sourcePath)
                || !string.Equals(savedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            float clipLength = clip == null ? 0.01f : Mathf.Max(0.01f, clip.length);
            motionPreviewSpeed = Mathf.Clamp(SessionState.GetFloat(ClipAdjustSpeedSessionKey, 1f), 0.25f, 2f);
            clipTrimStart = Mathf.Clamp(SessionState.GetFloat(ClipAdjustStartSessionKey, 0f), 0f, clipLength);
            clipTrimEnd = Mathf.Clamp(SessionState.GetFloat(ClipAdjustEndSessionKey, clipLength), clipTrimStart + 0.01f, clipLength);
            clipLoop = SessionState.GetBool(ClipAdjustLoopSessionKey, true);
            return true;
        }

        private void SaveClipAdjustSessionState(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            SessionState.SetString(ClipAdjustSourcePathSessionKey, sourcePath);
            SessionState.SetFloat(ClipAdjustSpeedSessionKey, Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f));
            SessionState.SetFloat(ClipAdjustStartSessionKey, Mathf.Max(0f, clipTrimStart));
            SessionState.SetFloat(ClipAdjustEndSessionKey, Mathf.Max(0.01f, clipTrimEnd));
            SessionState.SetBool(ClipAdjustLoopSessionKey, clipLoop);
        }
        private bool HasClipAdjustInput(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            float clipLength = Mathf.Max(0.01f, clip.length);
            float rangeStart = Mathf.Clamp(clipTrimStart, 0f, clipLength);
            float rangeEnd = Mathf.Clamp(clipTrimEnd <= 0f ? clipLength : clipTrimEnd, rangeStart + 0.01f, clipLength);
            bool speedChanged = Mathf.Abs(Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f) - 1f) > 0.001f;
            bool rangeChanged = rangeStart > 0.001f || Mathf.Abs(rangeEnd - clipLength) > 0.001f;
            bool loopChanged = !clipLoop;
            return speedChanged || rangeChanged || loopChanged;
        }

        private ClipEditSettings BuildClipEditSettings(AnimationClip sourceClip)
        {
            float clipLength = sourceClip == null ? 0.01f : Mathf.Max(0.01f, sourceClip.length);
            float rangeStart = Mathf.Clamp(clipTrimStart, 0f, clipLength);
            float rangeEnd = Mathf.Clamp(clipTrimEnd <= 0f ? clipLength : clipTrimEnd, rangeStart + 0.01f, clipLength);
            return new ClipEditSettings(
                Mathf.Clamp(motionPreviewSpeed, 0.25f, 2f),
                rangeStart,
                rangeEnd,
                clipLoop);
        }

        private AnimationClip GetAssignedAnimationClip()
        {
            if (!TryUpdateSerializedObject(animationClipObject) || animationClipProperty == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (!TryUpdateSerializedObject(animationClipObject) || animationClipProperty == null)
            {
                return null;
            }

            return animationClipProperty.objectReferenceValue as AnimationClip;
        }

        private UnityEngine.Object GetAssignedAnimatorController()
        {
            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (!TryUpdateSerializedObject(animatorControllerObject) || animatorControllerProperty == null)
            {
                return null;
            }

            return animatorControllerProperty.objectReferenceValue;
        }

        private string GetAnimatorControllerPath()
        {
            UnityEngine.Object controller = GetAssignedAnimatorController();
            return controller == null ? string.Empty : AssetDatabase.GetAssetPath(controller);
        }

        private string GetCurrentZepetoId()
        {
            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (!TryUpdateSerializedObject(zepetoIdObject) || zepetoIdProperty == null)
            {
                return string.Empty;
            }

            return zepetoIdProperty.stringValue;
        }

        private static bool TryUpdateSerializedObject(SerializedObject serializedObject)
        {
            // [QC][Guard:stale_serialized_object]
            // ZEPETO export and domain reloads can destroy the target behind a cached SerializedObject.
            // Returning false lets callers refind LOADER instead of throwing "target has been destroyed".
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return false;
            }

            try
            {
                serializedObject.Update();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private void RefreshAll()
        {
            RefreshSafetySnapshot();
            FindLoaderAndSerializedFields();
            FindDefaultClothingPrefab();
            LoadPackageAnimations();
            FindExistingCopiedAnimation();
            ValidateState();
        }

        private SafetySnapshot GetSafetySnapshot(bool force)
        {
            double now = EditorApplication.timeSinceStartup;
            if (force || now - lastSafetyRefreshTime > SafetyRefreshIntervalSeconds)
            {
                RefreshSafetySnapshot();
            }

            return safetySnapshot;
        }

        private void RefreshSafetySnapshot()
        {
            safetySnapshot = BuildSafetySnapshot();
            lastSafetyRefreshTime = EditorApplication.timeSinceStartup;
        }

        private SafetySnapshot BuildSafetySnapshot()
        {
            string logPath = GetCurrentLogPath();
            long logSize = 0L;
            long newLogBytes = 0L;
            DateTime logLastWriteUtc = DateTime.MinValue;
            string recentLogText = string.Empty;

            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                try
                {
                    FileInfo logFile = new FileInfo(logPath);
                    logSize = logFile.Length;
                    logLastWriteUtc = logFile.LastWriteTimeUtc;
                    long baselineBytes = Math.Max(0L, safetyLogBaselineBytes);
                    if (logSize < baselineBytes)
                    {
                        baselineBytes = 0L;
                        safetyLogBaselineBytes = 0L;
                    }

                    newLogBytes = Math.Max(0L, logSize - baselineBytes);
                    if (newLogBytes > 0L)
                    {
                        recentLogText = ReadLogTailSince(logPath, baselineBytes, RecentLogTailBytes);
                    }
                }
                catch (Exception exception)
                {
                    return SafetySnapshot.Warning(
                        "Could not inspect Unity log. / Unity 로그를 확인하지 못했습니다.",
                        exception.Message,
                        logPath,
                        logSize,
                        logLastWriteUtc);
                }
            }

            if (newLogBytes >= LogGrowthBlockBytes)
            {
                return SafetySnapshot.Blocked(
                    "Log grew over 100MB after the last Recover/Clear. Stop before refreshing or playing. / 마지막 복구 이후 로그가 100MB 넘게 증가했습니다.",
                    "New log growth guard blocked risky actions. New bytes since last Recover/Clear: " + FormatBytes(newLogBytes) + ".",
                    "log-size",
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            string safetyText = lastConsoleMessage + "\n" + recentLogText;
            string riskKeyword = FindCriticalLoopKeyword(safetyText);
            if (!string.IsNullOrEmpty(riskKeyword))
            {
                return SafetySnapshot.Blocked(
                    "A repeated Unity/ZEPETO error pattern was detected. / 반복 오류 패턴을 감지했습니다.",
                    "Matched keyword: " + riskKeyword + ". Clear the console/log after fixing the source before Play or Refresh.",
                    riskKeyword,
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            if (ContainsPackageCacheImmutableWarning(safetyText))
            {
                return SafetySnapshot.Warning(
                    "Package cache immutable asset warning detected. / package cache asset 변경 경고가 있습니다.",
                    "Use Local Controller Fix, then restart Unity or let Library/PackageCache rebuild if the warning was already emitted.",
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            if (ContainsReloadingAssembliesFailed(safetyText))
            {
                return SafetySnapshot.Warning(
                    "Unity assembly reload failed previously. / 이전 assembly reload 실패가 감지되었습니다.",
                    "Run Validate, fix compile errors if any, then use Recover before Play.",
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            bool hasKnownSdkCleanup = ContainsKnownSdkCleanupException(safetyText);

            if (EditorApplication.isCompiling)
            {
                return SafetySnapshot.Blocked(
                    "Unity is compiling. Wait before Play or Refresh. / Unity가 컴파일 중입니다.",
                    string.Empty,
                    "compiling",
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            if (EditorApplication.isUpdating)
            {
                return SafetySnapshot.Blocked(
                    "Unity is importing/updating assets. Wait before Play or Refresh. / Unity가 에셋을 갱신 중입니다.",
                    string.Empty,
                    "updating",
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            if (sessionErrorCount > 0)
            {
                return SafetySnapshot.Warning(
                    "Helper session has errors, but no known loop pattern was found. / 헬퍼 세션 오류가 있지만 폭주 패턴은 없습니다.",
                    lastConsoleMessage,
                    logPath,
                    logSize,
                    logLastWriteUtc);
            }

            return SafetySnapshot.Ok(
                "Safe to work. No known SDK/helper loop pattern is active. / 작업해도 되는 상태입니다.",
                hasKnownSdkCleanup
                    ? "ZEPETO SDK cleanup warning was ignored because it is a non-repeating Play/Stop cleanup message."
                    : (sessionWarningCount > 0 ? lastConsoleMessage : string.Empty),
                logPath,
                logSize,
                logLastWriteUtc);
        }

        private static string GetCurrentLogPath()
        {
            try
            {
                return Application.consoleLogPath ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long GetCurrentLogSize()
        {
            string logPath = GetCurrentLogPath();
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            {
                return 0L;
            }

            try
            {
                return new FileInfo(logPath).Length;
            }
            catch
            {
                return 0L;
            }
        }

        private static string ReadLogTailSince(string path, long startBytes, int maxBytes)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || maxBytes <= 0)
            {
                return string.Empty;
            }

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = stream.Length;
                    long safeStart = Math.Max(0L, Math.Min(startBytes, length));
                    long available = length - safeStart;
                    int readLength = (int)Math.Min((long)maxBytes, available);
                    if (readLength <= 0)
                    {
                        return string.Empty;
                    }

                    long readStart = Math.Max(safeStart, length - readLength);
                    stream.Seek(readStart, SeekOrigin.Begin);
                    byte[] buffer = new byte[readLength];
                    int bytesRead = stream.Read(buffer, 0, readLength);
                    return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FindCriticalLoopKeyword(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            bool hasNullOrAssertion = text.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Assertion failed", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasNullOrAssertion)
            {
                return string.Empty;
            }

            for (int i = 0; i < CriticalLoopKeywords.Length; i++)
            {
                if (text.IndexOf(CriticalLoopKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return CriticalLoopKeywords[i];
                }
            }

            return string.Empty;
        }

        private static bool ContainsKnownSdkCleanupException(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            for (int i = 0; i < KnownSdkCleanupKeywords.Length; i++)
            {
                if (text.IndexOf(KnownSdkCleanupKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPackageCacheImmutableWarning(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf("immutable asset", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("PackageCache", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsReloadingAssembliesFailed(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf("Reloading assemblies failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatBytes(long value)
        {
            if (value <= 0L)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = value;
            int unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return size.ToString(unitIndex == 0 ? "0" : "0.0") + " " + units[unitIndex];
        }
        private void FindLoaderAndSerializedFields()
        {
            if (loader == null)
            {
                loader = GameObject.Find("LOADER");
            }

            zepetoIdObject = null;
            animationClipObject = null;
            animatorControllerObject = null;
            zepetoIdProperty = null;
            animationClipProperty = null;
            animatorControllerProperty = null;

            if (loader == null)
            {
                zepetoIdText = defaultZepetoId;
                return;
            }

            Component[] components = loader.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                SerializedObject serializedObject = new SerializedObject(component);

                if (zepetoIdProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("zepetoId");
                    if (property != null && property.propertyType == SerializedPropertyType.String)
                    {
                        zepetoIdObject = serializedObject;
                        zepetoIdProperty = property;
                        zepetoIdText = string.IsNullOrEmpty(property.stringValue) ? defaultZepetoId : property.stringValue;
                    }
                }

                if (animationClipProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("AnimationClip");
                    if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        animationClipObject = serializedObject;
                        animationClipProperty = property;
                    }
                }

                if (animatorControllerProperty == null)
                {
                    SerializedProperty property = serializedObject.FindProperty("AnimatorController");
                    if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        animatorControllerObject = serializedObject;
                        animatorControllerProperty = property;
                    }
                }
            }
        }

        private void FindDefaultClothingPrefab()
        {
            List<GameObject> prefabs = FindAllOutfitPrefabs();
            if (prefabs.Count == 0)
            {
                clothingPrefab = null;
                statusMessage = "Assets/Contents 아래에서 의상 prefab을 찾지 못했습니다.";
                return;
            }

            if (clothingPrefab != null && prefabs.Contains(clothingPrefab))
            {
                statusMessage = "선택된 의상: " + clothingPrefab.name;
                return;
            }

            clothingPrefab = null;
            statusMessage = "1-2 의상 선택 목록에서 사용할 prefab을 직접 선택하세요.";
        }

        private void LoadPackageAnimations()
        {
            packageAnimations.Clear();

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { PackageAnimationFolder });
            Array.Sort(guids, CompareAnimationGuidByName);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null)
                {
                    packageAnimations.Add(clip);
                }
            }

            packageAnimationNames = new string[packageAnimations.Count];
            for (int i = 0; i < packageAnimations.Count; i++)
            {
                packageAnimationNames[i] = packageAnimations[i].name;
                if (selectedAnimationIndex < 0 && packageAnimations[i].name.Equals("Videobooth_282", StringComparison.OrdinalIgnoreCase))
                {
                    selectedAnimationIndex = i;
                }
            }

            if (selectedAnimationIndex < 0 && packageAnimations.Count > 0)
            {
                selectedAnimationIndex = 0;
            }

            if (selectedAnimationIndex >= packageAnimations.Count)
            {
                selectedAnimationIndex = packageAnimations.Count - 1;
            }
        }

        private static int CompareAnimationGuidByName(string leftGuid, string rightGuid)
        {
            string left = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(leftGuid));
            string right = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(rightGuid));
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private void FindExistingCopiedAnimation()
        {
            if (!AssetDatabase.IsValidFolder(AnimationCopyRoot))
            {
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            string expectedName = selected == null ? "Videobooth_282_editable" : selected.name + "_editable";
            string[] guids = AssetDatabase.FindAssets(expectedName + " t:AnimationClip", new[] { AnimationCopyRoot });
            if (guids.Length == 0)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            copiedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private AnimationClip GetSelectedPackageAnimation()
        {
            if (selectedAnimationIndex < 0 || selectedAnimationIndex >= packageAnimations.Count)
            {
                return null;
            }

            return packageAnimations[selectedAnimationIndex];
        }

        private void ApplyZepetoId(string value)
        {
            if (zepetoIdProperty == null)
            {
                return;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 아이디를 저장하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return;
            }

            value = SanitizeZepetoId(value);
            if (string.IsNullOrEmpty(value))
            {
                statusMessage = "아이디가 비어 있습니다. 적용할 ID를 입력하세요.";
                ValidateState();
                return;
            }

            zepetoIdObject.Update();
            zepetoIdProperty.stringValue = value;
            zepetoIdObject.ApplyModifiedProperties();
            zepetoIdText = value;
            EditorUtility.SetDirty(zepetoIdObject.targetObject);
            EditorUtility.SetDirty(loader);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            SetAvatarOutfitStageComplete(false);
            statusMessage = "아이디 적용됨: " + value;
            ValidateState();
        }

        private void LoadDefaultZepetoId()
        {
            defaultZepetoId = SanitizeZepetoId(EditorPrefs.GetString(DefaultZepetoIdEditorPrefsKey, BuiltInDefaultZepetoId));
            if (string.IsNullOrEmpty(defaultZepetoId))
            {
                defaultZepetoId = BuiltInDefaultZepetoId;
            }

            zepetoIdText = defaultZepetoId;
        }
        private static string SanitizeZepetoId(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().TrimStart('@');
        }

        private void CopySelectedAnimation(bool completeStageAfterAssign = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 동작 파일을 복사하거나 LOADER에 연결하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return;
            }

            AnimationClip selected = GetSelectedPackageAnimation();
            if (selected == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not resolve selected animation path.");
                return;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Animations");

            string destinationPath = AssetDatabase.GenerateUniqueAssetPath(AnimationCopyRoot + "/" + selected.name + "_editable.anim");
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                Debug.LogWarning("ZEPETO Studio Helper could not copy animation from " + sourcePath + " to " + destinationPath);
                return;
            }

            AssetDatabase.ImportAsset(destinationPath);
            copiedAnimationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
            bool didAssign = AssignAnimationClip(copiedAnimationClip);
            if (didAssign)
            {
                if (completeStageAfterAssign)
                {
                    SetMotionSelectStageComplete(true);
                }
            }
            SelectAndPing(copiedAnimationClip);
            statusMessage = didAssign
                ? "동작을 복사하고 LOADER에 연결했습니다: " + destinationPath
                : "동작 복사본은 만들었지만 LOADER에 연결하지 못했습니다: " + destinationPath;
            ValidateState();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static bool IsPackageOrPackageCachePath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath)
                && (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                    || assetPath.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase)
                    || assetPath.IndexOf("PackageCache", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool EnsureLocalAnimatorController(out string message)
        {
            message = string.Empty;
            if (animatorControllerProperty == null || animatorControllerObject == null)
            {
                message = "LOADER AnimatorController field was not found.";
                return false;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play 중에는 AnimatorController를 바꾸지 않습니다. 먼저 Stop을 눌러주세요.";
                return false;
            }

            animatorControllerObject.Update();
            UnityEngine.Object currentController = animatorControllerProperty.objectReferenceValue;
            if (currentController == null)
            {
                message = "LOADER AnimatorController is empty.";
                return false;
            }

            string sourcePath = AssetDatabase.GetAssetPath(currentController);
            if (string.IsNullOrEmpty(sourcePath))
            {
                message = "Could not resolve AnimatorController asset path.";
                return false;
            }

            if (!IsPackageOrPackageCachePath(sourcePath))
            {
                message = "AnimatorController is already project-local: " + sourcePath;
                return true;
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Controllers");

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LocalPlaygroundControllerPath) == null)
            {
                string copyMessage;
                if (!CreateLocalAnimatorControllerCopy(currentController, sourcePath, out copyMessage))
                {
                    message = copyMessage;
                    return false;
                }

                AssetDatabase.ImportAsset(LocalPlaygroundControllerPath);
            }

            UnityEngine.Object localController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LocalPlaygroundControllerPath);
            if (localController == null)
            {
                message = "Local AnimatorController copy could not be loaded: " + LocalPlaygroundControllerPath;
                return false;
            }

            Undo.RecordObject(animatorControllerObject.targetObject, "Use Local ZEPETO Preview Controller");
            animatorControllerObject.Update();
            animatorControllerProperty.objectReferenceValue = localController;
            animatorControllerObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animatorControllerObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            message = "AnimatorController를 local copy로 변경했습니다: " + LocalPlaygroundControllerPath;
            return true;
        }

        private static bool CreateLocalAnimatorControllerCopy(UnityEngine.Object sourceController, string sourcePath, out string message)
        {
            message = string.Empty;

            AnimatorOverrideController sourceOverrideController = sourceController as AnimatorOverrideController;
            if (sourceOverrideController != null)
            {
                AnimatorOverrideController localController = UnityEngine.Object.Instantiate(sourceOverrideController);
                localController.name = "PlaygroundAnimatorController_local";
                AssetDatabase.CreateAsset(localController, LocalPlaygroundControllerPath);
                message = "Created local AnimatorOverrideController copy.";
                return true;
            }

            if (AssetDatabase.CopyAsset(sourcePath, LocalPlaygroundControllerPath))
            {
                message = "Copied AnimatorController to local project asset.";
                return true;
            }

            message = "Could not copy AnimatorController from " + sourcePath + " to " + LocalPlaygroundControllerPath + ".";
            return false;
        }

        private bool AssignAnimationClip(AnimationClip clip, bool preserveClipStageComplete = false)
        {
            if (clip == null || animationClipProperty == null)
            {
                return false;
            }

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                statusMessage = "Play 중에는 LOADER AnimationClip을 바꾸지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return false;
            }

            string controllerMessage;
            if (!EnsureLocalAnimatorController(out controllerMessage))
            {
                statusMessage = "AnimationClip 연결 전에 local AnimatorController가 필요합니다. " + controllerMessage;
                return false;
            }

            // [AUDIT][Risk:Major][Scope:loader_binding]
            // Temporary Play previews pass preserveClipStageComplete=true so preview assignment does not unlock
            // completed workflow stages. Real clip changes intentionally reset step 3 below.
            animationClipObject.Update();
            AnimationClip previousClip = animationClipProperty.objectReferenceValue as AnimationClip;
            Undo.RecordObject(animationClipObject.targetObject, "Assign ZEPETO Preview Animation");
            animationClipProperty.objectReferenceValue = clip;
            animationClipObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(animationClipObject.targetObject);
            if (loader != null)
            {
                EditorUtility.SetDirty(loader);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loader.scene);
            }

            statusMessage = "Assigned LOADER AnimationClip: " + clip.name;
            if (previousClip != clip && !preserveClipStageComplete)
            {
                SetClipStageComplete(false);
            }
            ValidateState();
            return true;
        }

        private bool CanEditAnimationClip(AnimationClip clip, out string reason)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "Play 중에는 .anim 파일을 만들거나 LOADER에 연결하지 않습니다. 먼저 정지 / Stop을 눌러주세요.";
                return false;
            }

            if (clip == null)
            {
                reason = "LOADER 동작이 비어 있습니다. 먼저 3단계에서 동작을 복사하고 연결하세요.";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                reason = "현재 동작은 프로젝트 안의 .anim 파일이 아닙니다.";
                return false;
            }

            if (IsPackageOrPackageCachePath(path))
            {
                reason = "현재 동작은 원본 package 동작입니다. 먼저 3단계에서 복사본을 만들어 연결하세요.";
                return false;
            }

            if (!path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                reason = "편집 저장은 " + AnimationCopyRoot + " 아래의 복사본에만 만들 수 있습니다.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        private void ValidateState()
        {
            validationMessages.Clear();

            string foundZepetoStudioVersion;
            bool isRequiredPackageVersionValid = IsRequiredZepetoStudioPackageInstalled(out foundZepetoStudioVersion);
            AddValidation(
                isRequiredPackageVersionValid,
                RequiredPackage + "@" + RequiredPackageVersion + " package is available.",
                string.IsNullOrEmpty(foundZepetoStudioVersion)
                    ? RequiredPackage + "@" + RequiredPackageVersion + " package was not found."
                    : RequiredPackage + " version mismatch. Expected " + RequiredPackageVersion + ", found " + foundZepetoStudioVersion + ".");

            if (loader == null)
            {
                loader = GameObject.Find("LOADER");
            }
            AddValidation(loader != null, "Active scene has LOADER.", "Active scene does not have LOADER.");

            if (loader != null && (zepetoIdProperty == null || animationClipProperty == null || animatorControllerProperty == null))
            {
                FindLoaderAndSerializedFields();
            }

            AddValidation(zepetoIdProperty != null, "LOADER has zepetoId serialized field.", "LOADER is missing zepetoId serialized field.");
            AddValidation(animationClipProperty != null, "LOADER has AnimationClip serialized field.", "LOADER is missing AnimationClip serialized field.");
            AddValidation(animatorControllerProperty != null, "LOADER has AnimatorController serialized field.", "LOADER is missing AnimatorController serialized field.");

            if (zepetoIdProperty != null)
            {
                zepetoIdObject.Update();
                if (string.IsNullOrEmpty(zepetoIdProperty.stringValue))
                {
                    validationMessages.Add(new ValidationMessage("아이디가 비어 있습니다. ID를 입력하거나 기본 ID를 적용하세요.", MessageType.Warning));
                }
                else
                {
                    validationMessages.Add(new ValidationMessage("아이디가 설정되어 있습니다: " + zepetoIdProperty.stringValue, MessageType.Info));
                }
            }

            ValidatePrefab();
            ValidateAnimatorController();
            ValidateAnimationClip();

            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (snapshot.HasBlockingRisk)
            {
                validationMessages.Add(new ValidationMessage("Safe Status is blocking risky actions: " + snapshot.Message, MessageType.Error));
            }
            else if (snapshot.HasWarning)
            {
                validationMessages.Add(new ValidationMessage("Safe Status warning: " + snapshot.Message, MessageType.Warning));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("Safe Status is clean.", MessageType.Info));
            }

            Repaint();
        }

        private void ValidatePrefab()
        {
            if (clothingPrefab == null)
            {
                validationMessages.Add(new ValidationMessage("No clothing prefab selected.", MessageType.Warning));
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(clothingPrefab);
            if (prefabPath.StartsWith(ContentsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                validationMessages.Add(new ValidationMessage("Selected prefab is under " + ContentsRoot + ".", MessageType.Info));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("Selected prefab should be under " + ContentsRoot + ". Current path: " + prefabPath, MessageType.Warning));
            }
        }

        private static bool IsRequiredZepetoStudioPackageInstalled(out string foundVersion)
        {
            foundVersion = string.Empty;
            string packageCacheRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache");
            if (!Directory.Exists(packageCacheRoot))
            {
                return false;
            }

            string[] packageDirectories = Directory.GetDirectories(packageCacheRoot, RequiredPackage + "@*");
            for (int i = 0; i < packageDirectories.Length; i++)
            {
                string directoryName = Path.GetFileName(packageDirectories[i]);
                string prefix = RequiredPackage + "@";
                if (string.IsNullOrEmpty(directoryName) || !directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string version = directoryName.Substring(prefix.Length);
                if (string.IsNullOrEmpty(foundVersion))
                {
                    foundVersion = version;
                }

                if (version == RequiredPackageVersion)
                {
                    foundVersion = version;
                    return true;
                }
            }

            return false;
        }
        private void ValidateAnimationClip()
        {
            if (animationClipProperty == null)
            {
                return;
            }

            animationClipObject.Update();
            AnimationClip clip = animationClipProperty.objectReferenceValue as AnimationClip;
            if (clip == null)
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip is empty.", MessageType.Warning));
                return;
            }

            string path = AssetDatabase.GetAssetPath(clip);
            if (IsPackageOrPackageCachePath(path))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip points to a package source. Copy it before editing: " + path, MessageType.Warning));
            }
            else if (path.StartsWith(AnimationCopyRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip is ready for clip adjust: " + path, MessageType.Info));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimationClip points to a project asset. Clip adjust only supports " + AnimationCopyRoot + ": " + path, MessageType.Warning));
            }
        }

        private void ValidateAnimatorController()
        {
            if (animatorControllerProperty == null)
            {
                return;
            }

            animatorControllerObject.Update();
            UnityEngine.Object controller = animatorControllerProperty.objectReferenceValue;
            if (controller == null)
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController is empty.", MessageType.Warning));
                return;
            }

            string path = AssetDatabase.GetAssetPath(controller);
            if (IsPackageOrPackageCachePath(path))
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController points to package cache. Use Local Controller Fix before assigning clips: " + path, MessageType.Warning));
            }
            else
            {
                validationMessages.Add(new ValidationMessage("LOADER AnimatorController is project-local: " + path, MessageType.Info));
            }
        }

        private void AddValidation(bool condition, string okMessage, string failMessage)
        {
            validationMessages.Add(new ValidationMessage(condition ? okMessage : failMessage, condition ? MessageType.Info : MessageType.Error));
        }

        private static void SelectAndPing(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private void FrameLoaderForScenePreview()
        {
            if (loader == null)
            {
                FindLoaderAndSerializedFields();
            }

            if (loader == null)
            {
                return;
            }

            Selection.activeGameObject = loader;
            EditorGUIUtility.PingObject(loader);

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            Bounds bounds = GetLoaderPreviewBounds();
            sceneView.Frame(bounds, false);
            sceneView.LookAt(bounds.center, sceneView.rotation, Mathf.Max(1.6f, bounds.extents.magnitude * 1.8f));
            sceneView.Repaint();
        }

        private Bounds GetLoaderPreviewBounds()
        {
            if (loader == null)
            {
                return new Bounds(Vector3.up, Vector3.one * 2f);
            }

            Renderer[] renderers = loader.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = new Bounds(loader.transform.position + Vector3.up, Vector3.one * 2f);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds || bounds.size.sqrMagnitude < 0.01f)
            {
                bounds = new Bounds(loader.transform.position + Vector3.up, Vector3.one * 2.2f);
            }

            return bounds;
        }

        private bool LoaderHasPreviewRenderers()
        {
            if (loader == null)
            {
                return false;
            }

            Renderer[] renderers = loader.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    return true;
                }
            }

            return false;
        }
        private static void SubscribeLogCollector()
        {
            if (isLogCollectorSubscribed)
            {
                return;
            }

            Application.logMessageReceived += HandleLogMessage;
            isLogCollectorSubscribed = true;
        }

        private static void UnsubscribeLogCollector()
        {
            if (!isLogCollectorSubscribed)
            {
                return;
            }

            Application.logMessageReceived -= HandleLogMessage;
            isLogCollectorSubscribed = false;
        }

        private static void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            string combinedMessage = condition + "\n" + stackTrace;
            bool isKnownSdkCleanup = ContainsKnownSdkCleanupException(combinedMessage);
            if (isKnownSdkCleanup)
            {
                lastConsoleMessage = combinedMessage;
                return;
            }

            if (type == LogType.Warning)
            {
                sessionWarningCount++;
            }
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                sessionErrorCount++;
            }

            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                lastConsoleMessage = condition;
            }
        }
        private struct ClipEditSettings
        {
            public readonly float Speed;
            public readonly float RangeStart;
            public readonly float RangeEnd;
            public readonly bool Loop;

            public ClipEditSettings(float speed, float rangeStart, float rangeEnd, bool loop)
            {
                Speed = Mathf.Clamp(speed, 0.25f, 2f);
                RangeStart = Mathf.Max(0f, rangeStart);
                RangeEnd = Mathf.Max(RangeStart + 0.01f, rangeEnd);
                Loop = loop;
            }
        }

        private struct ClipEditResult
        {
            public readonly bool Success;
            public readonly AnimationClip Clip;
            public readonly string Path;
            public readonly string Message;
            public readonly string WarningSummary;
            public readonly int ModifiedCurveCount;

            private ClipEditResult(bool success, AnimationClip clip, string path, string message, string warningSummary, int modifiedCurveCount)
            {
                Success = success;
                Clip = clip;
                Path = path;
                Message = message;
                WarningSummary = warningSummary;
                ModifiedCurveCount = modifiedCurveCount;
            }

            public static ClipEditResult Fail(string message)
            {
                return new ClipEditResult(false, null, string.Empty, message, string.Empty, 0);
            }

            public static ClipEditResult Ok(AnimationClip clip, string path, string warningSummary, int modifiedCurveCount)
            {
                return new ClipEditResult(true, clip, path, string.Empty, warningSummary, modifiedCurveCount);
            }
        }

        private static class ClipEditUtility
        {
            public static ClipEditResult CreateClipAdjustedPreviewClip(AnimationClip sourceClip, ClipEditSettings settings, string destinationPath)
            {
                if (sourceClip == null)
                {
                    return ClipEditResult.Fail("Source AnimationClip is empty.");
                }

                string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return ClipEditResult.Fail("Could not resolve source AnimationClip path.");
                }

                string outputRoot = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrEmpty(outputRoot))
                {
                    return ClipEditResult.Fail("Could not resolve preview output folder.");
                }

                outputRoot = outputRoot.Replace('\\', '/');
                // [QC][Invariant:asset_root]
                // All generated preview/edit clips stay under Assets/ZepetoHelper so package/cache assets remain immutable.
                EnsureFolder("Assets", "ZepetoHelper");
                EnsureFolder("Assets/ZepetoHelper", "Animations");
                EnsureOutputFolder(outputRoot);

                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath) != null)
                {
                    AssetDatabase.DeleteAsset(destinationPath);
                }

                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    return ClipEditResult.Fail("Could not copy animation to " + destinationPath + ".");
                }

                AssetDatabase.ImportAsset(destinationPath);
                AnimationClip previewClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
                if (previewClip == null)
                {
                    return ClipEditResult.Fail("Preview clip could not be loaded: " + destinationPath);
                }

                string warningSummary;
                int modifiedCurveCount = ApplyClipTiming(previewClip, settings, out warningSummary);
                ApplyClipLoopSetting(previewClip, settings.Loop);
                EditorUtility.SetDirty(previewClip);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(destinationPath);
                return ClipEditResult.Ok(previewClip, destinationPath, warningSummary, modifiedCurveCount);
            }

            public static ClipEditResult CreateClipAdjustedClip(AnimationClip sourceClip, ClipEditSettings settings, string outputRoot)
            {
                if (sourceClip == null)
                {
                    return ClipEditResult.Fail("Source AnimationClip is empty.");
                }

                string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    return ClipEditResult.Fail("Could not resolve source AnimationClip path.");
                }

                EnsureFolder("Assets", "ZepetoHelper");
                EnsureFolder("Assets/ZepetoHelper", "Animations");
                EnsureOutputFolder(outputRoot);

                // [QC][Invariant:copy_before_retime]
                // Saving creates a new clip path first; retiming is applied only after the copy is imported.
                string destinationPath = CreateNextEditPath(sourceClip.name, outputRoot, "clipedit");
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    return ClipEditResult.Fail("Could not copy animation to " + destinationPath + ".");
                }

                AssetDatabase.ImportAsset(destinationPath);
                AnimationClip editedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destinationPath);
                if (editedClip == null)
                {
                    return ClipEditResult.Fail("Copied clip edit could not be loaded: " + destinationPath);
                }

                string warningSummary;
                int modifiedCurveCount = ApplyClipTiming(editedClip, settings, out warningSummary);
                ApplyClipLoopSetting(editedClip, settings.Loop);
                EditorUtility.SetDirty(editedClip);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(destinationPath);

                return ClipEditResult.Ok(editedClip, destinationPath, warningSummary, modifiedCurveCount);
            }

            private static int ApplyClipTiming(AnimationClip clip, ClipEditSettings settings, out string warningSummary)
            {
                // [QA][Expected]
                // Numeric and object-reference curves are both retimed. If no curves are found, the caller surfaces
                // a warning instead of silently claiming the saved clip changed.
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);
                int modifiedCurveCount = 0;

                for (int i = 0; i < curveBindings.Length; i++)
                {
                    EditorCurveBinding binding = curveBindings[i];
                    AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (sourceCurve == null)
                    {
                        continue;
                    }

                    AnimationCurve retimedCurve = RetimingCurve(sourceCurve, settings);
                    AnimationUtility.SetEditorCurve(clip, binding, retimedCurve);
                    modifiedCurveCount++;
                }

                EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                for (int i = 0; i < objectBindings.Length; i++)
                {
                    EditorCurveBinding binding = objectBindings[i];
                    ObjectReferenceKeyframe[] sourceKeys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (sourceKeys == null || sourceKeys.Length == 0)
                    {
                        continue;
                    }

                    AnimationUtility.SetObjectReferenceCurve(clip, binding, RetimingObjectKeys(sourceKeys, settings));
                    modifiedCurveCount++;
                }

                warningSummary = modifiedCurveCount == 0
                    ? "No animation curves were found to retime."
                    : string.Empty;
                return modifiedCurveCount;
            }

            private static AnimationCurve RetimingCurve(AnimationCurve sourceCurve, ClipEditSettings settings)
            {
                // [QC][Invariant:time_range]
                // The saved clip is normalized to start at t=0 and uses speed-adjusted duration.
                // Boundary keys are inserted so trimmed clips keep deterministic first/last poses.
                float rangeStart = settings.RangeStart;
                float rangeEnd = Mathf.Max(settings.RangeStart + 0.01f, settings.RangeEnd);
                float speed = Mathf.Max(0.01f, settings.Speed);
                float outputDuration = Mathf.Max(0.01f, (rangeEnd - rangeStart) / speed);
                List<Keyframe> keys = new List<Keyframe>();

                AddRetimedKey(keys, new Keyframe(0f, sourceCurve.Evaluate(rangeStart)));
                Keyframe[] sourceKeys = sourceCurve.keys;
                for (int i = 0; i < sourceKeys.Length; i++)
                {
                    Keyframe sourceKey = sourceKeys[i];
                    if (sourceKey.time <= rangeStart || sourceKey.time >= rangeEnd)
                    {
                        continue;
                    }

                    Keyframe key = sourceKey;
                    key.time = (sourceKey.time - rangeStart) / speed;
                    key.inTangent *= speed;
                    key.outTangent *= speed;
                    AddRetimedKey(keys, key);
                }

                AddRetimedKey(keys, new Keyframe(outputDuration, sourceCurve.Evaluate(rangeEnd)));

                AnimationCurve retimedCurve = new AnimationCurve(keys.ToArray());
                retimedCurve.preWrapMode = sourceCurve.preWrapMode;
                retimedCurve.postWrapMode = settings.Loop ? WrapMode.Loop : sourceCurve.postWrapMode;
                return retimedCurve;
            }

            private static ObjectReferenceKeyframe[] RetimingObjectKeys(ObjectReferenceKeyframe[] sourceKeys, ClipEditSettings settings)
            {
                float rangeStart = settings.RangeStart;
                float rangeEnd = Mathf.Max(settings.RangeStart + 0.01f, settings.RangeEnd);
                float speed = Mathf.Max(0.01f, settings.Speed);
                List<ObjectReferenceKeyframe> keys = new List<ObjectReferenceKeyframe>();

                for (int i = 0; i < sourceKeys.Length; i++)
                {
                    ObjectReferenceKeyframe sourceKey = sourceKeys[i];
                    if (sourceKey.time < rangeStart || sourceKey.time > rangeEnd)
                    {
                        continue;
                    }

                    ObjectReferenceKeyframe key = sourceKey;
                    key.time = (sourceKey.time - rangeStart) / speed;
                    keys.Add(key);
                }

                return keys.ToArray();
            }

            private static void AddRetimedKey(List<Keyframe> keys, Keyframe key)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (Mathf.Abs(keys[i].time - key.time) <= 0.0001f)
                    {
                        keys[i] = key;
                        return;
                    }
                }

                keys.Add(key);
            }

            private static void ApplyClipLoopSetting(AnimationClip clip, bool loop)
            {
                // [QC][UnitySerialization]
                // wrapMode alone is not enough for imported .anim loop state; m_LoopTime is the Project setting Unity reads.
                clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Default;

                SerializedObject serializedClip = new SerializedObject(clip);
                SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
                SerializedProperty loopTime = settings == null ? null : settings.FindPropertyRelative("m_LoopTime");
                if (loopTime != null)
                {
                    loopTime.boolValue = loop;
                    serializedClip.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            private static void EnsureOutputFolder(string outputRoot)
            {
                if (AssetDatabase.IsValidFolder(outputRoot))
                {
                    return;
                }

                string parent = Path.GetDirectoryName(outputRoot);
                string child = Path.GetFileName(outputRoot);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                {
                    return;
                }

                parent = parent.Replace('\\', '/');
                EnsureFolder(parent, child);
            }
            private static string CreateNextEditPath(string sourceClipName, string outputRoot, string suffix)
            {
                string safeName = MakeSafeFileName(sourceClipName);
                for (int i = 1; i <= 999; i++)
                {
                    string candidate = outputRoot + "/" + safeName + "_" + suffix + "_" + i.ToString("000") + ".anim";
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(candidate) == null)
                    {
                        return candidate;
                    }
                }

                return AssetDatabase.GenerateUniqueAssetPath(outputRoot + "/" + safeName + "_" + suffix + ".anim");
            }

            private static string MakeSafeFileName(string value)
            {
                string safeName = string.IsNullOrEmpty(value) ? "clip_edit" : value.Trim();
                char[] invalidChars = Path.GetInvalidFileNameChars();
                for (int i = 0; i < invalidChars.Length; i++)
                {
                    safeName = safeName.Replace(invalidChars[i], '_');
                }

                return string.IsNullOrEmpty(safeName) ? "clip_edit" : safeName;
            }
        }

        private struct SafetySnapshot
        {
            public readonly SafetyLevel Level;
            public readonly string Message;
            public readonly string Detail;
            public readonly string MatchedKeyword;
            public readonly string LogPath;
            public readonly long LogSizeBytes;
            public readonly DateTime LogLastWriteUtc;

            private SafetySnapshot(SafetyLevel level, string message, string detail, string matchedKeyword, string logPath, long logSizeBytes, DateTime logLastWriteUtc)
            {
                Level = level;
                Message = message;
                Detail = detail;
                MatchedKeyword = matchedKeyword;
                LogPath = logPath;
                LogSizeBytes = logSizeBytes;
                LogLastWriteUtc = logLastWriteUtc;
            }

            public bool HasBlockingRisk
            {
                get { return Level == SafetyLevel.HardBlock; }
            }

            public bool HasWarning
            {
                get { return Level != SafetyLevel.Ok; }
            }

            public bool IsRecoverable
            {
                get { return Level == SafetyLevel.Recoverable; }
            }

            public static SafetySnapshot Unknown(string message)
            {
                return new SafetySnapshot(SafetyLevel.Recoverable, message, string.Empty, string.Empty, string.Empty, 0L, DateTime.MinValue);
            }

            public static SafetySnapshot Ok(string message, string detail, string logPath, long logSizeBytes, DateTime logLastWriteUtc)
            {
                return new SafetySnapshot(SafetyLevel.Ok, message, detail, string.Empty, logPath, logSizeBytes, logLastWriteUtc);
            }

            public static SafetySnapshot Warning(string message, string detail, string logPath, long logSizeBytes, DateTime logLastWriteUtc)
            {
                return new SafetySnapshot(SafetyLevel.Recoverable, message, detail, string.Empty, logPath, logSizeBytes, logLastWriteUtc);
            }

            public static SafetySnapshot Blocked(string message, string detail, string matchedKeyword, string logPath, long logSizeBytes, DateTime logLastWriteUtc)
            {
                return new SafetySnapshot(SafetyLevel.HardBlock, message, detail, matchedKeyword, logPath, logSizeBytes, logLastWriteUtc);
            }
        }

        private struct ValidationMessage
        {
            public readonly string Text;
            public readonly MessageType Type;

            public ValidationMessage(string text, MessageType type)
            {
                Text = text;
                Type = type;
            }
        }
    }
}
