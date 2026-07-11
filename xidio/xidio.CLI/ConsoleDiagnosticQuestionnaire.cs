using Spectre.Console;
using xidio.Core.Models;

namespace xidio.CLI;

internal static class ConsoleDiagnosticQuestionnaire
{
    public static Task<UserScenarioInfo> AskAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = PromptLocation(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var connectionMethod = PromptChoice<ConnectionMethod>(
            "你的设备是如何连接校园网的？",
            [
                new("直接连接：连接到 stu-xdwlan 等校园网 Wi-Fi 节点，或使用有线直接连接墙口", ConnectionMethod.DirectCampusNetwork),
                new("间接连接：通过连接到宿舍的路由器等方式，间接连接到校园网", ConnectionMethod.CampusNetworkViaRouter),
                new("其他连接：使用移动数据的手机热点等，并没有直接连接到校园网", ConnectionMethod.OtherNetwork),
                new("不确定", ConnectionMethod.Unknown)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        var accessMethod = PromptChoice<NetworkAccessMethod>(
            "你的设备当前使用了哪种接入方式？",
            [
                new("Wi-Fi 连接（连接到校园网 Wi-Fi 节点或路由器等）", NetworkAccessMethod.Wireless),
                new("有线连接（未拨号，直接使用网线连接到路由器等）", NetworkAccessMethod.Ethernet),
                new("PPPoE / 宽带拨号（连接到了墙口的网口）", NetworkAccessMethod.Pppoe),
                new("不确定", NetworkAccessMethod.Unknown)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        var problemSymptom = PromptChoice<ProblemSymptom>(
            "你现在遇到的主要问题是什么？",
            [
                new("没有显著问题，仅进行一次诊断", ProblemSymptom.NoProblem),
                new("无法连接 Wi-Fi", ProblemSymptom.CannotConnectWifi),
                new("Wi-Fi 连接成功，但提示无 Internet", ProblemSymptom.ConnectedNoInternet),
                new("未弹出校园网认证页", ProblemSymptom.CaptivePortalNotShown),
                new("已认证，但仍无法上网", ProblemSymptom.PortalAuthenticatedNoWeb),
                new("可上网，但部分应用可上网，部分应用无法上网", ProblemSymptom.SomeApplicationsUnavailable),
                new("拨号失败", ProblemSymptom.PppoeDialFailed),
                new("拨号成功，但无法上网", ProblemSymptom.PppoeConnectedNoInternet),
                new("其他问题", ProblemSymptom.Other),
                new("不确定", ProblemSymptom.Unknown)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        var impactScope = problemSymptom == ProblemSymptom.NoProblem
            ? ImpactScope.Unknown
            : PromptChoice<ImpactScope>(
                "是否有其他设备或用户也有此问题？",
                [
                    new("只有这台设备有该问题", ImpactScope.OnlyThisDevice),
                    new("同宿舍 / 同房间也有人遇到", ImpactScope.SameRoomOrDormitory),
                    new("同楼层 / 附近区域也有人遇到", ImpactScope.SameFloorOrArea),
                    new("更大范围都有人遇到", ImpactScope.WiderArea),
                    new("不清楚", ImpactScope.Unknown)
                ]);

        cancellationToken.ThrowIfCancellationRequested();
        var collectSensitiveSystemInformation = AnsiConsole.Prompt(
            new ConfirmationPrompt(
                "是否同意 xidio 进一步收集系统代理等敏感系统信息？诊断结果不会自动上报。")
            {
                DefaultValue = false
            });
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new UserScenarioInfo
        {
            Location = location,
            ConnectionMethod = connectionMethod,
            AccessMethod = accessMethod,
            ProblemSymptom = problemSymptom,
            ImpactScope = impactScope,
            CollectSensitiveSystemInformation = collectSensitiveSystemInformation
        });
    }

    private static string PromptLocation(CancellationToken cancellationToken)
    {
        var location = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("你的设备目前位于哪里？")
            .PageSize(12)
            .AddChoices(
                "海棠公寓",
                "竹园公寓",
                "丁香公寓",
                "图书馆",
                "网安大楼",
                "家属区",
                "综合楼",
                "北校区",
                "其他"));

        cancellationToken.ThrowIfCancellationRequested();

        return location switch
        {
            "海棠公寓" or "竹园公寓" or "丁香公寓" => PromptDormitoryBuilding(location),
            "图书馆" => PromptLibraryArea(),
            "网安大楼" => PromptNetworkSecurityBuildingArea(),
            "综合楼" => PromptComprehensiveBuilding(),
            _ => location
        };
    }

    private static string PromptDormitoryBuilding(string dormitory)
    {
        var buildingNumber = AnsiConsole.Prompt(new TextPrompt<int>($"请输入所在{dormitory}的楼栋数（一个正整数）：")
            .Validate(number => number > 0
                ? ValidationResult.Success()
                : ValidationResult.Error("请输入一个正整数")));

        return $"{dormitory} {buildingNumber} 号楼";
    }

    private static string PromptLibraryArea()
    {
        var area = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("你现在位于图书馆哪个区域？")
            .AddChoices("A", "B", "C", "D"));

        return $"图书馆 {area} 区";
    }

    private static string PromptNetworkSecurityBuildingArea()
    {
        var area = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("你现在位于网安大楼哪个学院区域？")
            .PageSize(8)
            .AddChoices(
                "人工智能学院",
                "网络安全学院",
                "集成电路学院",
                "计算机科学学院",
                "不确定"));

        return area == "不确定" ? "网安大楼" : $"网安大楼 {area}";
    }

    private static string PromptComprehensiveBuilding()
    {
        return AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("你现在位于哪个综合楼？")
            .AddChoices("旧综合楼", "新综合楼"));
    }

    private static T PromptChoice<T>(string title, IReadOnlyList<PromptOption<T>> choices)
    {
        var selectedLabel = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title(title)
            .PageSize(10)
            .AddChoices(choices.Select(choice => choice.Label)));

        return choices.First(choice => choice.Label == selectedLabel).Value;
    }

    private sealed record PromptOption<T>(string Label, T Value);
}
