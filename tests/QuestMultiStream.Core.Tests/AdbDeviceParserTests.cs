using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Parsing;

namespace QuestMultiStream.Core.Tests;

public sealed class AdbDeviceParserTests
{
    [Fact]
    public void Parse_FindsUsbAndWifiQuestDevices()
    {
        const string output = """
            List of devices attached
            1WMHH812345678	device product:eureka model:Quest_3 device:eureka transport_id:5
            192.168.0.91:5555	device product:seacliff model:Quest_Pro device:seacliff transport_id:9
            emulator-5554	device product:sdk_gphone model:sdk_gphone64_x86_64 device:emu64xa transport_id:11
            """;

        var devices = AdbDeviceParser.Parse(output);

        Assert.Collection(
            devices.Where(device => device.IsQuest),
            usb =>
            {
                Assert.Equal("Quest 3", usb.DisplayName);
                Assert.Equal(QuestDeviceConnectionKind.Usb, usb.ConnectionKind);
                Assert.True(usb.IsAvailable);
            },
            wifi =>
            {
                Assert.Equal("Quest Pro", wifi.DisplayName);
                Assert.Equal(QuestDeviceConnectionKind.TcpIp, wifi.ConnectionKind);
                Assert.Equal("192.168.0.91", wifi.IpAddress);
            });
    }
}
