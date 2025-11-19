using Google.Protobuf;

namespace OpenTelWatcher.Serialization;

/// <summary>
/// Interface for serializing Protobuf messages to JSON.
/// </summary>
public interface IProtobufJsonSerializer
{
    /// <summary>
    /// Serializes a Protobuf message to JSON string.
    /// </summary>
    string Serialize<T>(T message) where T : IMessage;
}
