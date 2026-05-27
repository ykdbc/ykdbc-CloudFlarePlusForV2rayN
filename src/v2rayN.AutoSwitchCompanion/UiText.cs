namespace v2rayN.AutoSwitchCompanion;

public sealed class UiText
{
    public static UiText For(string language)
    {
        return string.Equals(language, CompanionSettings.LanguageEnglish, StringComparison.OrdinalIgnoreCase)
            ? English
            : Chinese;
    }

    public static UiText Chinese { get; } = new()
    {
        Title = "v2rayN Cloudflare 自动切换插件",
        SpeedTestUrl = "测速地址",
        CheckMinutes = "检查间隔(分钟)",
        TestTimeoutMinutes = "测速超时(分钟)",
        RestartV2rayNAfterSwitch = "切换后重启 v2rayN",
        PasswallSsh = "iStoreOS / Passwall SSH",
        PasswallEnabled = "启用 Passwall 同步",
        PasswallHost = "主机",
        PasswallPort = "端口",
        PasswallUser = "用户",
        PasswallPassword = "密码",
        PasswallPrivateKey = "私钥",
        PasswallRestartAfterSwitch = "切换后重启 Passwall",
        PasswallSwitchUdpWithTcp = "同步 UDP 节点",
        TestPasswall = "测试 Passwall",
        PasswallGroup = "passwallgroup",
        Enabled = "启用",
        Name = "显示名称",
        GroupName = "v2rayN 分组名",
        ApiToken = "Cloudflare Token",
        Threshold = "Threshold",
        CurrentUsage = "当前使用量",
        RemainingUsage = "剩余量",
        Delay = "延迟",
        Speed = "速度",
        AddRule = "添加规则",
        RemoveRule = "删除规则",
        DeleteRuleConfirmTitle = "确认删除",
        DeleteRuleConfirmMessageFormat = "确定要删除这条规则吗？\n\n显示名称：{0}\n分组：{1}",
        Save = "保存",
        CheckNow = "立即检查",
        StartMonitor = "启动监控",
        Stop = "停止",
        OpenConfigFolder = "打开配置目录",
        Refresh = "刷新",
        SwitchGroup = "切换分组",
        ShowFloating = "打开悬浮窗口",
        HideFloating = "关闭悬浮窗口",
        About = "关于",
        Exit = "退出",
        LanguageSwitch = "English",
        TrayTip = "v2rayN Cloudflare 自动切换插件",
        AlreadyRunning = "插件已经在运行。",
        NoConfiguredGroup = "没有可切换的分组。",
        AboutText = "v2rayN Cloudflare 自动切换插件\n\n侧车插件，不修改 v2rayN 主业务逻辑。",
        MonitorStarted = "监控已启动。",
        MonitorStopped = "监控已停止。",
        SettingsSaved = "设置已保存",
        FloatingRefreshCooldown = "悬浮窗刷新太频繁，请稍后再试。",
        CurrentSelection = "当前条目",
        QueryFailed = "查询失败",
        NotMatchedGroup = "未匹配分组",
        Loading = "正在查询"
    };

