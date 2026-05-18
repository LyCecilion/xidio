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
            "你的设备现在是通过什么方式接入网络的？",
            [
                new("直接连接：校园 Wi-Fi / 宿舍有线 PPPoE", ConnectionMethod.DirectCampusNetwork),
                new("间接连接：通过路由器等设备", ConnectionMethod.CampusNetworkViaRouter),
                new("其他连接：手机热点 / 校外网络等", ConnectionMethod.OtherNetwork),
                new("不确定", ConnectionMethod.Unknown)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        var problemSymptom = PromptChoice<ProblemSymptom>(
            "你现在遇到的主要问题是什么？",
            [
                new("连不上 Wi-Fi", ProblemSymptom.CannotConnectWifi),
                new("连上了但显示无 Internet", ProblemSymptom.ConnectedNoInternet),
                new("认证页不弹出", ProblemSymptom.CaptivePortalNotShown),
                new("认证成功但打不开网页", ProblemSymptom.PortalAuthenticatedNoWeb),
                new("部分应用能用，部分应用不能用", ProblemSymptom.SomeApplicationsUnavailable),
                new("有线拨号失败", ProblemSymptom.PppoeDialFailed),
                new("有线拨号成功但没有网", ProblemSymptom.PppoeConnectedNoInternet),
                new("其他问题", ProblemSymptom.Other),
                new("不确定", ProblemSymptom.Unknown)
            ]);

        cancellationToken.ThrowIfCancellationRequested();
        var impactScope = PromptChoice<ImpactScope>(
            "这个问题影响到哪些设备或区域？",
            [
                new("只有这台设备", ImpactScope.OnlyThisDevice),
                new("同宿舍 / 同房间也有人遇到", ImpactScope.SameRoomOrDormitory),
                new("同楼层 / 附近区域也有人遇到", ImpactScope.SameFloorOrArea),
                new("更大范围都有人遇到", ImpactScope.WiderArea),
                new("不清楚", ImpactScope.Unknown)
            ]);

        return Task.FromResult(new UserScenarioInfo
        {
            Location = location,
            ConnectionMethod = connectionMethod,
            ProblemSymptom = problemSymptom,
            ImpactScope = impactScope
        });
    }

    private static string PromptLocation(CancellationToken cancellationToken)
    {
        var location = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("你的设备目前位于哪里？通常情况下，同一设备在不同位置的表现也会有差异。")
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
        var buildingNumber = AnsiConsole.Prompt(new TextPrompt<int>($"{dormitory}几号楼？")
            .Validate(number => number > 0
                ? ValidationResult.Success()
                : ValidationResult.Error("请输入正整数。")));

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
