namespace QuestMultiStream.Core.Models;

public sealed record QuestDevice(
    string Serial,
    string DisplayName,
    string? ModelName,
    string? ProductName,
    string? DeviceCodeName,
    string State,
    QuestDeviceConnectionKind ConnectionKind,
    bool IsQuest,
    string? IpAddress)
{
    public bool IsAvailable => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);

    public string ConnectionLabel => ConnectionKind switch
    {
        QuestDeviceConnectionKind.TcpIp => "Wi-Fi ADB",
        QuestDeviceConnectionKind.Usb => "USB",
        _ => "Unknown"
    };
}
