namespace v2rayN.AutoSwitchCompanion;

public static class AppIconProvider
{
    private const string ResourceName = "v2rayN.AutoSwitchCompanion.Assets.AutoSwitchCompanion.ico";

    public static Icon Load()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return SystemIcons.Application;
        }

        using (stream)
        {
            return new Icon(stream);
        }
    }
}