    public static UiText English { get; } = new()
    {
        Title = "v2rayN Cloudflare Auto Switch Companion",
        SpeedTestUrl = "Speed test URL",
        CheckMinutes = "Check minutes",
        TestTimeoutMinutes = "Test timeout minutes",
        RestartV2rayNAfterSwitch = "Restart v2rayN after switch",
        PasswallSsh = "iStoreOS / Passwall SSH",
        PasswallEnabled = "Enable Passwall sync",
        PasswallHost = "Host",
        PasswallPort = "Port",
        PasswallUser = "User",
        PasswallPassword = "Password",
        PasswallPrivateKey = "Private key",
        PasswallRestartAfterSwitch = "Restart Passwall after switch",
        PasswallSwitchUdpWithTcp = "Switch UDP with TCP",
        TestPasswall = "Test Passwall",
        PasswallGroup = "passwallgroup",
        Enabled = "Enabled",
        Name = "Name",
        GroupName = "v2rayN group name",
        ApiToken = "Cloudflare token",
        Threshold = "Threshold",
        CurrentUsage = "Current usage",
        RemainingUsage = "Remaining",
        Delay = "Delay",
        Speed = "Speed",
        AddRule = "Add rule",
        RemoveRule = "Remove rule",
        DeleteRuleConfirmTitle = "Confirm delete",
        DeleteRuleConfirmMessageFormat = "Delete this rule?\n\nName: {0}\nGroup: {1}",
        Save = "Save",
        CheckNow = "Check now",
        StartMonitor = "Start monitor",
        Stop = "Stop",
        OpenConfigFolder = "Open config folder",
        Refresh = "Refresh",
        SwitchGroup = "Switch group",
        ShowFloating = "Open floating window",
        HideFloating = "Close floating window",
        About = "About",
        Exit = "Exit",
        LanguageSwitch = "中文",
        TrayTip = "v2rayN Cloudflare Auto Switch Companion",
        AlreadyRunning = "The companion is already running.",
        NoConfiguredGroup = "No configured group is available.",
        AboutText = "v2rayN Cloudflare Auto Switch Companion\n\nSidecar companion; v2rayN core logic is not modified.",
        MonitorStarted = "Monitor started.",
        MonitorStopped = "Monitor stopped.",
        SettingsSaved = "Settings saved",
        FloatingRefreshCooldown = "Floating window refresh is cooling down. Try again later.",
        CurrentSelection = "Current profile",
        QueryFailed = "Query failed",
        NotMatchedGroup = "No matching group",
        Loading = "Loading"
    };

    public required string Title { get; init; }
    public required string SpeedTestUrl { get; init; }
    public required string CheckMinutes { get; init; }
    public required string TestTimeoutMinutes { get; init; }
    public required string RestartV2rayNAfterSwitch { get; init; }
    public required string PasswallSsh { get; init; }
    public required string PasswallEnabled { get; init; }
    public required string PasswallHost { get; init; }
    public required string PasswallPort { get; init; }
    public required string PasswallUser { get; init; }
    public required string PasswallPassword { get; init; }
    public required string PasswallPrivateKey { get; init; }
    public required string PasswallRestartAfterSwitch { get; init; }
    public required string PasswallSwitchUdpWithTcp { get; init; }
    public required string TestPasswall { get; init; }
    public required string PasswallGroup { get; init; }
    public required string Enabled { get; init; }
    public required string Name { get; init; }
    public required string GroupName { get; init; }
    public required string ApiToken { get; init; }
    public required string Threshold { get; init; }
    public required string CurrentUsage { get; init; }
    public required string RemainingUsage { get; init; }
    public required string Delay { get; init; }
    public required string Speed { get; init; }
    public required string AddRule { get; init; }
    public required string RemoveRule { get; init; }
    public required string DeleteRuleConfirmTitle { get; init; }
    public required string DeleteRuleConfirmMessageFormat { get; init; }
    public required string Save { get; init; }
    public required string CheckNow { get; init; }
    public required string StartMonitor { get; init; }
    public required string Stop { get; init; }
    public required string OpenConfigFolder { get; init; }
    public required string Refresh { get; init; }
    public required string SwitchGroup { get; init; }
    public required string ShowFloating { get; init; }
    public required string HideFloating { get; init; }
    public required string About { get; init; }
    public required string Exit { get; init; }
    public required string LanguageSwitch { get; init; }
    public required string TrayTip { get; init; }
    public required string AlreadyRunning { get; init; }
    public required string NoConfiguredGroup { get; init; }
    public required string AboutText { get; init; }
    public required string MonitorStarted { get; init; }
    public required string MonitorStopped { get; init; }
    public required string SettingsSaved { get; init; }
    public required string FloatingRefreshCooldown { get; init; }
    public required string CurrentSelection { get; init; }
    public required string QueryFailed { get; init; }
    public required string NotMatchedGroup { get; init; }
    public required string Loading { get; init; }
}
