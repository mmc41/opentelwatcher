using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Serialization;
using System.Text.Json;
using Xunit;

namespace OpenTelWatcher.Tests.Serialization;

public class ProtobufToJsonConverterTests
{
    // Note: Using Google.Protobuf.WellKnownTypes.Any as a test message
    // In actual usage, this will be used with OTLP proto types

    [Fact]
    public void ConvertToJson_WithValidProtobufMessage_ReturnsValidJson()
    {
        // Arrange
        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        var json = ProtobufToJsonConverter.ConvertToJson(timestamp, prettyPrint: false);

        // Assert
        json.Should().NotBeNullOrEmpty();

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        doc.Should().NotBeNull();
    }

    [Fact]
    public void ConvertToJson_WithPrettyPrint_ReturnsIndentedJson()
    {
        // Arrange
        // Use Struct which represents a JSON object
        var structMsg = new Struct();
        structMsg.Fields.Add("name", Google.Protobuf.WellKnownTypes.Value.ForString("test"));
        structMsg.Fields.Add("count", Google.Protobuf.WellKnownTypes.Value.ForNumber(42));

        // Act
        var json = ProtobufToJsonConverter.ConvertToJson(structMsg, prettyPrint: true);

        // Assert
        json.Should().Contain("\n");  // Should contain newlines
        json.Should().Contain("  ");  // Should contain indentation
    }

    [Fact]
    public void ConvertToJson_WithoutPrettyPrint_ReturnsCompactJson()
    {
        // Arrange
        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        var json = ProtobufToJsonConverter.ConvertToJson(timestamp, prettyPrint: false);

        // Assert
        // Compact JSON should not have extra whitespace between elements
        json.Should().NotContain("\n  ");
    }

    [Fact]
    public void ConvertToJson_WithNullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => ProtobufToJsonConverter.ConvertToJson<Timestamp>(null!, prettyPrint: false);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConvertToJson_WithComplexMessage_PreservesAllFields()
    {
        // Arrange
        // Use Struct with nested values to test field preservation
        var structMsg = new Struct();
        structMsg.Fields.Add("name", Google.Protobuf.WellKnownTypes.Value.ForString("example"));
        structMsg.Fields.Add("count", Google.Protobuf.WellKnownTypes.Value.ForNumber(123));
        structMsg.Fields.Add("enabled", Google.Protobuf.WellKnownTypes.Value.ForBool(true));

        // Act
        var json = ProtobufToJsonConverter.ConvertToJson(structMsg, prettyPrint: false);

        // Assert
        json.Should().Contain("name");  // field name
        json.Should().Contain("example");  // field value
        json.Should().Contain("123");  // number value

        // Verify valid JSON structure
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("name", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("count", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("enabled", out _).Should().BeTrue();
    }

    [Fact]
    public void ConvertToJson_WithOtlpTraceMessage_ReturnsValidJson()
    {
        // Arrange - Create an OTLP trace message with actual data
        var traceId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var spanId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(traceId),
            SpanId = ByteString.CopyFrom(spanId),
            Name = "test-span",
            StartTimeUnixNano = 1700000000000000000,
            EndTimeUnixNano = 1700000001000000000
        };
        span.Attributes.Add(new KeyValue { Key = "test.key", Value = new AnyValue { StringValue = "test.value" } });

        var scopeSpan = new ScopeSpans();
        scopeSpan.Spans.Add(span);

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.Resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "test-service" } });
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var traceRequest = new ExportTraceServiceRequest();
        traceRequest.ResourceSpans.Add(resourceSpan);

        // Act
        var json = ProtobufToJsonConverter.ConvertToJson(traceRequest, prettyPrint: false);

        // Assert
        json.Should().NotBeNullOrEmpty();

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        doc.Should().NotBeNull();

        // Verify structure
        doc.RootElement.TryGetProperty("resourceSpans", out var resourceSpans).Should().BeTrue();
        var resourceSpansArray = resourceSpans.EnumerateArray().ToList();
        resourceSpansArray.Should().HaveCount(1);

        // Verify resource
        var resource = resourceSpansArray[0].GetProperty("resource");
        var attributes = resource.GetProperty("attributes").EnumerateArray().ToList();
        attributes.Should().Contain(attr =>
            attr.GetProperty("key").GetString() == "service.name" &&
            attr.GetProperty("value").GetProperty("stringValue").GetString() == "test-service");

        // Verify span
        var scopeSpans = resourceSpansArray[0].GetProperty("scopeSpans").EnumerateArray().ToList();
        scopeSpans.Should().HaveCount(1);
        var spans = scopeSpans[0].GetProperty("spans").EnumerateArray().ToList();
        spans.Should().HaveCount(1);
        spans[0].GetProperty("name").GetString().Should().Be("test-span");
    }
}

