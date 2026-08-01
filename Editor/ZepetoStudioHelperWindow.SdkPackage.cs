using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Easy.ZepetoHelper.Editor
{
    /// <summary>
    /// 설치된 zepeto.studio 패키지를 찾아내고 버전을 비교하는 부분.
    /// </summary>
    public sealed partial class ZepetoStudioHelperWindow
    {
        // [QC][Invariant:sdk_detection]
        // 탐지가 SDK를 어떻게 설치했는지에 의존하면 안 되므로, Unity가 패키지를 물리적으로 놓을 수 있는 두 자리를
        // 모두 조사한다. embedded 패키지는 Packages/<name>에 실제 폴더로 존재하고, registry 의존성(그리고
        // "file:" TARBALL 의존성)은 Library/PackageCache/<name>@<version>으로 풀려 나온다.
        // 이 프로젝트에서 확인한 결과: Packages/manifest.json의 ZEPETO scoped registry를 통해
        // Library/PackageCache/zepeto.studio@3.2.16에서 zepeto.studio 3.2.16이 해석된다 -
        // Packages/zepeto.studio 폴더는 없으므로 embedded 조사는 빗나가고 registry 조사가 답을 낸다.
        // 다루지 못하는 경우: "file:<folder>" 의존성. Unity는 그것이 가리키는 자리에 그대로 두고 두 위치 어느
        // 쪽으로도 복사하지 않는다. 그런 의존성은 Packages/<name>이라는 *가상* 경로로만 해석되는데 이 조사는
        // 파일시스템 조사다. 이 프로젝트에는 그 형태의 의존성이 없다.
        private static bool TryGetZepetoStudioPackage(out string version, out string source)
        {
            version = string.Empty;
            source = string.Empty;

            // embedded를 먼저 보고, 찾으면 PackageCache에 더 높은 버전이 있어도 그대로 답한다. Packages/ 아래에
            // 폴더를 둔 것 자체가 "이 사본을 쓴다"는 선언이고, Unity의 해석 순서도 embedded가 먼저이기 때문이다.
            string embeddedManifest = Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.Combine("Packages", Path.Combine(RequiredPackage, "package.json")));
            if (File.Exists(embeddedManifest))
            {
                version = ReadPackageVersion(embeddedManifest);
                if (!string.IsNullOrEmpty(version))
                {
                    source = "embedded";
                    return true;
                }
            }

            string packageCacheRoot = Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.Combine("Library", "PackageCache"));
            if (Directory.Exists(packageCacheRoot))
            {
                string[] directories = Directory.GetDirectories(packageCacheRoot, RequiredPackage + "@*");
                string bestVersion = string.Empty;
                for (int i = 0; i < directories.Length; i++)
                {
                    string folderName = Path.GetFileName(directories[i]);

                    // Windows의 와일드카드 매칭은 디렉터리의 8.3 짧은 이름에도 걸린다 - 두 곳의 fbx 열거가
                    // ".part"를 손으로 건너뛰게 만드는 것과 같은 함정이라, "<name>@*"가 긴 이름은 전혀 다른
                    // 항목을 돌려줄 수 있다. 그래서 긴 이름으로 접두사를 다시 확인하고, 버전은
                    // RequiredPackage.Length가 아니라 '@' 위치에서 잘라낸다. 길이로 자르면 그렇게 빠져나온
                    // 더 짧은 이름에서 ArgumentOutOfRangeException이 난다.
                    if (!folderName.StartsWith(RequiredPackage + "@", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string manifest = Path.Combine(directories[i], "package.json");
                    string candidate = File.Exists(manifest)
                        ? ReadPackageVersion(manifest)
                        : folderName.Substring(folderName.IndexOf('@') + 1);
                    if (string.IsNullOrEmpty(candidate))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(bestVersion) || CompareVersions(candidate, bestVersion) > 0)
                    {
                        bestVersion = candidate;
                    }
                }

                if (!string.IsNullOrEmpty(bestVersion))
                {
                    version = bestVersion;
                    source = "registry";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// package.json에서 "version" 값 하나만 손으로 훑어 읽는다.
        /// </summary>
        /// <remarks>
        /// JsonUtility를 쓰지 않는 이유는 두 가지다. 역직렬화를 받을 [Serializable] 대상 타입이 여기에 없고,
        /// package.json의 모양은 패키지마다 달라서(중첩 객체, dependencies 블록) 한 타입으로 고정할 수 없다.
        /// 네 번의 인덱스 검사는 모두 빈 문자열로 떨어지므로, 형태가 어긋난 manifest는 예외가 아니라 "버전 없음"이
        /// 되고 호출자는 다음 후보로 넘어간다. 패키지 하나의 manifest가 이상하다고 탐지 전체가 죽으면 안 된다.
        /// </remarks>
        private static string ReadPackageVersion(string manifestPath)
        {
            try
            {
                string json = File.ReadAllText(manifestPath);
                int keyIndex = json.IndexOf("\"version\"", StringComparison.Ordinal);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex);
                int openQuote = colonIndex < 0 ? -1 : json.IndexOf('"', colonIndex);
                int closeQuote = openQuote < 0 ? -1 : json.IndexOf('"', openQuote + 1);
                if (openQuote < 0 || closeQuote <= openQuote)
                {
                    return string.Empty;
                }

                return json.Substring(openQuote + 1, closeQuote - openQuote - 1).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        // 3.2.16이 3.2.9보다 위로 오도록 숫자 단위로 비교한다 (문자열 비교로는 반대가 된다).
        private static int CompareVersions(string left, string right)
        {
            int[] leftParts = ParseVersionParts(left);
            int[] rightParts = ParseVersionParts(right);
            for (int i = 0; i < 3; i++)
            {
                if (leftParts[i] != rightParts[i])
                {
                    return leftParts[i] < rightParts[i] ? -1 : 1;
                }
            }

            return 0;
        }

        // major.minor.patch 세 자리만 본다. '-'나 '+' 뒤의 prerelease/build 접미사는 파싱 전에 잘라내므로
        // "3.2.12-preview.1"은 "3.2.12"와 같은 값으로 비교된다 - ZepetoHelperSelfTest.cs:346의
        // version-compare:suffix 검사가 그 동작을 고정하고 있으니 접미사 처리를 빼면 그 검사가 빨개진다.
        // 숫자로 읽히지 않는 자리는 조용히 0이 된다. 버전 문자열 하나 때문에 패키지 탐지가 통째로 예외로
        // 죽는 것보다, 그 후보가 낮게 평가되어 밀려나는 편이 낫기 때문이다.
        private static int[] ParseVersionParts(string version)
        {
            int[] parts = new int[3];
            if (string.IsNullOrEmpty(version))
            {
                return parts;
            }

            string core = version.Trim();
            int suffixIndex = core.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex > 0)
            {
                core = core.Substring(0, suffixIndex);
            }

            string[] segments = core.Split('.');
            for (int i = 0; i < parts.Length && i < segments.Length; i++)
            {
                int value;
                parts[i] = int.TryParse(segments[i], out value) ? value : 0;
            }

            return parts;
        }

        private static bool IsRequiredZepetoStudioPackageInstalled(out string foundVersion)
        {
            string source;
            // 실패 경로에서 foundVersion을 다시 비우지 않는다. TryGetZepetoStudioPackage는 첫 줄에서
            // version = string.Empty로 시작하고 return true 직전에만 값을 채우므로, false로 돌아온 시점에
            // foundVersion은 이미 비어 있다.
            if (!TryGetZepetoStudioPackage(out foundVersion, out source))
            {
                return false;
            }

            return CompareVersions(foundVersion, MinimumPackageVersion) >= 0;
        }
    }
}
