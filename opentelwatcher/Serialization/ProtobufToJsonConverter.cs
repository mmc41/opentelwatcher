using Google.Protobuf;
using System.Text.Json;

namespace OpenTelWatcher.Serialization;

/// <summary>
/// Converts Protobuf messages to JSON format.
/// </summary>
public static class ProtobufToJsonConverter
{
    /// <summary>
    /// Converts a Protobuf message to JSON string.
    /// </summary>
    /// <typeparam name="T">Type of Protobuf message.</typeparam>
    /// <param name="message">Protobuf message to convert.</param>
    /// <param name="prettyPrint">Whether to format JSON with indentation.</param>
    /// <returns>JSON representation of the Protobuf message.</returns>
    /// <exception cref="ArgumentNullException">Thrown if message is null.</exception>
    public static string ConvertToJson<T>(T message, bool prettyPrint) where T : IMessage
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        // Use Protobuf's built-in JSON formatter
        var jsonFormatter = new JsonFormatter(new JsonFormatter.Settings(formatDefaultValues: true));
        var jsonString = jsonFormatter.Format(message);

        // If pretty print is requested, re-serialize with indentation
        if (prettyPrint)
        {
            var jsonDocument = JsonDocument.Parse(jsonString);
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(jsonDocument, options);
        }

        return jsonString;
    }
}
