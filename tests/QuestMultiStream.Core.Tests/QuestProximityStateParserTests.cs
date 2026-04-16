using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Parsing;

namespace QuestMultiStream.Core.Tests;

public sealed class QuestProximityStateParserTests
{
    [Fact]
    public void Parse_ReturnsKeepAwakeWhenLastActionWasProxClose()
    {
        const string output = """
            Action: "com.oculus.vrpowermanager.prox_far"
            Action: "com.oculus.vrpowermanager.prox_close"
            #829: act=com.oculus.vrpowermanager.prox_close flg=0x400010
            """;

        var mode = QuestProximityStateParser.Parse(output);

        Assert.Equal(QuestProximityMode.KeepAwake, mode);
    }

    [Fact]
    public void Parse_ReturnsNormalSensorWhenLastActionWasAutomationDisable()
    {
        const string output = """
            Action: "com.oculus.vrpowermanager.prox_close"
            #829: act=com.oculus.vrpowermanager.automation_disable flg=0x400010
            """;

        var mode = QuestProximityStateParser.Parse(output);

        Assert.Equal(QuestProximityMode.NormalSensor, mode);
    }

    [Fact]
    public void Parse_ReturnsUnknownWhenNoKnownActionExists()
    {
        const string output = """
            Action: "com.oculus.externaldisplayservice.SET_SCREEN_EYE"
            Action: "com.oculus.vrruntimeservice.COMPOSITOR_INITIALIZED"
            """;

        var mode = QuestProximityStateParser.Parse(output);

        Assert.Equal(QuestProximityMode.Unknown, mode);
    }
}
