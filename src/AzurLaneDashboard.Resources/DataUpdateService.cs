using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

namespace AzurLaneDashboard.Resources;

public sealed partial class DataUpdateService
{
    private const string LuaRepoUrl = "https://github.com/AzurLaneTools/AzurLaneLuaScripts.git";
    private const string LivePullManifestFileName = ".pull_live_manifest.json";
    private const int MaxProcessOutputChars = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly IReadOnlyList<string> DefaultCategories = AppDefaults.DefaultAssetCategories;

    public async Task<UpdateResult> RunAsync(UpdateOptions options, CancellationToken cancellationToken = default)
    {
        var projectRoot = string.IsNullOrWhiteSpace(options.ProjectRoot)
            ? ProjectLocator.FindProjectRoot()
            : Path.GetFullPath(options.ProjectRoot);
        var startedAt = DateTimeOffset.Now;
        var logs = new ProgressLogList(options.Progress);
        var command = BuildNativeCommand(options);
        const int totalStages = 6;

        try
        {
            Report(options.Progress, "준비", "업데이트 준비 중", 0, totalStages);
            var context = UpdateContext.Create(projectRoot, options);
            logs.Add("native update started; Python is not used");
            logs.Add($"project root: {projectRoot}");

            Report(options.Progress, "LuaScripts", "LuaScripts 저장소 동기화 중", 1, totalStages);
            if (!options.SkipGit)
            {
                await SyncLuaRepoAsync(context, logs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logs.Add("LuaScripts git sync skipped by option");
            }

            Report(options.Progress, "게임 리소스", "MuMu/ADB 리소스 동기화 확인 중", 2, totalStages);
            await PullLiveResourcesAsync(context, logs, cancellationToken).ConfigureAwait(false);

            Report(options.Progress, "맵 생성", "Lua 메타데이터를 WPF 맵으로 변환 중", 3, totalStages);
            var luaRegionDirectory = Path.Combine(context.LuaRepoDirectory, context.Region);
            var builder = new NativeLuaMapBuilder();
            await builder.BuildAsync(
                luaRegionDirectory,
                context.MapsDirectory,
                !options.SkipRegal,
                logs,
                cancellationToken).ConfigureAwait(false);

            Report(options.Progress, "이미지", "AssetBundle Texture2D PNG 추출 중", 4, totalStages);
            if (!options.MetadataOnly)
            {
                await PatchLocalAssetsAsync(context, logs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logs.Add("icon map patch skipped because metadata-only is enabled");
            }

            Report(options.Progress, "상태 저장", "업데이트 상태 파일 작성 중", 5, totalStages);
            WriteUpdateState(context, logs);

            var finishedAt = DateTimeOffset.Now;
            Report(options.Progress, "완료", "업데이트 완료", totalStages, totalStages);
            return new UpdateResult(true, startedAt, finishedAt, 0, command, "", logs.ToArray());
        }
        catch (OperationCanceledException)
        {
            logs.Add("update cancelled");
            Report(options.Progress, "취소됨", "업데이트가 취소되었습니다.", null, null, true);
            throw;
        }
        catch (Exception ex)
        {
            logs.Add($"ERROR: {ex.Message}");
            Report(options.Progress, "실패", ex.Message, null, null, true);
            return new UpdateResult(false, startedAt, DateTimeOffset.Now, 1, command, ex.Message, logs.ToArray());
        }
    }

    private sealed class ProcessOutputLimitExceededException(
        string streamName,
        string partialText,
        string message) : Exception(message)
    {
        public string StreamName { get; } = streamName;
        public string PartialText { get; } = partialText;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ObbUnpackResult(int Archives, int Copied, int Skipped, int Missing);

    private sealed record HashRow(string FilePath, long Size, string Md5Hash)
    {
        public bool HasHash => Size >= 0 && !string.IsNullOrWhiteSpace(Md5Hash);
    }

    private sealed class ProgressLogList(IProgress<UpdateProgress>? progress) : IList<string>
    {
        private readonly List<string> inner = [];

        public IEnumerator<string> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(string item)
        {
            inner.Add(item);
            progress?.Report(new UpdateProgress("", item, AppendLog: true));
        }

        public void Clear() => inner.Clear();

        public bool Contains(string item) => inner.Contains(item);

        public void CopyTo(string[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);

        public bool Remove(string item) => inner.Remove(item);

        public int Count => inner.Count;

        public bool IsReadOnly => false;

        public int IndexOf(string item) => inner.IndexOf(item);

        public void Insert(int index, string item)
        {
            inner.Insert(index, item);
            progress?.Report(new UpdateProgress("", item, AppendLog: true));
        }

        public void RemoveAt(int index) => inner.RemoveAt(index);

        public string this[int index]
        {
            get => inner[index];
            set => inner[index] = value;
        }
    }

    private sealed record UpdateContext(
        string ProjectRoot,
        string Region,
        string Serial,
        string AdbPath,
        string MuMuManagerPath,
        string DatabasePath,
        string DataDirectory,
        string MapsDirectory,
        string LuaRepoDirectory,
        string LuaBranch,
        string GameResourcesDirectory,
        string StaticAssetsDirectory,
        string LocalAssetManifest,
        string UpdateStatePath,
        IReadOnlyList<string> Categories,
        int ProgressEvery,
        IProgress<UpdateProgress>? Progress,
        bool SkipGameResources,
        bool RequireGameResources,
        bool MetadataOnly,
        bool SkipScripts,
        bool ForceGameResources,
        bool SkipIconExtract,
        bool ForceIconExtract)
    {
        public static UpdateContext Create(string projectRoot, UpdateOptions options)
        {
            var dataDirectory = string.IsNullOrWhiteSpace(options.DataDirectory)
                ? DataDirectoryFromDatabasePath(projectRoot, options.DatabasePath)
                : ResolveProjectPath(projectRoot, options.DataDirectory);
            var databasePath = string.IsNullOrWhiteSpace(options.DatabasePath)
                ? Path.Combine(dataDirectory, AppDefaults.DefaultDatabaseFileName)
                : ResolveProjectPath(projectRoot, options.DatabasePath);
            var categories =
                NormalizeAssetCategories(options.Categories.Count > 0 ? options.Categories : DefaultCategories);
            return new UpdateContext(
                projectRoot,
                string.IsNullOrWhiteSpace(options.Region)
                    ? AppDefaults.DefaultRegion
                    : options.Region.ToUpperInvariant(),
                string.IsNullOrWhiteSpace(options.Serial) ? AppDefaults.DefaultAdbSerial : options.Serial,
                string.IsNullOrWhiteSpace(options.AdbPath) ? AppDefaults.DefaultAdbExecutable : options.AdbPath,
                options.MuMuManagerPath,
                databasePath,
                dataDirectory,
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.MapsDirectory)
                        ? AppDefaults.DefaultMapsDirectory
                        : options.MapsDirectory),
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.LuaRepoDirectory)
                        ? AppDefaults.DefaultLuaRepoDirectory
                        : options.LuaRepoDirectory),
                string.IsNullOrWhiteSpace(options.LuaBranch) ? AppDefaults.DefaultLuaBranch : options.LuaBranch,
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.GameResourcesDirectory)
                        ? AppDefaults.DefaultGameResourcesDirectory
                        : options.GameResourcesDirectory),
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.StaticAssetsDirectory)
                        ? AppDefaults.DefaultStaticAssetsDirectory
                        : options.StaticAssetsDirectory),
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.LocalAssetManifest)
                        ? AppDefaults.DefaultLocalAssetManifest
                        : options.LocalAssetManifest),
                ResolveProjectPath(projectRoot,
                    string.IsNullOrWhiteSpace(options.UpdateStatePath)
                        ? AppDefaults.DefaultUpdateStatePath
                        : options.UpdateStatePath),
                categories,
                Math.Max(0, options.ProgressEvery),
                options.Progress,
                options.SkipGameResources,
                options.RequireGameResources,
                options.MetadataOnly,
                options.SkipScripts,
                options.ForceGameResources,
                options.SkipIconExtract,
                options.ForceIconExtract);
        }
    }
}