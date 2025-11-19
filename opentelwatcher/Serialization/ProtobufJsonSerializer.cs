using Google.Protobuf;

namespace OpenTelWatcher.Serialization;

/// <summary>
/// Default implementation of IProtobufJsonSerializer using ProtobufToJsonConverter.
/// </summary>
public sealed class ProtobufJsonSerializer : IProtobufJsonSerializer
{
    private readonly bool _prettyPrint;

    public ProtobufJsonSerializer(bool prettyPrint = false)
    {
        _prettyPrint = prettyPrint;
    }

    public string Serialize<T>(T message) where T : IMessage
    {
        return ProtobufToJsonConverter.ConvertToJson(message, _prettyPrint);
    }
}
