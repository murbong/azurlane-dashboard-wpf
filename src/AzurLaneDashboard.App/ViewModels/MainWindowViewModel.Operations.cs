using AzurLaneDashboard.Capture;
using AzurLaneDashboard.Core.Configuration;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshCaptureInterfacesAsync()
    {
        if (IsRefreshingCaptureInterfaces)
        {
            return;
        }

        IsRefreshingCaptureInterfaces = true;
        var backend = NormalizeCaptureBackend(CaptureBackend);
        var label = CaptureInterfaceBackendLabel(backend);
        CaptureInterfaceStatus = $"{label} 인터페이스 목록을 검색하는 중입니다.";

        try
        {
            if (backend == AppDefaults.CaptureBackendAdb)
            {
                EnsureCaptureInterfaceOption();
                CaptureInterfaceStatus = "ADB 캡처는 Android tcpdump의 기본 인터페이스(any)를 사용합니다.";
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var interfaces = new List<CaptureInterfaceOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var entries = backend == AppDefaults.CaptureBackendNpcap
                ? DumpcapCaptureService.ListNpcapInterfacesAsync(timeout.Token)
                : DumpcapCaptureService.ListInterfacesAsync(CaptureDumpcapPath, timeout.Token);
            await foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || !seen.Add(entry.Id))
                {
                    continue;
                }

                interfaces.Add(new CaptureInterfaceOption(entry.Id, entry.Name));
            }

            Replace(CaptureInterfaceOptions, WithCurrentCaptureInterface(interfaces));
            CaptureInterfaceStatus = interfaces.Count == 0
                ? $"{label} 인터페이스가 없습니다. Npcap 또는 dumpcap 설치 상태를 확인하세요."
                : $"{label} 인터페이스 {interfaces.Count}개를 찾았습니다.";
        }
        catch (OperationCanceledException)
        {
            EnsureCaptureInterfaceOption();
            CaptureInterfaceStatus = $"{label} 인터페이스 검색 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            EnsureCaptureInterfaceOption();
            CaptureInterfaceStatus = $"{label} 인터페이스 검색 실패: {ex.Message}";
        }
        finally
        {
            IsRefreshingCaptureInterfaces = false;
        }
    }

    private IEnumerable<CaptureInterfaceOption> WithCurrentCaptureInterface(
        IEnumerable<CaptureInterfaceOption> interfaces)
    {
        var items = interfaces.ToList();
        var current = string.IsNullOrWhiteSpace(CaptureInterface)
            ? AppDefaults.DefaultCaptureInterface
            : CaptureInterface;

        if (items.All(item => !string.Equals(item.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            items.Insert(0, new CaptureInterfaceOption(current, "현재 설정"));
        }

        return items;
    }

    private void EnsureCaptureInterfaceOption()
    {
        var current = string.IsNullOrWhiteSpace(CaptureInterface)
            ? AppDefaults.DefaultCaptureInterface
            : CaptureInterface;

        if (CaptureInterfaceOptions.Any(item => string.Equals(item.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        CaptureInterfaceOptions.Insert(0, new CaptureInterfaceOption(current, "현재 설정"));
    }

    private static string NormalizeCaptureBackend(string? value)
    {
        if (string.Equals(value, AppDefaults.CaptureBackendNpcap, StringComparison.OrdinalIgnoreCase))
        {
            return AppDefaults.CaptureBackendNpcap;
        }

        if (string.Equals(value, AppDefaults.CaptureBackendAdb, StringComparison.OrdinalIgnoreCase))
        {
            return AppDefaults.CaptureBackendAdb;
        }

        return AppDefaults.CaptureBackendDumpcap;
    }

    private static string CaptureInterfaceBackendLabel(string backend)
    {
        return backend == AppDefaults.CaptureBackendNpcap ? "Npcap" :
            backend == AppDefaults.CaptureBackendAdb ? "ADB" : "dumpcap";
    }
}
