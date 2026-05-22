namespace v2rayN.AutoSwitchCompanion;

internal static class WindowsShellIdentity
{
    public const string AppUserModelId = "ykdbc.CloudFlarePlus.AutoSwitchCompanion";
    public const string ProductTitle = "CloudFlarePlus For v2rayN";

    public static void Apply()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // The app can still run without a shell identity; Windows will just fall back to the exe identity.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
