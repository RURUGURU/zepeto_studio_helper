using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Watches the Blender output folder while Play is running and swaps the motion onto the live avatar
    /// without leaving Play mode.
    ///
    /// Why this is worth the machinery: the avatar with the user's own face, body shape and outfit only exists
    /// during Play, because it is downloaded at runtime. Checking a motion against it used to cost nine steps
    /// per tweak (Blender export, focus Unity, click the fbx, configure, extract, pick it in the dropdown,
    /// apply, Play, Stop). From the second iteration it is one button press in Blender plus refocusing Unity.
    ///
    /// The trick that avoids a rebind: a single fixed clip asset (LivePreviewClipPath) is bound into every
    /// override slot BEFORE Play. During Play only that asset's CONTENTS are rewritten via
    /// EditorUtility.CopySerialized, so the Animator keeps pointing at the same object and picks the new
    /// motion up in place. Rebinding a controller mid-Play is what tends to reset the ZEPETO context.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// Where the Blender add-on actually writes. This is deliberately NOT CustomMotionRoot.
        ///
        /// zepeto_motion_helper.py's DEFAULT_EXPORT_DIR is "&lt;project&gt;/Assets/CustomMotions", while
        /// CustomMotionRoot ("Assets/ZepetoHelper/Motions") is where extracted .anim copies live. The names are
        /// close enough to invite "cleaning this up" - do not. Watching the wrong one makes the whole feature
        /// silently never fire, which is exactly the bug this const was introduced to fix.
        /// </summary>
        private const string LiveWatchRoot = "Assets/CustomMotions";

        private const string LivePreviewClipPath = CustomMotionRoot + "/LiveFromBlender.anim";
        private const double LivePollIntervalSeconds = 0.4d;

        private const string LiveArmedSessionKey = "Easy.ZepetoHelper.LiveReloadArmed";
        private const string LiveRestorePathSessionKey = "Easy.ZepetoHelper.LiveRestorePath";
        private const string LiveRestoreActiveSessionKey = "Easy.ZepetoHelper.LiveRestoreActive";
        private const string LiveRunInBackgroundSessionKey = "Easy.ZepetoHelper.LivePrevRunInBackground";
        private const string LiveWatchFileSessionKey = "Easy.ZepetoHelper.LiveWatchFile";
        private const string LiveWatchStampSessionKey = "Easy.ZepetoHelper.LiveWatchStamp";
        private const string LiveWatchSizeSessionKey = "Easy.ZepetoHelper.LiveWatchSize";
        private const string LiveReloadCountSessionKey = "Easy.ZepetoHelper.LiveReloadCount";
        private const string LiveMessageSessionKey = "Easy.ZepetoHelper.LiveMessage";

        private double lastLivePollTime;
        private long livePendingSize = -1L;
        private bool liveReloadInFlight;

        internal static string LiveClipAssetPath
        {
            get { return LivePreviewClipPath; }
        }

        internal static string LiveWatchFolder
        {
            get { return LiveWatchRoot; }
        }

        // All cross-Play state lives in SessionState. Entering Play triggers a domain reload, which resets every
        // non-serialized instance field of an EditorWindow - a plain bool would come back false exactly when the
        // watcher is supposed to start working. activePreviewStage already uses SessionState for the same reason.
        private static bool LiveReloadArmed
        {
            get { return SessionState.GetBool(LiveArmedSessionKey, false); }
            set { SessionState.SetBool(LiveArmedSessionKey, value); }
        }

        private static string LiveWatchedFile
        {
            get { return SessionState.GetString(LiveWatchFileSessionKey, string.Empty); }
            set { SessionState.SetString(LiveWatchFileSessionKey, value ?? string.Empty); }
        }

        private static string LiveWatchedStamp
        {
            get { return SessionState.GetString(LiveWatchStampSessionKey, string.Empty); }
            set { SessionState.SetString(LiveWatchStampSessionKey, value ?? string.Empty); }
        }

        // Stored as a string: SessionState has no long overload, and truncating a file size into an int would
        // make two different files compare equal once anything crosses 2 GB.
        private static long LiveWatchedSize
        {
            get
            {
                long parsed;
                return long.TryParse(SessionState.GetString(LiveWatchSizeSessionKey, string.Empty), out parsed)
                    ? parsed
                    : -1L;
            }

            set { SessionState.SetString(LiveWatchSizeSessionKey, value.ToString()); }
        }

        private static int LiveReloadCount
        {
            get { return SessionState.GetInt(LiveReloadCountSessionKey, 0); }
            set { SessionState.SetInt(LiveReloadCountSessionKey, value); }
        }

        private static string LiveMessage
        {
            get { return SessionState.GetString(LiveMessageSessionKey, string.Empty); }
            set { SessionState.SetString(LiveMessageSessionKey, value ?? string.Empty); }
        }

        private void SubscribeLiveReload()
        {
            EditorApplication.update += PumpLiveReload;

            // Closing the helper window mid-Play unsubscribes playModeStateChanged, so EnteredEditMode never
            // fires and nothing puts the playback slot or Run In Background back. Catch that on the next open:
            // if we are not playing, any leftover live-preview state is stale by definition.
            //
            // isPlayingOrWillChangePlaymode, not isPlaying: OnEnable also runs during the domain reload that
            // ENTERS Play, and isPlaying is not reliably true yet at that point. Using isPlaying would disarm
            // the watcher at the exact moment it is supposed to start, and the feature would silently do
            // nothing - the same class of bug as the SessionState one this replaced.
            if (!EditorApplication.isPlayingOrWillChangePlaymode
                && (LiveReloadArmed || SessionState.GetBool(LiveRestoreActiveSessionKey, false)))
            {
                // Deferred: OnEnable runs SubscribeLiveReload before RefreshAll, so the LOADER's serialized
                // fields are not bound yet and ApplyClipToOverrideController would fail silently.
                EditorApplication.delayCall += RestoreLivePreviewState;
            }
        }

        private void UnsubscribeLiveReload()
        {
            EditorApplication.update -= PumpLiveReload;
        }

        /// <summary>
        /// Poll rather than FileSystemWatcher: the watcher fires on the first write of a multi-megabyte fbx, so
        /// it would reimport a partial file.
        /// </summary>
        private void PumpLiveReload()
        {
            if (!LiveReloadArmed || !EditorApplication.isPlaying)
            {
                return;
            }

            // A reimport pumps the editor loop, so this can re-enter. Without the guard two imports of the same
            // file can interleave and the second CopySerialized reads a half-imported clip.
            if (liveReloadInFlight || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - lastLivePollTime < LivePollIntervalSeconds)
            {
                return;
            }

            lastLivePollTime = EditorApplication.timeSinceStartup;

            string newest;
            long size;
            DateTime stamp;
            string blockReason;
            if (!TryFindNewestMotionFile(out newest, out size, out stamp, out blockReason))
            {
                // Report the reason once rather than every 0.4s, otherwise the panel repaints forever.
                if (LiveMessage != blockReason)
                {
                    LiveMessage = blockReason;
                    Repaint();
                }

                return;
            }

            string stampText = stamp.ToString("O");
            if (string.Equals(newest, LiveWatchedFile, StringComparison.OrdinalIgnoreCase)
                && size == LiveWatchedSize
                && string.Equals(stampText, LiveWatchedStamp, StringComparison.Ordinal))
            {
                livePendingSize = -1L;
                return;
            }

            // First sighting of a change: remember the size and wait one tick to see it settle.
            if (livePendingSize != size)
            {
                livePendingSize = size;
                return;
            }

            LiveWatchedFile = newest;
            LiveWatchedSize = size;
            LiveWatchedStamp = stampText;
            livePendingSize = -1L;

            liveReloadInFlight = true;
            try
            {
                string message;
                if (TryPushMotionToLiveAvatar(newest, out message))
                {
                    LiveReloadCount = LiveReloadCount + 1;
                }

                LiveMessage = message;
            }
            catch (Exception exception)
            {
                // An exception here would otherwise repeat every 0.4s and bury the console.
                LiveMessage = "적용 중 오류: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                liveReloadInFlight = false;
            }

            Repaint();
        }

        private static bool TryFindNewestMotionFile(out string path, out long size, out DateTime stamp, out string blockReason)
        {
            path = string.Empty;
            size = -1L;
            stamp = default(DateTime);
            blockReason = string.Empty;

            string absoluteFolder = ToAbsoluteProjectPath(LiveWatchRoot);
            if (!Directory.Exists(absoluteFolder))
            {
                blockReason = "감시 폴더가 없습니다: " + LiveWatchRoot;
                return false;
            }

            string[] files = Directory.GetFiles(absoluteFolder, "*.fbx", SearchOption.TopDirectoryOnly);
            DateTime newest = DateTime.MinValue;
            string winner = string.Empty;

            for (int i = 0; i < files.Length; i++)
            {
                // Blender writes "<name>.fbx.part" then renames. Windows can match that with "*.fbx" via short
                // names, and importing a partial fbx bakes a corrupt clip.
                if (files[i].EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime written = File.GetLastWriteTimeUtc(files[i]);
                if (written > newest)
                {
                    newest = written;
                    winner = files[i];
                }
            }

            if (string.IsNullOrEmpty(winner))
            {
                blockReason = "감시 폴더에 FBX가 없습니다: " + LiveWatchRoot
                    + " (Blender 패널의 '폴더' 값이 이 경로인지 확인하세요)";
                return false;
            }

            path = winner;
            stamp = newest;
            try
            {
                size = new FileInfo(winner).Length;
            }
            catch (IOException)
            {
                blockReason = "파일을 읽는 중입니다: " + Path.GetFileName(winner);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reimports the changed fbx and copies its clip into the live clip asset in place.
        /// </summary>
        private bool TryPushMotionToLiveAvatar(string absoluteFbxPath, out string message)
        {
            message = string.Empty;

            string assetPath = ToProjectRelativePath(absoluteFbxPath);
            if (string.IsNullOrEmpty(assetPath))
            {
                message = "프로젝트 밖의 파일입니다: " + absoluteFbxPath;
                return false;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            string rangeNote;
            RefreshClipRangeIfStale(assetPath, out rangeNote);

            AnimationClip source = FindLiveSourceClip(assetPath);
            if (source == null)
            {
                message = Path.GetFileName(assetPath) + ": 애니메이션 클립이 없습니다. Blender에서 키프레임을 "
                    + "2개 이상 찍었는지 확인하세요.";
                return false;
            }

            if (!source.isHumanMotion)
            {
                message = Path.GetFileName(assetPath) + ": 아직 Humanoid가 아닙니다. 새 이름으로 내보냈다면 "
                    + "Play를 멈추고 '1. FBX를 ZEPETO용으로 설정'을 한 번 누른 뒤 다시 시작하세요. "
                    + "다음부터는 같은 이름에 덮어쓰면 이 단계가 필요 없습니다.";
                return false;
            }

            AnimationClip live = AssetDatabase.LoadAssetAtPath<AnimationClip>(LivePreviewClipPath);
            if (live == null)
            {
                message = "라이브 클립이 없습니다: " + LivePreviewClipPath;
                return false;
            }

            // CopySerialized overwrites m_Name too, so the asset would be renamed to the fbx's clip name and the
            // .anim file and its object would disagree. Restore it.
            string keepName = live.name;
            EditorUtility.CopySerialized(source, live);
            live.name = keepName;

            // The fbx sub-asset is hidden inside the model; copying its flags across would hide the .anim too.
            live.hideFlags = HideFlags.None;

            // Blender authors a 2 second cycle. CopySerialized brings the fbx clip's own loop state across, so
            // this has to be re-applied on every reload, not just at setup - without it the motion plays once
            // and freezes, which reads as "the tool broke".
            ApplyClipLoopSetting(live, true);
            EditorUtility.SetDirty(live);

            message = Path.GetFileName(assetPath) + " → 적용했습니다 ("
                + live.length.ToString("0.00") + "초, " + (LiveReloadCount + 1) + "번째)"
                + (string.IsNullOrEmpty(rangeNote) ? string.Empty : " / " + rangeNote);
            return true;
        }

        /// <summary>
        /// Re-derives the clip list when the fbx's own take no longer matches what the .meta pinned.
        ///
        /// TryConfigureMotionFbx has to write importer.clipAnimations, because the Root Transform lock flags
        /// exist only on ModelImporterClipAnimation and nowhere else on ModelImporter. The side effect is that
        /// the importer stops following the file's takes: change the motion length in Blender from 48 to 96
        /// frames and the reimport still produces the pinned 48, silently discarding the rest. Reimporting an
        /// asset does not reload the domain, so this is safe to do during Play - it is only slow.
        /// </summary>
        private static void RefreshClipRangeIfStale(string assetPath, out string note)
        {
            note = string.Empty;

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            ModelImporterClipAnimation[] pinned = importer.clipAnimations;
            ModelImporterClipAnimation[] actual = importer.defaultClipAnimations;
            if (pinned == null || pinned.Length == 0 || actual == null || actual.Length == 0)
            {
                return;
            }

            bool stale = pinned.Length != actual.Length;
            if (!stale)
            {
                for (int i = 0; i < pinned.Length; i++)
                {
                    if (!Mathf.Approximately(pinned[i].firstFrame, actual[i].firstFrame)
                        || !Mathf.Approximately(pinned[i].lastFrame, actual[i].lastFrame)
                        || pinned[i].takeName != actual[i].takeName)
                    {
                        stale = true;
                        break;
                    }
                }
            }

            if (!stale)
            {
                return;
            }

            float oldLast = pinned[0].lastFrame;
            for (int i = 0; i < actual.Length; i++)
            {
                actual[i].lockRootRotation = true;
                actual[i].keepOriginalOrientation = true;
                actual[i].lockRootHeightY = true;
                actual[i].keepOriginalPositionY = true;
                actual[i].lockRootPositionXZ = true;
                actual[i].keepOriginalPositionXZ = false;
            }

            importer.clipAnimations = actual;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            note = "길이 변경 반영 (" + oldLast.ToString("0") + " → " + actual[0].lastFrame.ToString("0") + "프레임)";
        }

        private static AnimationClip FindLiveSourceClip(string assetPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                AnimationClip candidate = assets[i] as AnimationClip;
                if (candidate != null && (candidate.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Everything that must happen while still in Edit mode: make sure the controller is project-local,
        /// remember what was in the playback slot so Stop can put it back, create the live clip asset, bind it,
        /// and pre-set every Blender fbx to Humanoid so the in-Play reimport never writes importer settings.
        /// </summary>
        private bool PrepareLivePreview(out string message)
        {
            message = string.Empty;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play를 멈춘 상태에서 준비해야 합니다.";
                return false;
            }

            // Same prerequisite RequestPlayMode enforces: writing into the SDK's package copy would corrupt it,
            // so a project-local controller has to exist first. Without this the button dead-ends on a fresh
            // project with "AnimatorController가 아직 package 원본입니다".
            if (IsPackageOrPackageCachePath(GetAnimatorControllerPath()))
            {
                string controllerMessage;
                if (!EnsureLocalAnimatorController(out controllerMessage))
                {
                    message = "local AnimatorController를 만들지 못했습니다: " + controllerMessage;
                    return false;
                }
            }

            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Motions");
            EnsureFolder("Assets", "CustomMotions");

            List<string> notes = new List<string>();

            // Capture the current playback clip BEFORE binding, so Stop can restore it. Guarded twice: never
            // capture the live clip itself (that would destroy the pointer back to the user's real clip), and
            // never overwrite an existing capture that has not been restored yet.
            if (!SessionState.GetBool(LiveRestoreActiveSessionKey, false))
            {
                string currentPath = AssetDatabase.GetAssetPath(GetPlaybackClip());
                if (!string.IsNullOrEmpty(currentPath)
                    && !string.Equals(currentPath, LivePreviewClipPath, StringComparison.OrdinalIgnoreCase))
                {
                    SessionState.SetString(LiveRestorePathSessionKey, currentPath);
                    SessionState.SetBool(LiveRestoreActiveSessionKey, true);
                    notes.Add("기존 동작 기억");
                }
            }

            int configured = ConfigureMotionFolderForLivePreview();
            if (configured > 0)
            {
                notes.Add("FBX " + configured + "개 Humanoid 설정");
            }

            AnimationClip live = AssetDatabase.LoadAssetAtPath<AnimationClip>(LivePreviewClipPath);
            if (live == null)
            {
                AnimationClip seed = GetPlaybackClip();
                AnimationClip created = seed != null
                    ? UnityEngine.Object.Instantiate(seed)
                    : new AnimationClip();
                created.name = Path.GetFileNameWithoutExtension(LivePreviewClipPath);
                AssetDatabase.CreateAsset(created, LivePreviewClipPath);
                AssetDatabase.SaveAssets();
                live = AssetDatabase.LoadAssetAtPath<AnimationClip>(LivePreviewClipPath);
                notes.Add("라이브 클립 생성");
            }

            if (live == null)
            {
                message = "라이브 클립을 만들지 못했습니다: " + LivePreviewClipPath;
                return false;
            }

            ApplyClipLoopSetting(live, true);

            string bindMessage;
            if (!ApplyClipToOverrideController(live, out bindMessage))
            {
                message = "라이브 클립을 재생 슬롯에 연결하지 못했습니다: " + bindMessage;
                return false;
            }

            // Play pauses whenever the editor is not the active application, and this whole feature depends on
            // the user being in Blender. ProjectSettings ships runInBackground: 0, so it has to be turned on -
            // and put back on Stop, so the project does not gain a stray diff.
            if (!PlayerSettings.runInBackground)
            {
                SessionState.SetBool(LiveRunInBackgroundSessionKey, false);
                PlayerSettings.runInBackground = true;
                notes.Add("Run In Background 켬");
            }

            AssetDatabase.SaveAssets();

            LiveWatchedFile = string.Empty;
            LiveWatchedSize = -1L;
            LiveWatchedStamp = string.Empty;
            LiveReloadCount = 0;
            LiveMessage = string.Empty;
            livePendingSize = -1L;

            notes.Add("재생 슬롯 연결");
            message = string.Join(", ", notes.ToArray());
            return true;
        }

        /// <summary>
        /// Puts back what live preview borrowed. Called from OnPlayModeStateChanged/EnteredEditMode, alongside
        /// the two restore paths that already existed for the other preview flows.
        /// </summary>
        private void RestoreLivePreviewState()
        {
            LiveReloadArmed = false;

            if (SessionState.GetBool(LiveRunInBackgroundSessionKey, true) == false)
            {
                PlayerSettings.runInBackground = false;
                SessionState.EraseBool(LiveRunInBackgroundSessionKey);
            }

            if (!SessionState.GetBool(LiveRestoreActiveSessionKey, false))
            {
                return;
            }

            string restorePath = SessionState.GetString(LiveRestorePathSessionKey, string.Empty);
            AnimationClip restoreClip = string.IsNullOrEmpty(restorePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<AnimationClip>(restorePath);

            if (restoreClip == null)
            {
                // Nothing to put back (the clip was deleted, or the path was never captured). Clear the flag so
                // this does not retry forever.
                SessionState.EraseBool(LiveRestoreActiveSessionKey);
                SessionState.EraseString(LiveRestorePathSessionKey);
                return;
            }

            string restoreMessage;
            if (!ApplyClipToOverrideController(restoreClip, out restoreMessage))
            {
                // Keep the flag: the usual cause is the LOADER not being bound yet, which the next open fixes.
                // Losing it here would leave the user's clip permanently replaced by LiveFromBlender.anim.
                return;
            }

            SessionState.EraseBool(LiveRestoreActiveSessionKey);
            SessionState.EraseString(LiveRestorePathSessionKey);
        }

        /// <summary>
        /// Pre-sets Animation Type on every fbx in the Blender output folder, with a progress bar because a
        /// folder of multi-megabyte fbx files takes long enough that the editor looks hung.
        /// </summary>
        private int ConfigureMotionFolderForLivePreview()
        {
            string absoluteFolder = ToAbsoluteProjectPath(LiveWatchRoot);
            if (!Directory.Exists(absoluteFolder))
            {
                return 0;
            }

            string[] files = Directory.GetFiles(absoluteFolder, "*.fbx", SearchOption.TopDirectoryOnly);
            int changed = 0;

            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (files[i].EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string assetPath = ToProjectRelativePath(files[i]);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "라이브 확인 준비",
                        Path.GetFileName(assetPath) + " 설정 중...",
                        files.Length == 0 ? 1f : (float)i / files.Length);

                    string configureMessage;
                    if (TryConfigureMotionFbx(assetPath, out configureMessage))
                    {
                        changed++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return changed;
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return string.Empty;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string normalized = absolutePath.Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/');
            if (!root.EndsWith("/"))
            {
                root += "/";
            }

            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : string.Empty;
        }

        /// <summary>
        /// Sits just under step 2, where the user is already looking after importing a motion.
        /// </summary>
        /// <summary>
        /// Every control here is drawn unconditionally, in a fixed order, with only `enabled`, the label, the
        /// text and the MessageType varying.
        ///
        /// This panel is the one the user watches while toggling Play, and isPlaying / LiveReloadArmed /
        /// LiveMessage all change asynchronously - the last two from EditorApplication.update in
        /// PumpLiveReload. Branching the control COUNT or ORDER on any of them means the Repaint pass sees a
        /// layout the Layout pass never recorded, which corrupts the group and makes controls flicker out.
        /// Same rule as the header Stop button; see DrawV7WorkbenchHeader.
        /// </summary>
        private void DrawLivePreviewBody()
        {
            bool playing = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            bool armed = LiveReloadArmed;
            bool live = playing && armed;

            DrawStatusRow("상태", playing
                ? (armed ? "연결됨" : "Play 중 (라이브 연결 아님)")
                : "정지");
            DrawStatusRow("감시 폴더", LiveWatchRoot);
            DrawStatusRow("적용된 횟수", live ? LiveReloadCount.ToString() : "-");

            string watched = LiveWatchedFile;
            DrawStatusRow("마지막 파일", live && !string.IsNullOrEmpty(watched)
                ? Path.GetFileName(watched)
                : "-");

            string liveMessage = LiveMessage;
            DrawMiniHelp(
                string.IsNullOrEmpty(liveMessage) ? " " : liveMessage,
                string.IsNullOrEmpty(liveMessage) || liveMessage.Contains("적용했습니다")
                    ? MessageType.Info
                    : MessageType.Warning);

            if (DrawColoredActionButton("내 캐릭터로 확인 시작 (Play)", !playing, PlayGreen, GUILayout.Height(34f)))
            {
                RequestLivePreviewPlay();
            }

            if (DrawColoredActionButton(
                    live ? "■ Stop (원래 동작으로 되돌림)" : "■ Stop",
                    playing, StopRed, GUILayout.Height(26f)))
            {
                StopPlayMode();
            }

            string help;
            MessageType helpType;
            if (!playing)
            {
                help = "누르면 Play가 켜지고 내 ZEPETO 아바타(얼굴·머리·옷 전부)가 서버에서 내려옵니다. "
                    + "확인이 끝날 때까지 Play는 끄지 마세요.\n\n"
                    + "① Blender로 전환해 포즈를 고치고 'Unity로 보내기'\n"
                    + "② Unity 창을 다시 클릭해서 맨 앞으로 가져오기\n"
                    + "③ 1~2초 안에 동작이 바뀌고 위 '적용된 횟수'가 1 올라갑니다\n\n"
                    + "②를 건너뛰면 안 됩니다. Unity는 뒤에 있는 동안 Play가 멈춰 있어서 파일 감시도 쉬어갑니다. "
                    + "다만 Unity에서 누를 버튼은 없습니다 — 창을 앞으로 가져오는 것까지가 전부입니다.\n\n"
                    + "Blender에서는 같은 이름에 덮어쓰세요. 새 이름으로 내보내면 Humanoid 설정이 없어서 "
                    + "한 번은 Play를 멈춰야 합니다.";
                helpType = MessageType.None;
            }
            else if (armed)
            {
                help = "Blender에서 포즈를 고치고 'Unity로 보내기'를 누른 뒤, Unity 창을 다시 클릭해 맨 앞으로 "
                    + "가져오세요. 위 '적용된 횟수'가 올라가면 적용된 것입니다.";
                helpType = MessageType.None;
            }
            else
            {
                help = "지금 Play는 라이브 연결 없이 시작된 상태입니다. Stop을 누른 뒤 위 초록 버튼으로 다시 시작하면 "
                    + "Blender와 연결됩니다.";
                helpType = MessageType.Warning;
            }

            DrawMiniHelp(help, helpType);
        }
    }
}
