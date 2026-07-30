using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Play-mode gating and the log based safety snapshot.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private SafetySnapshot safetySnapshot = SafetySnapshot.Unknown("Safety status has not been checked yet.");

        private enum SafetyLevel
        {
            Ok,
            Recoverable,
            HardBlock
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                RestoreTemporarySelectedMotionPreview();
                RestoreTemporaryClipAdjustPreview();
                activePreviewStage = -1;
                SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
                // Disarms the watcher and puts back the playback slot / Run In Background that live preview
                // borrowed, the same way the other two preview flows restore themselves above.
                RestoreLivePreviewState();
                EditorApplication.delayCall += () =>
                {
                    SyncPreviewBody();
                    Repaint();
                };
                return;
            }

            // The stand-in must be gone before the real avatar spawns, or the two overlap.
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ClearPreviewBody();
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

            if (previewStage == PreviewStageClipAdjust && !PrepareClipAdjustPreviewBeforePlay())
            {
                return;
            }

            activePreviewStage = previewStage;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            EditorApplication.isPlaying = true;
            statusMessage = "Play Mode requested. Scene View will focus on LOADER after Play starts. / 실행 후 Scene View를 LOADER에 맞춥니다.";
        }

        /// <summary>
        /// Enters Play with the Blender watcher armed. Separate from RequestPlayMode because it has one extra
        /// hard prerequisite and one extra setup pass that must both happen before the domain is live.
        /// </summary>
        private void RequestLivePreviewPlay()
        {
            // Gate FIRST. PrepareLivePreview rewrites importer settings and the override controller, so running
            // it before this check would mutate assets and then refuse to play - leaving the project changed for
            // nothing. RequestPlayMode checks in this order for the same reason.
            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (!CanEnterPlayMode(snapshot))
            {
                statusMessage = "안전 상태 때문에 Play를 막았습니다. 위 Safe Status를 확인하세요.";
                return;
            }

            // Not a suggestion: a reimport during Play recompiles nothing, but ANY script compile while the
            // ZEPETO context is alive nulls its internals and the avatar freezes. The existing UI only warns
            // about this; for the live loop it has to be enforced or the feature breaks itself. Left on
            // afterwards deliberately - it is the safe setting for this project either way, and the helper
            // already recommends it with its own button.
            bool changedCompilePref = false;
            if (EditorPrefs.GetInt(ScriptCompilationDuringPlayPrefKey, RecompileAndContinuePlaying)
                != RecompileAfterFinishedPlaying)
            {
                EditorPrefs.SetInt(ScriptCompilationDuringPlayPrefKey, RecompileAfterFinishedPlaying);
                changedCompilePref = true;
            }

            string prepareMessage;
            if (!PrepareLivePreview(out prepareMessage))
            {
                statusMessage = "라이브 확인 준비 실패 — " + prepareMessage;
                ValidateState();
                return;
            }

            if (changedCompilePref)
            {
                prepareMessage += ", Play 중 재컴파일 끔";
            }

            LiveReloadArmed = true;
            // Live preview is card 5 (DrawStep5CheckOnMyCharacter, Flow.cs), which owns no stage of its own: the
            // internal stage machine has four stages for seven cards, and everything about trying a motion on the
            // avatar - card 2's preview and this - sits in PreviewStageMotion. So that stage owns this Play
            // session. Leaving it at -1 meant no stage owned it, which used to grey out every Stop in the window.
            activePreviewStage = PreviewStageMotion;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            EditorApplication.isPlaying = true;
            statusMessage = "라이브 확인을 시작합니다 (" + prepareMessage
                + "). 이제 Blender에서 'Unity로 보내기'를 누르면 여기서 바로 바뀝니다.";
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
                // [QC][Invariant:actionable_block]
                // Clearing the console does not repair a broken ZEPETO context, so the message must name the real
                // remedy: leave Play, keep scripts from recompiling mid-Play, then Play again.
                return SafetySnapshot.Blocked(
                    "ZEPETO SDK 내부 상태가 깨졌습니다. 아바타가 움직이지 않습니다. / ZEPETO SDK context is broken.",
                    "감지된 패턴: " + riskKeyword + ". 대부분 Play 도중에 스크립트가 다시 컴파일되어 생깁니다. "
                        + "Stop을 눌러 Play를 끝내고, 위의 'Play 중 재컴파일 끄기'를 적용한 뒤 다시 Play하세요. "
                        + "Play를 끝내지 않고 Console만 지우면 복구되지 않습니다.",
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
    }
}
