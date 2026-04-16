using System.Text.RegularExpressions;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Parsing;

public static partial class QuestProximityStateParser
{
    public static QuestProximityMode Parse(string text)
    {
        string? lastAction = null;

        foreach (Match match in ProximityActionPattern().Matches(text))
        {
            lastAction = match.Groups["action"].Value;
        }

        return lastAction switch
        {
            "prox_close" => QuestProximityMode.KeepAwake,
            "prox_far" => QuestProximityMode.NormalSensor,
            "prox_automation_disable" => QuestProximityMode.NormalSensor,
            "automation_disable" => QuestProximityMode.NormalSensor,
            _ => QuestProximityMode.Unknown
        };
    }

    [GeneratedRegex(@"com\.oculus\.vrpowermanager\.(?<action>prox_close|prox_far|prox_automation_disable|automation_disable)", RegexOptions.Compiled)]
    private static partial Regex ProximityActionPattern();
}
