namespace DesktopCompanion.WpfHost.Cognition;

public static class CompanionKernelRuntime
{
    private static string _currentKernelId = "balanced";

    public static string CurrentKernelId => _currentKernelId;

    public static CompanionKernelProfile Current => CompanionKernelCatalog.Resolve(_currentKernelId);

    public static void SetCurrent(string? kernelId)
    {
        _currentKernelId = CompanionKernelCatalog.Resolve(kernelId).Id;
    }
}
