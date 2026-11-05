namespace AzurLaneDashboard.Automation.Device;

/// <summary>What a macro needs from the device: the screen and touch input. Packets are never sent.</summary>
public interface IAutomationDevice
{
    /// <summary>The current screen as PNG bytes.</summary>
    byte[] Capture();

    /// <summary>A press at one point held for <paramref name="durationMs"/>, as ALAuto's touch does.</summary>
    void Tap(int x, int y, int durationMs);

    void Swipe(int x1, int y1, int x2, int y2, int durationMs);

    /// <summary>The Android back key.</summary>
    void Back();
}
