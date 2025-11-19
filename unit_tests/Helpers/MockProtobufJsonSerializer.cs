using Google.Protobuf;
using OpenTelWatcher.Serialization;

namespace UnitTests.Helpers;

/// <summary>
/// Mock serializer for testing.
/// </summary>
public class MockProtobufJsonSerializer : IProtobufJsonSerializer
{
    public string SerializedResult { get; set; } = "{\"test\":\"data\"}";

    public string Serialize<T>(T message) where T : IMessage
    {
        return SerializedResult;
    }
}
