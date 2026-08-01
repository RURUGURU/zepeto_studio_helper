using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// Play가 도는 동안 Blender 출력 폴더를 감시하다가, Play를 벗어나지 않고 동작만 라이브 아바타에 갈아 끼운다.
    ///
    /// 이 정도 장치를 들일 값어치가 있는 이유: 사용자의 실제 얼굴·체형·의상이 붙은 아바타는 런타임에 내려받기
    /// 때문에 Play 중에만 존재한다. 그 아바타로 동작을 확인하려면 수정 한 번마다 아홉 단계를 밟아야 했다
    /// (Blender 내보내기, Unity로 전환, fbx 클릭, 설정, 추출, 드롭다운에서 선택, 적용, Play, Stop).
    /// 두 번째 반복부터는 Blender에서 버튼 한 번 + Unity 창을 다시 앞으로 가져오기가 전부다.
    ///
    /// 재바인딩을 피하는 요령: 고정된 클립 에셋 하나(LivePreviewClipPath)를 Play 전에 모든 override 슬롯에
    /// 미리 연결해 둔다. Play 중에는 그 에셋의 내용만 EditorUtility.CopySerialized로 덮어쓰므로 Animator는
    /// 계속 같은 오브젝트를 가리킨 채 새 동작을 그 자리에서 집어간다. Play 도중에 controller를 재바인딩하는
    /// 것이야말로 ZEPETO context를 초기화시키는 원인이다.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        /// <summary>
        /// Blender 애드온이 실제로 파일을 쓰는 곳. 일부러 CustomMotionRoot가 아니다.
        ///
        /// 애드온 쪽에서 이 경로를 만드는 것은 하드코딩 상수가 아니라 refresh_paths다. resolve_unity_project()가
        /// 찾은 프로젝트 폴더에 os.path.join(project, "Assets", "CustomMotions")를 붙여
        /// scene.zepeto_export_dir에 써 넣고, 사용자가 패널에서 직접 고른 값은 덮어쓰지 않는다. 예전에 이
        /// 경로를 들고 있던 상수 DEFAULT_EXPORT_DIR은 하드코딩 경로 UNITY_PROJECT와 함께 이미 삭제됐으므로,
        /// 애드온에서 그 이름으로 찾지 말 것.
        ///
        /// CustomMotionRoot("Assets/ZepetoHelper/Motions")는 추출한 .anim 사본이 사는 완전히 다른 폴더다.
        /// 이름이 비슷해서 "정리"하고 싶어지지만 하면 안 된다. 잘못된 쪽을 감시하면 이 기능 전체가 아무 소리
        /// 없이 한 번도 동작하지 않는다 — 이 상수가 생긴 이유가 정확히 그 버그다.
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

        // Unity가 도메인을 내리는 동안에만 true. OnDisable은 도메인 리로드에도, 창이 정말 사라질 때에도 똑같이
        // 불리는데 라이브 프리뷰는 두 번째 경우에만 빌린 것을 돌려줘야 한다 — Play 진입이 곧 도메인 리로드이고,
        // SessionState 예약은 그 리로드를 빌림이 살아남으라고 존재한다. 일부러 static이다: 리로드가 이 값을
        // 날려 버리므로 새 도메인은 다시 false에서 시작한다.
        private static bool isAssemblyReloadInProgress;

        internal static string LiveClipAssetPath
        {
            get { return LivePreviewClipPath; }
        }

        [InitializeOnLoadMethod]
        private static void HookAssemblyReloadForLivePreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += MarkAssemblyReloadInProgress;
        }

        private static void MarkAssemblyReloadInProgress()
        {
            isAssemblyReloadInProgress = true;
        }

        // Play를 넘나드는 상태는 전부 SessionState에 둔다. Play 진입은 도메인 리로드라서 EditorWindow의
        // 직렬화되지 않은 인스턴스 필드를 모두 초기화한다 — 평범한 bool이었다면 감시가 시작돼야 하는 바로 그
        // 순간에 false로 돌아온다. activePreviewStage도 같은 이유로 이미 SessionState를 쓴다.
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

        // 문자열로 저장한다: SessionState에는 long 오버로드가 없고, 파일 크기를 int로 잘라 담으면 2 GB를 넘는
        // 순간 서로 다른 두 파일이 같은 크기로 비교된다.
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

        /// <summary>
        /// 감시 삼종(파일 / 크기 / 스탬프)과 화면에 떠 있는 메시지, 그리고 정착 대기값을 비운다.
        ///
        /// 무장할 때(PrepareLivePreview)와 반납할 때(RestoreLivePreviewState) 똑같은 값을 비워야 해서 한곳에
        /// 모았다. SessionState에 남겨 두면 이 값들은 자기가 설명하던 감시보다 오래 살아남는다: Stop 뒤에도
        /// 패널이 더 이상 감시하지 않는 파일에 대한 옛 경고를 계속 보여 줬다. LiveWatchedSize는 문자열로
        /// 저장되며(SessionState에 long 오버로드가 없다) -1이 "감시 중 아님"을 뜻하는데, 키가 없을 때 게터가
        /// 돌려주는 값도 정확히 그 -1이다.
        ///
        /// LiveReloadCount는 여기 없다. 그 값의 기준점은 무장 시점 하나뿐이라 PrepareLivePreview에만 남긴다.
        /// </summary>
        private void ResetLiveWatchState()
        {
            LiveWatchedFile = string.Empty;
            LiveWatchedSize = -1L;
            LiveWatchedStamp = string.Empty;
            LiveMessage = string.Empty;
            livePendingSize = -1L;
        }

        private void SubscribeLiveReload()
        {
            EditorApplication.update += PumpLiveReload;

            // OnEnable은 리로드 중이 아닌 도메인에서만 돌 수 있으므로 플래그를 내리기에 자연스러운 자리다.
            // 리로드가 끝나면 새 도메인은 어차피 false로 시작하지만, 리로드가 예고만 되고 실제로 일어나지 않은
            // 경우에도 여기서 풀어 준다 — 그러지 않으면 남은 세션 내내 teardown 복구가 막힌다.
            isAssemblyReloadInProgress = false;

            // 라이브 창이 곁에 없어서 아무도 처리하지 못한 모든 종료 경로를 여기서 뒤늦게 수습한다. OnDestroy는
            // Play가 도는 동안에는 일부러 복구하지 않는다 — OnDestroy는 창 LAYOUT 리로드에도 불리기 때문이다.
            // ReturnLivePreviewBorrowOnTeardown 참고. 그래서 Play 도중 헬퍼를 닫은 경우, Play 중에 에디터를
            // 종료한 경우, LOADER가 아직 바인딩되지 않아 복구가 포기한 경우가 전부 이 지점으로 모인다. Play
            // 중이 아니라면 남아 있는 라이브 프리뷰 상태는 정의상 이미 낡은 값이다.
            //
            // isPlaying이 아니라 isPlayingOrWillChangePlaymode다: OnEnable은 Play로 진입하는 도메인 리로드
            // 중에도 돌고, 그 시점에 isPlaying은 아직 믿을 만하게 true가 아니다. isPlaying을 쓰면 감시가
            // 시작돼야 하는 바로 그 순간에 감시를 해제해 버려서 기능이 조용히 아무것도 하지 않게 된다 —
            // 이 코드가 대체한 SessionState 버그와 같은 부류다.
            if (!EditorApplication.isPlayingOrWillChangePlaymode
                && (LiveReloadArmed || SessionState.GetBool(LiveRestoreActiveSessionKey, false)))
            {
                // 지연 실행: OnEnable은 RefreshAll보다 SubscribeLiveReload를 먼저 부르므로 이 시점에는
                // LOADER의 직렬화 필드가 아직 바인딩되지 않았고, ApplyClipToOverrideController가 조용히
                // 실패한다.
                //
                // 예약과 콜백 사이에 창이 닫힐 수 있고, 그러면 콜백은 이미 파괴된 EditorWindow를 대상으로
                // 돈다. 파괴된 EditorWindow는 == null로 비교되며, 그때는 teardown 경로가 이미 빌림을 돌려준
                // 뒤이므로 호출을 그냥 버리는 것이 맞다.
                EditorApplication.delayCall += () =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    RestoreLivePreviewState();
                };
            }
        }

        private void UnsubscribeLiveReload()
        {
            EditorApplication.update -= PumpLiveReload;

            // 라이브 프리뷰 빌림을 여기서 일부러 돌려주지 않는다. OnDisable이 창이 리스닝을 멈추는 자리이긴
            // 하지만, 정반대로 다뤄야 할 두 가지 상황에 똑같이 불린다: 창이 정말 사라지는 경우(복구해야 함)와
            // Play 진입이 곧 도메인 리로드인 경우(복구하면 안 됨 — SessionState 예약은 그 리로드를 빌림이
            // 살아남으라고 있는 것이다). OnDisable 안에서는 둘을 구분할 수 없다: 양쪽 모두
            // isPlayingOrWillChangePlaymode가 true이고, play-mode 리로드에서는 beforeAssemblyReload가 이미
            // 발생했다는 보장이 없다. 한 틱 이르게 복구하면 감시가 시작돼야 하는 바로 그 순간에 해제된다.
            // 그래서 빌림은 OnDestroy에서 돌려준다. Unity는 리로드에는 OnDestroy를 부르지 않는다 — 그리고
            // 거기서도 Play가 멈춰 있을 때만 돌려준다. OnDestroy 역시 닫힌 창과 리로드된 창 LAYOUT을
            // 구분하지 못하기 때문이다.
        }

        /// <summary>
        /// 이 창의 유일한 OnDestroy. Unity는 도메인 리로드에는 이것을 부르지 않지만, 사용자가 정말 창을 닫은
        /// 경우와 똑같이 창 LAYOUT 리로드에도 부른다. 그래서 여기 있는 어떤 코드도 이 호출만 보고 "사용자가
        /// 헬퍼를 닫았다"고 판단해서는 안 된다 — ReturnLivePreviewBorrowOnTeardown 참고.
        /// </summary>
        private void OnDestroy()
        {
            ReturnLivePreviewBorrowOnTeardown();
            UnsubscribeActiveSceneWatch();
        }

        /// <summary>
        /// PrepareLivePreview가 빌려 간 것을 전부 돌려준다. 단, 창 자체가 사라지는 중이면서 PLAY가 멈춰 있는
        /// 경우에만. 이 메서드가 직접 처리하는 것은 그 한 가지뿐이고, Play 도중의 teardown은 전부 위임한다.
        ///
        /// 직접 처리: Edit 모드에서 빌림이 예약된 채로 헬퍼가 닫힌 경우 — LOADER가 아직 바인딩되지 않아 앞서
        /// 포기한 복구, 또는 EnteredEditMode를 들을 창이 하나도 없이 끝난 라이브 세션. 이게 없으면
        /// runInBackground가 켜진 채 ProjectSettings에 엉뚱한 diff로 남고, 사용자의 원래 동작은
        /// LiveFromBlender.anim으로 바뀐 채 남는데 그것을 되돌릴 UI도 남아 있지 않다.
        ///
        /// 위임: Play가 도는 동안 벌어지는 일은 전부. 그때의 OnDestroy는 "사용자가 창을 닫았다"는 뜻이 아니기
        /// 때문이다. Unity는 창 LAYOUT이 리로드되면 모든 EditorWindow를 파괴하고, 레이아웃 리로드는 Play
        /// 도중에도 공짜로 일어난다: Game 뷰의 기본 기능인 "Maximize On Play"가 레이아웃을 저장하고 다른
        /// 레이아웃을 불러오며, 수동으로 Window &gt; Layouts를 바꿔도 마찬가지다. 아래 두 가드 중 어느 것도
        /// 이것을 잡지 못한다 — 레이아웃 리로드는 어셈블리 리로드가 아니고, Unity는 새 창을 만들기 전에 옛
        /// 창을 먼저 닫으므로 HasAnotherLiveHelperWindow도 false다. 거기서 복구를 돌렸더니 라이브 프리뷰가
        /// 무장되는 바로 그 순간에 해제됐다(LiveReloadArmed가 지워지고, runInBackground가 다시 꺼지고,
        /// 사용자의 클립이 라이브 클립 위로 되써졌다). 게다가 다시 만들어진 창의 OnEnable은 Play 중에는 일부러
        /// 재무장하지 않으므로, Play는 계속 도는데 기능만 조용히 죽었다. 클립 쪽은 무장을 잃는 것보다 더
        /// 나쁘다: Play 도중이라면 그것은 라이브 아바타가 지금 쓰고 있는 바로 그 controller에 ApplyOverrides +
        /// SaveAssets + ImportAsset을 거는 일이고, 이 파일의 클래스 주석이 피하려고 존재하는 그 재바인딩이라서
        /// ZEPETO context를 깨뜨린다.
        ///
        /// 대신 넘겨받는 두 경로는 이미 둘 다 올바르고, 예약이 이 인스턴스가 아니라 SessionState에 있으므로
        /// 레이아웃 리로드에서도 살아남는다:
        ///   - OnPlayModeStateChanged/EnteredEditMode (Safety.cs)가 Stop 시점에, 그때 열려 있는 아무 창을
        ///     통해서 복구한다.
        ///   - SubscribeLiveReload의 지연된 OnEnable 수습이 다음에 창이 열릴 때, Play 중이 아니고
        ///     LiveReloadArmed / LiveRestoreActive가 아직 예약돼 있으면 복구한다.
        /// </summary>
        private void ReturnLivePreviewBorrowOnTeardown()
        {
            // Play가 돌고 있거나 시작하는 중: 이 OnDestroy는 헬퍼가 정말 닫혔는지에 대해 아무 정보도 담고 있지
            // 않으므로 아무 일도 하면 안 된다. 어떤 복구 작업보다도 먼저 여기서 돌아가는 것이
            // RestoreLivePreviewState 안의 Play 중 controller 재바인딩을 손이 닿지 않는 곳에 두는 방법이기도
            // 하다. SessionState 예약은 일부러 그대로 남긴다 — 위임받은 두 경로가 읽는 값이 그것이다.
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            // 이중 안전장치: 도메인 리로드가 복구에 도달하는 일은 절대 없어야 한다. 리로드에는 OnDestroy가
            // 불리지 않으므로 이 가드는 그 전제가 바뀌었을 때만 의미가 있다.
            if (isAssemblyReloadInProgress)
            {
                return;
            }

            // 빌림은 아직 열려 있는 창의 것이지 이 창의 것이 아니다. 테스트 러너는 ZepetoStudioHelperWindow를
            // CreateInstance로 만들었다가 DestroyImmediate 하는데, 이때 OnDestroy는 실제로 발생한다 — 진짜
            // 창이 예약을 들고 있는 동안에 말이다. 거기서 빌림을 돌려주면 그 창 밑에서 예약이 사라진다. Play
            // 중이면 위 검사에서 이미 돌아갔으므로 여기는 정지 상태 전용이다.
            if (HasAnotherLiveHelperWindow())
            {
                return;
            }

            bool hasClipBorrow = SessionState.GetBool(LiveRestoreActiveSessionKey, false);
            bool hasRunInBackgroundBorrow = !SessionState.GetBool(LiveRunInBackgroundSessionKey, true);
            if (!LiveReloadArmed && !hasClipBorrow && !hasRunInBackgroundBorrow)
            {
                return;
            }

            // 일부러 동기 호출이다. delayCall로 미루면 콜백이 이 창이 사라진 뒤에 돌고,
            // RestoreLivePreviewState는 재생 슬롯에 닿기 위해 이 인스턴스에 바인딩된 LOADER 필드가 필요하다.
            // 여기서 동기로 해도 안전한 이유는 정확히 Play가 멈춰 있기 때문이다: 이때의 controller 쓰기는 돌고
            // 있는 무언가를 재바인딩하지 않는다.
            RestoreLivePreviewState();
        }

        /// <summary>
        /// 다른 헬퍼 창 인스턴스가 아직 살아 있는지. 파괴된 EditorWindow도 관리 래퍼는 목록에 그대로 남은 채
        /// == null로 비교되며, null 검사가 걸러 내는 것이 바로 그것이다.
        /// </summary>
        private bool HasAnotherLiveHelperWindow()
        {
            ZepetoStudioHelperWindow[] windows = Resources.FindObjectsOfTypeAll<ZepetoStudioHelperWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null && windows[i] != this)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// FileSystemWatcher가 아니라 폴링인 이유: 워처는 수 MB짜리 fbx의 첫 write에 이미 발생하므로, 아직
        /// 덜 쓰인 부분 파일을 리임포트하게 된다.
        ///
        /// 폴링이 그 문제를 대신 푸는 방식이 이 파일에서 가장 미묘한 부분이라 정착(settle) 프로토콜을 여기
        /// 적어 둔다:
        ///   1. LivePollIntervalSeconds(0.4초)가 지나지 않았으면 아무것도 하지 않는다.
        ///   2. 감시 폴더에서 가장 최근 fbx를 찾고, 경로 + 크기 + 타임스탬프 세 값으로 이미 처리한 파일인지
        ///      판정한다. 셋이 모두 같으면 변화가 없는 것이므로 정착 대기값을 비우고 끝낸다.
        ///   3. 변화를 처음 본 폴링은 크기만 livePendingSize에 적어 두고 그대로 돌아간다. 같은 크기가 한 번
        ///      더 관측돼야(= 다음 틱) 쓰기가 끝났다고 본다. 이 한 틱이 부분 파일을 걸러 내는 전부다.
        ///   4. 그때서야 감시 삼종을 갱신하고 커밋한다 — TryPushMotionToLiveAvatar.
        /// </summary>
        private void PumpLiveReload()
        {
            if (!LiveReloadArmed || !EditorApplication.isPlaying)
            {
                return;
            }

            // 리임포트가 에디터 루프를 돌리므로 이 메서드는 재진입할 수 있다. 가드가 없으면 같은 파일에 대한
            // 임포트 두 개가 엇갈리고, 두 번째 CopySerialized가 반쯤 임포트된 클립을 읽는다.
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
                // 0.4초마다가 아니라 이유가 바뀔 때 한 번만 보고한다. 아니면 패널이 끝없이 다시 그려진다.
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

            // 변화를 처음 본 순간(위 프로토콜 3단계): 크기만 기억하고 한 틱 기다려 값이 정착하는지 본다.
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
                // 여기서 나는 예외는 그대로 두면 0.4초마다 반복되어 콘솔을 파묻는다.
                LiveMessage = "적용 중 오류: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                liveReloadInFlight = false;
            }

            Repaint();
        }

        /// <summary>
        /// 감시 폴더에서 가장 최근에 쓰인 fbx 하나를 고른다. 규칙은 "가장 새것이 이긴다" 하나뿐이다 — 파일
        /// 이름은 보지 않으므로 사용자가 Blender에서 어떤 이름으로 내보내든 마지막으로 내보낸 것이 대상이 된다.
        ///
        /// 실패는 예외가 아니라 blockReason 문자열로 돌려준다. 이 메서드는 0.4초마다 불리므로 예외를 던지면
        /// 콘솔이 파묻힌다. FileInfo.Length에서 IOException이 나면 그것은 실패가 아니라 "파일이 아직 쓰이는
        /// 중"이라는 뜻이고, 다음 폴링이 그대로 다시 시도한다.
        /// </summary>
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
                // Blender 애드온(zepeto_motion_helper.py)은 "<name>.fbx.part"로 쓴 다음 이름을 바꾼다.
                // Windows는 8.3 짧은 이름 때문에 그 파일을 "*.fbx" 글롭으로도 매칭할 수 있고, 완성되지 않은
                // fbx를 임포트하면 깨진 클립이 구워진다. 애드온과의 양방향 계약이므로 지우면 안 된다 —
                // ConfigureMotionFolderForLivePreview에도 같은 건너뛰기가 있다.
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
        /// 바뀐 fbx를 리임포트하고, 그 안의 클립 내용을 라이브 클립 에셋 위에 제자리로 복사한다.
        ///
        /// 순서가 정해진 여섯 가지 일을 하며 각각 이유가 다르다:
        ///   1. ImportAsset(ForceUpdate) — 파일이 바뀌었다는 것을 AssetDatabase에 알린다.
        ///   2. RefreshClipRangeIfStale — .meta에 고정된 take 범위가 파일과 어긋났으면 다시 뽑는다.
        ///   3. CopySerialized — 에셋을 새로 바인딩하지 않고 내용만 덮어쓴다. 이 기능의 핵심이 이 한 줄이다.
        ///   4. m_Name 복원 — CopySerialized가 이름까지 덮어쓰기 때문이다.
        ///   5. hideFlags를 None으로 — fbx 서브에셋의 플래그가 따라오면 .anim까지 숨는다.
        ///   6. ApplyClipLoopSetting 재적용 — 루프 상태 역시 CopySerialized로 따라 넘어온다.
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

            // CopySerialized는 m_Name까지 덮어쓴다. 그대로 두면 에셋 이름이 fbx 안의 클립 이름으로 바뀌어
            // .anim 파일 이름과 그 안의 오브젝트 이름이 어긋난다. 되돌려 놓는다.
            string keepName = live.name;
            EditorUtility.CopySerialized(source, live);
            live.name = keepName;

            // fbx 서브에셋은 모델 안에 숨겨져 있다. 그 플래그까지 복사돼 오면 .anim 자체가 숨어 버린다.
            live.hideFlags = HideFlags.None;

            // Blender는 2초짜리 사이클로 만든다. CopySerialized가 fbx 클립 자신의 루프 상태를 함께 가져오므로
            // 최초 설정 때 한 번이 아니라 리로드 때마다 다시 걸어야 한다 — 그러지 않으면 동작이 한 번 재생되고
            // 멈추고, 사용자에게는 "도구가 고장 났다"로 보인다.
            ApplyClipLoopSetting(live, true);
            EditorUtility.SetDirty(live);

            message = Path.GetFileName(assetPath) + " → 적용했습니다 ("
                + live.length.ToString("0.00") + "초, " + (LiveReloadCount + 1) + "번째)"
                + (string.IsNullOrEmpty(rangeNote) ? string.Empty : " / " + rangeNote);
            return true;
        }

        /// <summary>
        /// fbx 자신의 take가 .meta에 고정된 값과 더 이상 맞지 않을 때 클립 목록을 다시 뽑는다.
        ///
        /// TryConfigureMotionFbx는 importer.clipAnimations를 직접 써야만 한다. Root Transform 잠금 플래그가
        /// ModelImporterClipAnimation에만 있고 ModelImporter의 다른 어디에도 없기 때문이다. 그 부작용으로
        /// importer가 파일의 take를 더 이상 따라가지 않게 된다: Blender에서 동작 길이를 48프레임에서
        /// 96프레임으로 바꿔도 리임포트는 여전히 고정된 48을 만들어 내고 나머지는 조용히 버려진다.
        /// 에셋을 리임포트하는 것은 도메인을 리로드하지 않으므로 Play 중에 해도 안전하다 — 느릴 뿐이다.
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
        /// 아직 Edit 모드일 때 끝내 두어야 하는 것 전부. Play 중에는 importer 설정도 controller 바인딩도
        /// 건드리지 않는다는 것이 이 기능의 전제이므로 순서까지 의미가 있다:
        ///   1. Play 중이면 거절한다 — 아래 작업이 전부 Edit 모드 전용이다.
        ///   2. controller가 아직 package 원본이면 project-local 사본을 먼저 만든다.
        ///   3. 필요한 폴더를 만든다.
        ///   4. 지금 재생 슬롯에 있는 클립을 기억한다. 반드시 6번의 바인딩 전에 해야 한다.
        ///   5. 감시 폴더의 fbx를 전부 미리 Humanoid로 설정한다 — Play 중 리임포트가 importer 설정을 쓰는
        ///      일이 없도록.
        ///   6. 라이브 클립 에셋을 만들고(없으면) 루프를 걸어 모든 override 슬롯에 바인딩한다.
        ///   7. runInBackground를 켜고 원래 값을 SessionState에 적어 둔 뒤, 감시 상태를 초기화한다.
        /// </summary>
        private bool PrepareLivePreview(out string message)
        {
            message = string.Empty;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play를 멈춘 상태에서 준비해야 합니다.";
                return false;
            }

            // RequestPlayMode가 강제하는 것과 같은 전제 조건: SDK의 package 사본에 쓰면 그것이 망가지므로
            // project-local controller가 먼저 있어야 한다. 이게 없으면 새 프로젝트에서 버튼이
            // "AnimatorController가 아직 package 원본입니다"로 막다른 길에 부딪힌다.
            if (IsPackageOrPackageCachePath(GetAnimatorControllerPath()))
            {
                string controllerMessage;
                if (!EnsureLocalAnimatorController(out controllerMessage))
                {
                    message = "local AnimatorController를 만들지 못했습니다: " + controllerMessage;
                    return false;
                }
            }

            // 두 폴더는 서로 다른 주인을 가진 서로 다른 뿌리다. CustomMotionRoot는 라이브 클립 에셋이 사는
            // 곳이고, LiveWatchRoot는 Blender 애드온이 쓰는 곳이다. 합치면 안 된다.
            EnsureFolder("Assets", "ZepetoHelper");
            EnsureFolder("Assets/ZepetoHelper", "Motions");
            EnsureFolder("Assets", "CustomMotions");

            List<string> notes = new List<string>();

            // 바인딩보다 먼저 지금 재생 중인 클립을 붙잡아 둔다. Stop이 되돌릴 수 있도록. 가드가 둘이다:
            // 라이브 클립 자신은 절대 붙잡지 않고(그러면 사용자의 진짜 클립으로 돌아갈 포인터가 사라진다),
            // 아직 되돌리지 않은 기존 기록도 절대 덮어쓰지 않는다.
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

            // 에디터가 활성 애플리케이션이 아니면 Play가 멈추는데, 이 기능은 사용자가 Blender에 가 있는 것을
            // 전제로 한다. ProjectSettings는 runInBackground: 0으로 배포되므로 켜 줘야 하고 — Stop 때 다시
            // 되돌려서 프로젝트에 엉뚱한 diff가 남지 않게 해야 한다.
            if (!PlayerSettings.runInBackground)
            {
                SessionState.SetBool(LiveRunInBackgroundSessionKey, false);
                PlayerSettings.runInBackground = true;
                notes.Add("Run In Background 켬");
            }

            AssetDatabase.SaveAssets();

            ResetLiveWatchState();

            // 적용 횟수는 무장 시점에만 0으로 되돌린다. 이 값이 세는 것은 "이번 라이브 세션에서 몇 번
            // 반영됐는가"이고, 그 기준점은 여기 하나뿐이라 ResetLiveWatchState와 공유하지 않는다.
            LiveReloadCount = 0;

            notes.Add("재생 슬롯 연결");
            message = string.Join(", ", notes.ToArray());
            return true;
        }

        /// <summary>
        /// 라이브 프리뷰가 빌려 간 것을 되돌려 놓고, 감시가 보고하던 내용을 지운다. 호출되는 곳은
        /// OnPlayModeStateChanged/EnteredEditMode(다른 프리뷰 흐름을 위해 이미 있던 두 복구 경로 옆), 지연된
        /// OnEnable 수습, 그리고 Play가 멈춘 상태의 창 teardown
        /// (OnDestroy -> ReturnLivePreviewBorrowOnTeardown, 이쪽은 Play 도중에는 절대 이것을 부르지 않는다)이다.
        ///
        /// 따라서 모든 호출자는 Edit 모드에 있다. 우연이 아니라 강제 조건이다: 클립 복구는
        /// ApplyClipToOverrideController를 거치고 그것은 controller에 대한 ApplyOverrides + SaveAssets +
        /// ImportAsset이며, 라이브 아바타가 재생 중일 때 그렇게 하면 ZEPETO context가 깨진다. Play 중에 돌 수
        /// 있는 경로에서는 절대 부르지 말 것.
        /// </summary>
        private void RestoreLivePreviewState()
        {
            LiveReloadArmed = false;

            ResetLiveWatchState();

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
                // 되돌릴 것이 없다(클립이 지워졌거나, 경로를 애초에 붙잡지 못했다). 무한 재시도를 막기 위해
                // 예약을 지운다.
                SessionState.EraseBool(LiveRestoreActiveSessionKey);
                SessionState.EraseString(LiveRestorePathSessionKey);
                return;
            }

            string restoreMessage;
            if (!ApplyClipToOverrideController(restoreClip, out restoreMessage))
            {
                // 예약은 남긴다: 보통 원인은 LOADER가 아직 바인딩되지 않은 것이고, 다음에 창을 열면 풀린다.
                // 여기서 예약을 잃으면 사용자의 클립이 LiveFromBlender.anim으로 바뀐 채 영구히 남는다.
                return;
            }

            SessionState.EraseBool(LiveRestoreActiveSessionKey);
            SessionState.EraseString(LiveRestorePathSessionKey);
        }

        /// <summary>
        /// Blender 출력 폴더의 모든 fbx에 Animation Type을 미리 설정해 둔다. 수 MB짜리 fbx가 여러 개 든 폴더는
        /// 충분히 오래 걸려서 에디터가 멈춘 것처럼 보이므로 진행 표시줄을 띄운다.
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
                    // TryFindNewestMotionFile과 짝을 이루는 .part 건너뛰기. Blender 애드온
                    // (zepeto_motion_helper.py)이 "<name>.fbx.part"로 쓴 뒤 이름을 바꾸고, Windows의 8.3 짧은
                    // 이름 때문에 그 파일이 "*.fbx" 글롭에 걸린다. 두 열거 지점 모두에서 걸러야 계약이 선다.
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
                        (float)i / files.Length);

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
        /// 카드 5(내 캐릭터로 확인)의 본문. 유일한 호출자는 Flow.cs의 DrawStep5CheckOnMyCharacter다.
        ///
        /// 여기 있는 컨트롤은 전부 무조건, 고정된 순서로 그린다. 달라지는 것은 `enabled`, 라벨, 텍스트,
        /// MessageType뿐이다.
        ///
        /// 이 패널은 사용자가 Play를 켜고 끄는 동안 계속 바라보는 화면이고, isPlaying / LiveReloadArmed /
        /// LiveMessage는 모두 비동기로 바뀐다 — 뒤의 둘은 PumpLiveReload가 EditorApplication.update에서
        /// 바꾼다. 이 중 무엇으로든 컨트롤의 개수나 순서를 분기하면 Repaint 패스가 Layout 패스에 기록된 적
        /// 없는 레이아웃을 보게 되고, 그룹이 깨져 컨트롤이 깜빡이며 사라진다. 헤더의 Stop 버튼과 같은
        /// 규칙이다. DrawV7WorkbenchHeader 참고.
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
