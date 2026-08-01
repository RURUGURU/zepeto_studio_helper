using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Play 진입 관문과, Unity 로그를 근거로 만드는 안전 스냅샷.
    /// 이 창의 Play 버튼은 전부 여기를 지나가고, 경고 패널이 보여주는 이유도 전부 여기서 나온다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        private SafetySnapshot safetySnapshot = SafetySnapshot.Unknown("Safety status has not been checked yet.");

        // 세 단계뿐이고, 셋의 차이는 "사용자가 지금 무엇을 할 수 있는가"다.
        // Ok          - 그냥 작업하면 된다.
        // Recoverable - 경고. 복구 버튼으로 지우고 계속할 수 있다. Play는 막지 않는다.
        // HardBlock   - Play를 막는다. 막을 때는 고칠 방법을 함께 말할 수 있어야 한다.
        private enum SafetyLevel
        {
            Ok,
            Recoverable,
            HardBlock
        }

        /// <summary>
        /// Play 전환마다 이 창이 빌려 갔던 것을 되돌린다.
        /// </summary>
        /// <remarks>
        /// EnteredEditMode가 되돌리는 것은 셋이다: 선택 동작 미리보기 / 클립 조정 미리보기 / 라이브 확인.
        /// 실제 순서는 앞의 둘 -> activePreviewStage 지우기(+SessionState 기록) -> 라이브 확인 복원이다.
        /// "세션 끝" 표시가 세 번째 복원보다 앞에 있어도 되는 이유는, 세 복원 중 어느 것도 activePreviewStage를
        /// 읽지 않기 때문이다. RestoreLivePreviewState가 보는 것은 자기 SessionState 키
        /// (LiveRestoreActiveSessionKey / LiveRestorePathSessionKey / LiveRunInBackgroundSessionKey)뿐이고,
        /// activePreviewStage는 이 파일과 Workflow.cs가 쓰기만
        /// 하는 값이다(Steps.cs의 Stop 버튼 주석: "activePreviewStage를 읽는 코드는 하나도 없다").
        /// 그 값을 읽는 코드가 새로 생기면 그때는 이 순서가 뜻을 갖게 되므로, 지우기를 세 복원 뒤로 옮길 것.
        ///
        /// delayCall로 미루는 것은 갈래마다 다르다.
        /// EnteredEditMode - 세 복원과 stage 지우기는 그 자리에서 돌고, SyncPreviewBody와 Repaint만 미룬다.
        ///   앞의 것들은 Play가 빌려 간 에셋을 되돌려 놓는 일이고, 그중 RestoreLivePreviewState는 자기
        ///   &lt;summary&gt;대로 Edit 모드에서만 불릴 수 있다. 뒤의 둘은 씬에 대역 몸을 만들거나 치우고 창을 다시
        ///   그리는 표시 쪽 일이라 한 틱 뒤로 넘겨도 된다.
        /// ExitingEditMode - ClearPreviewBody 하나뿐이고 미루지 않는다. 진짜 아바타가 생기기 전에 대역 몸을
        ///   치워야 하고, 늦으면 둘이 겹쳐 보인다.
        /// EnteredPlayMode - 하는 일 전부를 미룬다. 이 콜백은 도메인 리로드 언저리에서 불리므로, 씬 오브젝트를
        ///   지금 찾으면(FindLoaderAndSerializedFields) 곧 사라지거나 아직 살아나지 않은 것을 잡고, 지금
        ///   Repaint하면 아직 없는 상태를 그린다. delayCall은 리로드가 끝난 다음 에디터 틱으로 넘긴다.
        /// </remarks>
        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                RestoreTemporarySelectedMotionPreview();
                RestoreTemporaryClipAdjustPreview();
                activePreviewStage = PreviewStageNone;
                SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
                // 감시를 끄고, 라이브 확인이 빌려 갔던 재생 슬롯과 Run In Background를 돌려놓는다.
                // 위 두 미리보기가 스스로 복원하는 것과 같은 자리다.
                RestoreLivePreviewState();
                EditorApplication.delayCall += () =>
                {
                    SyncPreviewBody();
                    Repaint();
                };
                return;
            }

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

        /// <summary>
        /// 모든 Play 버튼 뒤에 있는 술어. 네 조건은 각각 다른 실패를 막는다.
        /// </summary>
        /// <remarks>
        /// HasBlockingRisk - 안전 스냅샷이 이미 막아 둔 상태. 막은 이유는 snapshot.Message에 있고 UI가 그대로
        ///   보여주므로, 여기서 걸린 사용자는 화면에서 이유를 읽을 수 있다.
        /// isCompiling - 컴파일이 끝나면 도메인 리로드가 따라온다. Play 진입과 겹치면 이 창의 비직렬화
        ///   필드가 전부 초기화된 채로 Play가 시작된다.
        /// isUpdating - 에셋 임포트 중. 방금 저장한 clip이나 컨트롤러가 아직 AssetDatabase에 올라오지 않은
        ///   상태로 Play가 시작될 수 있다.
        /// isPlayingOrWillChangePlaymode - 이미 Play이거나 전환 중. 중복 요청은 고치는 것 없이 전환만 겹친다.
        /// </remarks>
        private static bool CanEnterPlayMode(SafetySnapshot snapshot)
        {
            return !snapshot.HasBlockingRisk
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private void StopPlayMode()
        {
            // Play 중이 아니어도 먼저 지운다. 이 버튼은 "이 미리보기 세션은 끝"이라는 뜻이고, 실제로 Play가
            // 아니었다면 SessionState에 남은 stage 번호는 어차피 낡은 값이다.
            activePreviewStage = PreviewStageNone;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                statusMessage = "Play Mode stop requested. / 실행 정지를 요청했습니다.";
            }
        }

        /// <summary>
        /// 카드에서 시작하는 모든 Play가 지나가는 문. 순서 자체가 안전장치다.
        /// </summary>
        /// <remarks>
        /// 관문이 먼저다. 아래 두 준비 단계(local AnimatorController 만들기, 클립 조정 임시 clip 굽기)는
        /// 프로젝트 에셋을 실제로 고치므로, 막힐 거면 고치기 전에 막아야 한다. RequestLivePreviewPlay도
        /// 같은 이유로 같은 순서를 지킨다.
        ///
        /// previewStage는 카드 번호가 아니라 내부 stage 번호다(Workflow.cs의 PreviewStage 상수 주석).
        /// previewStage == PreviewStageClipAdjust 분기는 카드 6의 Play 직전에 임시 clip을 굽는 유일한
        /// 지점이다. 상수 값을 카드 번호에 맞춰 옮기면 이 분기가 조용히 안 걸리고, 카드 6의 미리보기는
        /// 아무 오류 없이 조정 전 원본 clip을 재생한다 - 오류가 없어서 더 찾기 어렵다.
        /// </remarks>
        private void RequestPlayMode(int previewStage = PreviewStageNone)
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
        /// Blender 감시를 켠 채로 Play에 들어간다. RequestPlayMode와 따로 있는 이유는, 도메인이 살아나기
        /// 전에 반드시 끝나야 하는 강한 전제 하나와 준비 과정 하나가 더 있기 때문이다.
        /// </summary>
        private void RequestLivePreviewPlay()
        {
            // 관문이 먼저다. PrepareLivePreview는 importer 설정과 override controller를 고쳐 쓰므로, 이 검사
            // 앞에서 돌리면 에셋만 바꿔 놓고 Play는 거절하는 꼴이 된다 - 아무것도 못 얻고 프로젝트만 바뀐다.
            // RequestPlayMode가 같은 순서를 지키는 것도 같은 이유다.
            SafetySnapshot snapshot = GetSafetySnapshot(true);
            if (!CanEnterPlayMode(snapshot))
            {
                statusMessage = "안전 상태 때문에 Play를 막았습니다. 위 Safe Status를 확인하세요.";
                return;
            }

            // 권장이 아니라 강제다. Play 중 reimport는 재컴파일을 부르지 않지만, ZEPETO 컨텍스트가 살아 있는
            // 동안 스크립트가 한 번이라도 컴파일되면 컨텍스트 내부가 null이 되고 아바타가 멈춘다. 다른 화면은
            // 이걸 경고만 하지만, 라이브 루프는 강제하지 않으면 기능이 스스로를 깨뜨린다. 끝나고 원래대로
            // 되돌리지 않는 것도 의도한 것이다 - 이 프로젝트에서는 어차피 그쪽이 안전한 설정이고, 헬퍼가
            // 자기 버튼으로도 같은 값을 권한다.
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
            // 라이브 확인은 카드 5(DrawStep5CheckOnMyCharacter, Flow.cs)이고 자기 stage가 없다. 내부 stage
            // 기계는 카드 일곱 장에 stage가 넷뿐이라, 아바타에 동작을 입혀보는 일은 전부 - 카드 2의
            // 미리보기도 여기도 - PreviewStageMotion에 들어간다. 그래서 이 Play 세션의 주인은 그 stage다.
            //
            // 다만 지금은 기록일 뿐이다. 예전에는 -1로 두면 아무 stage도 이 세션을 소유하지 않아 창 안의 Stop이
            // 전부 회색으로 죽었는데, 그 게이팅은 이제 없다 - DrawStagePlayStopButtons의 주석대로 Stop은
            // activePreviewStage를 아예 읽지 않는다(Steps.cs). 값을 채워 두는 이유는 어느 미리보기가 돌고
            // 있는지 SessionState에 남겨 두기 위해서다.
            activePreviewStage = PreviewStageMotion;
            SessionState.SetInt(ActivePreviewStageSessionKey, activePreviewStage);
            EditorApplication.isPlaying = true;
            statusMessage = "라이브 확인을 시작합니다 (" + prepareMessage
                + "). 이제 Blender에서 'Unity로 보내기'를 누르면 여기서 바로 바뀝니다.";
        }

        /// <summary>
        /// "여기서부터 다시 센다" 표시. 카운터를 0으로 되돌리는 것만으로는 부족하다.
        /// </summary>
        /// <remarks>
        /// safetyLogBaselineBytes를 지금 로그 크기로 다시 잡아야, 이미 쌓여 있던 로그가 다음 검사에서
        /// "새로 자란 바이트"로 읽히지 않는다. 그게 없으면 복구를 눌러도 100MB 관문이 그대로 걸린다.
        /// lastSafetyRefreshTime = -1000d은 2초 타이머를 무효로 만들어 다음 GetSafetySnapshot이 무조건
        /// 다시 읽게 한다.
        ///
        /// 기준선을 다시 잡는 자리는 이 메서드 하나뿐이다. Export.cs의
        /// ResetHelperConsoleSummaryAfterSuccessfulExport도 여기를 부르므로, 계산을 손볼 일이 생기면 여기만
        /// 고치면 된다.
        /// </remarks>
        private void ResetHelperSessionCounters()
        {
            sessionWarningCount = 0;
            sessionErrorCount = 0;
            lastConsoleMessage = string.Empty;
            safetyStartedUtc = DateTime.UtcNow;
            safetyLogBaselineBytes = GetCurrentLogSize();
            lastSafetyRefreshTime = -1000d;
        }

        private void RecoverSafetyState()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                StopPlayMode();
            }

            string clearMessage;
            bool didClearConsole = TryClearUnityConsole(out clearMessage);
            ResetHelperSessionCounters();

            RefreshSafetySnapshot();
            ValidateState();

            // 복구했는데도 여전히 막혀 있으면 그 사실을 말해야 한다. 깨진 ZEPETO 컨텍스트는 콘솔을 지운다고
            // 낫지 않으므로, 여기서 "복구 완료"라고 말해 버리면 사용자는 같은 Play를 계속 다시 누르게 된다.
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
            ResetHelperSessionCounters();
            statusMessage = didClearConsole
                ? "Console and helper session summary cleared. / 콘솔과 헬퍼 세션 요약을 정리했습니다."
                : "Helper session summary cleared. Unity Console clear failed: " + clearMessage;
            RefreshSafetySnapshot();
            ValidateState();
        }

        // UnityEditor.LogEntries는 공개 API가 아니라 리플렉션으로 부른다. Unity 버전이 올라가면서 타입이
        // 옮겨 가거나 Clear가 사라지면 여기서 null이 나오는데, 그때는 실패를 문자열로 돌려주고 호출자가
        // 헬퍼 쪽 카운터만 정리하고 진행한다. 콘솔을 못 지웠다고 복구 흐름 전체가 멈추면 안 된다.
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

        // 로그 파일 자체가 없거나 지워졌을 수도 있으므로 세 단계로 물러선다: 파일 -> 그 폴더 -> 프로젝트 폴더.
        // 아무것도 안 열리는 것보다 한 단계 위 폴더라도 열어 주는 편이 낫다.
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

        /// <summary>
        /// 안전 상태 접근자. 실제로 다시 재는 것은 SafetyRefreshIntervalSeconds(2초)에 한 번뿐이다.
        /// </summary>
        /// <remarks>
        /// 이 값은 그리는 도중에 읽힌다. 매 repaint마다 로그 파일을 열면 OnGUI가 디스크에 묶인다.
        /// 그 타이머 때문에 스냅샷은 한 OnGUI의 Layout 패스와 Repaint 패스 사이에서 값이 바뀔 수 있다.
        /// 그래서 스냅샷으로 컨트롤의 존재를 정하면 안 되고 enabled와 글자만 정해야 한다
        /// (Steps.cs DrawWarningCleanupPanel이 그 규칙을 어겼다가 고친 자리다).
        ///
        /// force는 Play 진입처럼 "지금 이 순간의 답"이 필요한 곳에서만 쓴다.
        /// </remarks>
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

        // 로그를 한 번 읽어서 나온 것 전부. ReadError가 비어 있지 않으면 나머지 값은 믿을 수 없다.
        private struct LogReading
        {
            public string Path;
            public long SizeBytes;
            public long NewBytes;
            public string TailText;
            public string ReadError;
        }

        private SafetySnapshot BuildSafetySnapshot()
        {
            LogReading log = ReadLogGrowthSinceBaseline();
            if (!string.IsNullOrEmpty(log.ReadError))
            {
                return SafetySnapshot.Warning(
                    "Could not inspect Unity log. / Unity 로그를 확인하지 못했습니다.",
                    log.ReadError,
                    log.Path,
                    log.SizeBytes);
            }

            return ClassifySafetySnapshot(log);
        }

        /// <summary>
        /// 마지막 Recover/Clear 이후 Unity 로그가 얼마나 자랐는지 재고, 자란 구간의 꼬리만 읽어 온다.
        /// </summary>
        /// <remarks>
        /// logSize &lt; baselineBytes는 손상이 아니라 로그 회전이다. Unity는 에디터를 다시 켜면 로그를 처음부터
        /// 쓰므로 파일이 기준선보다 작아질 수 있다. 그대로 빼면 음수가 나오고 0으로 자르면 "안 자랐다"가 되어
        /// 폭주를 통째로 놓친다. 그래서 기준선을 0으로 되잡아 새 파일 전체를 새로 자란 양으로 센다.
        ///
        /// 꼬리는 RecentLogTailBytes(64KB)까지만 읽는다. 폭주 중인 로그를 통째로 읽는 것 자체가 멈춤이 된다.
        /// 그래서 진짜 폭주는 텍스트가 아니라 양으로 잡고, 100MB 관문이 키워드 검사보다 먼저 온다.
        /// </remarks>
        private LogReading ReadLogGrowthSinceBaseline()
        {
            LogReading reading = new LogReading();
            reading.Path = GetCurrentLogPath();
            reading.TailText = string.Empty;
            reading.ReadError = string.Empty;

            if (string.IsNullOrEmpty(reading.Path) || !File.Exists(reading.Path))
            {
                return reading;
            }

            try
            {
                FileInfo logFile = new FileInfo(reading.Path);
                reading.SizeBytes = logFile.Length;
                long baselineBytes = Math.Max(0L, safetyLogBaselineBytes);
                if (reading.SizeBytes < baselineBytes)
                {
                    baselineBytes = 0L;
                    safetyLogBaselineBytes = 0L;
                }

                reading.NewBytes = Math.Max(0L, reading.SizeBytes - baselineBytes);
                if (reading.NewBytes > 0L)
                {
                    reading.TailText = ReadLogTailSince(reading.Path, baselineBytes, RecentLogTailBytes);
                }
            }
            catch (Exception exception)
            {
                reading.ReadError = exception.Message;
            }

            return reading;
        }

        /// <summary>
        /// 읽어 온 로그 한 덩어리를 SafetySnapshot 하나로 분류한다. 검사 순서가 곧 규칙이다.
        /// </summary>
        /// <remarks>
        /// 1. 로그 증가량(LogGrowthBlockBytes, 100MB)이 맨 앞이다. 폭주하는 로그는 내용을 읽기 전에 이미
        ///    위험하고, 꼬리 64KB만 보는 키워드 검사로는 이미 수백 MB 쏟아진 상황을 못 본다. 양의 문제라
        ///    텍스트로는 못 잡는다.
        /// 2. 그다음이 폭주 키워드(FindCriticalLoopKeyword). 걸리면 HardBlock이다.
        /// 3. isCompiling / isUpdating은 키워드 검사 뒤다. 순서를 뒤집으면, 컴파일이 도는 몇 초 동안 깨진
        ///    ZEPETO 컨텍스트가 "지금 컴파일 중" 이라는 순한 이유에 가려진다. 컴파일은 곧 끝나고 사용자는
        ///    다시 Play를 누르지만 컨텍스트는 그대로 깨져 있다. 진짜 원인이 임시 상태에 덮이면 안 된다.
        /// 4. sessionErrorCount는 맨 뒤고 Warning까지만 올린다. 아는 폭주 패턴이 하나도 없는 오류는 이 도구와
        ///    무관한 오류일 수 있고, 그걸로 Play를 막으면 사용자가 풀 수 없는 차단이 된다.
        /// </remarks>
        private static SafetySnapshot ClassifySafetySnapshot(LogReading log)
        {
            if (log.NewBytes >= LogGrowthBlockBytes)
            {
                return SafetySnapshot.Blocked(
                    "Log grew over 100MB after the last Recover/Clear. Stop before refreshing or playing. / 마지막 복구 이후 로그가 100MB 넘게 증가했습니다.",
                    "New log growth guard blocked risky actions. New bytes since last Recover/Clear: " + FormatBytes(log.NewBytes) + ".",
                    log.Path,
                    log.SizeBytes);
            }

            // 마지막 콘솔 메시지를 로그 꼬리 앞에 붙이는 이유: 예외가 방금 났는데 아직 파일로 flush되지 않은
            // 순간이 있고, 그때는 이쪽에만 증거가 있다.
            string safetyText = lastConsoleMessage + "\n" + log.TailText;
            string riskKeyword = FindCriticalLoopKeyword(safetyText);
            if (!string.IsNullOrEmpty(riskKeyword))
            {
                // [QC][Invariant:actionable_block]
                // 콘솔을 지운다고 깨진 ZEPETO 컨텍스트가 낫지는 않으므로, 메시지는 진짜 해법을 이름으로
                // 말해야 한다: Play에서 나가고, Play 도중 재컴파일을 끄고, 그다음 다시 Play.
                return SafetySnapshot.Blocked(
                    "ZEPETO SDK 내부 상태가 깨졌습니다. 아바타가 움직이지 않습니다. / ZEPETO SDK context is broken.",
                    "감지된 패턴: " + riskKeyword + ". 대부분 Play 도중에 스크립트가 다시 컴파일되어 생깁니다. "
                        + "Stop을 눌러 Play를 끝내고, 위의 'Play 중 재컴파일 끄기'를 적용한 뒤 다시 Play하세요. "
                        + "Play를 끝내지 않고 Console만 지우면 복구되지 않습니다.",
                    log.Path,
                    log.SizeBytes);
            }

            if (ContainsPackageCacheImmutableWarning(safetyText))
            {
                return SafetySnapshot.Warning(
                    "Package cache immutable asset warning detected. / package cache asset 변경 경고가 있습니다.",
                    "Use Local Controller Fix, then restart Unity or let Library/PackageCache rebuild if the warning was already emitted.",
                    log.Path,
                    log.SizeBytes);
            }

            if (ContainsReloadingAssembliesFailed(safetyText))
            {
                return SafetySnapshot.Warning(
                    "Unity assembly reload failed previously. / 이전 assembly reload 실패가 감지되었습니다.",
                    "Run Validate, fix compile errors if any, then use Recover before Play.",
                    log.Path,
                    log.SizeBytes);
            }

            bool hasKnownSdkCleanup = ContainsKnownSdkCleanupException(safetyText);

            if (EditorApplication.isCompiling)
            {
                return SafetySnapshot.Blocked(
                    "Unity is compiling. Wait before Play or Refresh. / Unity가 컴파일 중입니다.",
                    string.Empty,
                    log.Path,
                    log.SizeBytes);
            }

            if (EditorApplication.isUpdating)
            {
                return SafetySnapshot.Blocked(
                    "Unity is importing/updating assets. Wait before Play or Refresh. / Unity가 에셋을 갱신 중입니다.",
                    string.Empty,
                    log.Path,
                    log.SizeBytes);
            }

            if (sessionErrorCount > 0)
            {
                return SafetySnapshot.Warning(
                    "Helper session has errors, but no known loop pattern was found. / 헬퍼 세션 오류가 있지만 폭주 패턴은 없습니다.",
                    lastConsoleMessage,
                    log.Path,
                    log.SizeBytes);
            }

            return SafetySnapshot.Ok(
                "Safe to work. No known SDK/helper loop pattern is active. / 작업해도 되는 상태입니다.",
                hasKnownSdkCleanup
                    ? "ZEPETO SDK cleanup warning was ignored because it is a non-repeating Play/Stop cleanup message."
                    : (sessionWarningCount > 0 ? lastConsoleMessage : string.Empty),
                log.Path,
                log.SizeBytes);
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

        // FileShare.ReadWrite로 여는 것이 핵심이다. Unity가 지금도 쓰고 있는 파일이라 배타적으로 열면 못 읽는다.
        //
        // readStart를 startBytes가 아니라 max(startBytes, length - readLength)로 잡는 이유: 기준선 이후 자란
        // 양이 maxBytes보다 크면 앞이 아니라 끝을 읽어야 한다. 폭주의 증거는 늘 마지막에 있다.
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

        /// <summary>
        /// 두 조각이 함께 있어야 폭주로 본다: NullReferenceException 또는 Assertion failed 가 있고,
        /// 그 위에 CriticalLoopKeywords 중 하나가 있어야 한다.
        /// </summary>
        /// <remarks>
        /// 키워드만으로는 안 된다. "SwingBoneProcessor" 같은 이름은 멀쩡한 로그와 스택 트레이스에도 그냥
        /// 등장하므로, 그것만 보고 막으면 정상 프로젝트에서 Play 버튼이 죽는다. 예외만으로도 안 된다.
        /// 이 도구와 무관한 NullReferenceException은 계속 나고, 그걸로 막으면 사용자가 풀 방법이 없다.
        /// 둘이 겹칠 때만 "ZEPETO 컨텍스트가 깨졌다"고 말할 수 있다.
        /// </remarks>
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

        // Play/Stop 한 번마다 한 번씩 나고 반복되지 않는 SDK 정리 예외. 위 FindCriticalLoopKeyword가 잡는
        // 폭주와 글자만 보면 똑같이 NullReferenceException이라, 이 목록으로 따로 걸러내지 않으면 정상적으로
        // Play/Stop 한 번 한 사용자에게 차단 경고가 뜬다.
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

        // 두 단어가 다 있어야 한다. "immutable asset"만 보면 다른 패키지 경고에도 걸리고, "PackageCache"만
        // 보면 경로가 찍힌 평범한 로그 줄에 전부 걸린다.
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

        /// <summary>
        /// Application.logMessageReceived 콜백. 이 창의 그리기와 무관한 시점에 불리면서 static 카운터를 바꾼다.
        /// </summary>
        /// <remarks>
        /// 아는 SDK 정리 예외는 lastConsoleMessage만 갱신하고 오류 수는 올리지 않은 채 바로 빠져나간다.
        /// 세어 버리면 Play/Stop을 정상적으로 한 번 한 것만으로 경고 패널이 켜지고, 그 패널은 진짜 문제일
        /// 때만 의미가 있다.
        ///
        /// 이 메서드가 sessionErrorCount를 프레임 도중에 바꾼다는 사실이 무조건 그리기 규칙의 근거 절반이다.
        /// 이 값으로 컨트롤의 존재를 정하면 한 OnGUI의 Layout 패스와 Repaint 패스 사이에서 개수가 달라진다
        /// (Steps.cs DrawWarningCleanupPanel).
        /// </remarks>
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

        // 한 번 잰 안전 상태를 그대로 굳혀 들고 다니는 값 타입. UI 여러 곳이 같은 프레임 안에서 같은 스냅샷을
        // 돌려 보는데, 그때마다 다시 재면 한 프레임 안에서 답이 갈릴 수 있다.
        private struct SafetySnapshot
        {
            public readonly SafetyLevel Level;
            public readonly string Message;
            public readonly string Detail;
            public readonly string LogPath;
            public readonly long LogSizeBytes;

            private SafetySnapshot(SafetyLevel level, string message, string detail, string logPath, long logSizeBytes)
            {
                Level = level;
                Message = message;
                Detail = detail;
                LogPath = logPath;
                LogSizeBytes = logSizeBytes;
            }

            public bool HasBlockingRisk
            {
                get { return Level == SafetyLevel.HardBlock; }
            }

            // Ok가 아닌 모든 것. HasBlockingRisk를 함의하므로, 둘을 함께 쓸 때는 반드시 막힘을 먼저 물어야 한다.
            public bool HasWarning
            {
                get { return Level != SafetyLevel.Ok; }
            }

            /// <summary>
            /// 아직 한 번도 재지 않은 상태. Ok가 아니라 Recoverable이라서 첫 검사 전부터 HasWarning은 true다.
            /// 의도한 것이다 - 모르는 상태를 안전하다고 말하는 쪽이 훨씬 위험하다.
            /// </summary>
            public static SafetySnapshot Unknown(string message)
            {
                return new SafetySnapshot(SafetyLevel.Recoverable, message, string.Empty, string.Empty, 0L);
            }

            public static SafetySnapshot Ok(string message, string detail, string logPath, long logSizeBytes)
            {
                return new SafetySnapshot(SafetyLevel.Ok, message, detail, logPath, logSizeBytes);
            }

            public static SafetySnapshot Warning(string message, string detail, string logPath, long logSizeBytes)
            {
                return new SafetySnapshot(SafetyLevel.Recoverable, message, detail, logPath, logSizeBytes);
            }

            public static SafetySnapshot Blocked(string message, string detail, string logPath, long logSizeBytes)
            {
                return new SafetySnapshot(SafetyLevel.HardBlock, message, detail, logPath, logSizeBytes);
            }
        }
    }
}
