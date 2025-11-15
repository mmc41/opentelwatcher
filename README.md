# OpenTelWatcher

A minimal, file-based OpenTelemetry collector for local development and testing. Receives OTLP telemetry data over HTTP and persists it as NDJSON files for easy inspection by developers and AI tools.

## Status

[![build](https://github.com/mmc41/opentelwatcher/actions/workflows/build-validation.yml/badge.svg)](https://github.com/mmc41/opentelwatcher/actions/workflows/build-validation.yml)

## What is OpenTelWatcher?

OpenTelWatcher is a lightweight OTLP receiver designed for **development and testing environments only**. It accepts traces, logs, and metrics from OpenTelemetry-instrumented applications and writes them to human-readable NDJSON files.

**Key Features:**
- Receives OTLP data via HTTP/Protobuf (standard protocol used by OpenTelemetry SDKs)
- Parses complete OTLP messages (traces, logs, metrics) with all attributes and metadata
- Writes telemetry to NDJSON files (one JSON object per line)
- **Automatic error filtering** - traces and logs with errors/exceptions automatically saved to `.errors.ndjson` files
- Command-line interface (CLI) for service management and diagnostics
- **Daemon mode** for background execution with health check verification
- Web status dashboard with real-time statistics and endpoint information
- Interactive API documentation (Swagger UI) with consistent 127.0.0.1 addressing
- Automatic file rotation when size limits are exceeded
- Health monitoring with circuit breaker for file I/O failures
- Pre-flight check to prevent multiple instances on the same port
- PID file tracking for daemon instances
- Diagnostics API endpoint for runtime inspection
- Single-file deployment with embedded web resources
- Cross-platform: Windows, macOS, Linux
- Zero external dependencies or cloud services
- Comprehensive test suite (232 tests: 168 unit + 64 E2E)

**This is NOT a production collector.** It does not support:
- gRPC protocol
- Authentication/authorization
- Compression (gzip)
- Data forwarding or exporting
- Dashboards or visualizations

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- No other dependencies required

## Quick Start

### 1. Clone and Build

```bash
git clone <repository-url>
cd opentelwatcher
dotnet build
```

### 2. Run the Application

```bash
cd opentelwatcher
dotnet run -- start
```

*The "--" seperate dotnet arguments from application arguments. It is only necceasary when running the application using dotnet run. When running the application directly write "opentelwatcher start".*

You should see output similar to:

```
================================================================================
OpenTelWatcher v1.0.0 - OTLP/HTTP Receiver
https://github.com/mmc41/opentelwatcher
================================================================================
Status Dashboard: http://127.0.0.1:4318/
API Documentation: http://127.0.0.1:4318/swagger
Output Directory: D:\repos\opentelwatcher_spec\watcher\telemetry-data
OTLP Endpoints:
  - Traces:  http://127.0.0.1:4318/v1/traces
  - Logs:    http://127.0.0.1:4318/v1/logs
  - Metrics: http://127.0.0.1:4318/v1/metrics

WARNING: No authentication enabled. For local development use only.
================================================================================
```

**Key URLs:**
- Status Dashboard: `http://127.0.0.1:4318/`
- API Documentation (Swagger): `http://127.0.0.1:4318/swagger`
- Health Check: `http://127.0.0.1:4318/healthz`

**Note on Addressing:** OpenTelWatcher standardizes on `127.0.0.1` for all internal communication and displayed URLs to avoid IPv4/IPv6 ambiguity. While you can also access the dashboard via `localhost`, all displayed endpoint URLs and API communication use `127.0.0.1`.

### 3. Send Telemetry Data

Configure your OpenTelemetry SDK to export to `http://127.0.0.1:4318`:

**C# Example:**
```csharp
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyApp")
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("my-service"))
    .AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri("http://127.0.0.1:4318");
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
    })
    .Build();

// Create and export a trace
var tracer = tracerProvider.GetTracer("MyApp");
using (var span = tracer.StartActiveSpan("example-operation"))
{
    span.SetAttribute("user.id", "12345");
    span.SetAttribute("http.method", "POST");
    // Your application code here
}
```

**Python Example:**
```python
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter

trace.set_tracer_provider(TracerProvider())
tracer = trace.get_tracer(__name__)

otlp_exporter = OTLPSpanExporter(endpoint="http://127.0.0.1:4318/v1/traces")
trace.get_tracer_provider().add_span_processor(BatchSpanProcessor(otlp_exporter))

with tracer.start_as_current_span("example-operation"):
    print("Generating trace data...")
```

### 4. View the Output

Telemetry data is written to `./telemetry-data/` directory:

```bash
ls telemetry-data/
# Output:
# traces.20251108_143022.ndjson
# traces.20251108_143022.errors.ndjson  # Automatically created when errors detected
# logs.20251108_143022.ndjson
# logs.20251108_143022.errors.ndjson    # Automatically created when errors detected
# metrics.20251108_143022.ndjson
```

**Error File Filtering:**
OpenTelWatcher automatically detects and saves errors/exceptions to separate `.errors.ndjson` files:
- **Traces**: Spans with `STATUS_CODE_ERROR` or exception events
- **Logs**: Records with ERROR/FATAL severity (≥17) or exception attributes (`exception.type`, `exception.message`, `exception.stacktrace`)
- Error files use the same timestamp as normal files for easy correlation
- Created on-demand (only when errors are detected)
- Automatically deleted when using the `clear` command

Each file contains one JSON object per line (OTLP protobuf converted to JSON):

```bash
cat telemetry-data/traces.20251108_143022.ndjson | head -1 | jq
```

```json
{
  "resourceSpans": [
    {
      "resource": {
        "attributes": [
          {
            "key": "service.name",
            "value": {
              "stringValue": "my-service"
            }
          },
          {
            "key": "service.version",
            "value": {
              "stringValue": "1.0.0"
            }
          }
        ]
      },
      "scopeSpans": [
        {
          "scope": {
            "name": "MyApp",
            "version": "1.0.0"
          },
          "spans": [
            {
              "traceId": "5B8EFFF798038103D269B633813FC60C",
              "spanId": "EEE19B7EC3C1B174",
              "name": "example-operation",
              "kind": "SPAN_KIND_INTERNAL",
              "startTimeUnixNano": "1699564800000000000",
              "endTimeUnixNano": "1699564801000000000",
              "attributes": [
                {
                  "key": "user.id",
                  "value": {
                    "stringValue": "12345"
                  }
                },
                {
                  "key": "http.method",
                  "value": {
                    "stringValue": "POST"
                  }
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

## Status Dashboard

OpenTelWatcher includes a web-based status dashboard that displays real-time telemetry statistics and system information.

**Access the dashboard:**
```bash
# Open in your browser
http://127.0.0.1:4318/
```

**The dashboard shows:**
- Application version and uptime
- Real-time telemetry statistics (traces, logs, metrics received)
- Health status and consecutive error count
- Configuration settings (output directory, file rotation, timeouts)
- OTLP endpoint URLs
- Diagnostic endpoints

The dashboard automatically refreshes when you reload the page, providing up-to-date information about received telemetry and system health.

## CLI Interface

OpenTelWatcher includes a command-line interface for managing the service and viewing diagnostics.

### CLI Commands

**Show help (default command):**
```bash
dotnet run --project opentelwatcher
```

Displays help information with all available commands and options, including examples.

**Start the service with options:**
```bash
dotnet run --project opentelwatcher -- start --port 4318 --output-dir ./data --log-level Information
```

Options:
- `--port <number>` - Port number (default: 4318)
- `--output-dir, -o <path>` - Output directory (default: ./telemetry-data)
- `--log-level <level>` - Log level: Trace, Debug, Information, Warning, Error, Critical (default: Information)
- `--daemon` - Run in background (non-blocking mode)

**Start in background (daemon mode):**
```bash
dotnet run --project opentelwatcher -- start --daemon
```

Daemon mode:
- Spawns a background process and exits immediately
- Performs pre-flight health check to verify successful startup
- Creates `opentelwatcher.pid` file in platform-appropriate temp directory (removed on clean shutdown)
- Logs output to NLog configuration (check logs for errors)
- Use `opentelwatcher stop` to gracefully shut down the background instance

**Platform-Specific Daemon Behavior:**

OpenTelWatcher uses platform-specific approaches for proper daemon/background execution:

- **Windows:** Uses `CreateNoWindow` process flag for proper background execution
  - Child process detaches from parent console
  - Survives terminal closure automatically
  - Full support for all daemon features

- **Linux/macOS:** Uses `nohup` command wrapper for terminal detachment
  - Process survives terminal closure (proper daemon behavior)
  - Output redirected to `/dev/null` (use NLog configuration for logging)
  - PID file created in `XDG_RUNTIME_DIR` or temp directory

**Note:** On Linux/macOS, ensure `nohup` is available (standard on most distributions). For production deployments, consider using systemd (Linux) or launchd (macOS) service managers.

**Stop the running instance:**
```bash
dotnet run --project opentelwatcher -- stop
```

Gracefully shuts down the running OpenTelWatcher instance via the API. Works for both foreground and daemon instances.

**View diagnostic information:**
```bash
dotnet run --project opentelwatcher -- info
```

Displays:
- Application version and process ID
- Health status and uptime
- Telemetry statistics (file count and total size)
- Configuration settings
- Recent errors (if any)

**Clear telemetry data:**
```bash
dotnet run --project opentelwatcher -- clear
```

Clears all telemetry files from the output directory:
- **If instance running**: Validates and clears via `/api/clear` endpoint
- **If no instance**: Clears files directly from specified directory
- Options:
  - `--output-dir, -o <path>` - Directory to clear (validated against instance when running)
  - `--verbose` - Show detailed operation information
  - `--silent` - Suppress all output except errors

**Show detailed help:**
```bash
dotnet run --project opentelwatcher -- --help
# or for command-specific help:
dotnet run --project opentelwatcher -- start --help
```

### CLI Features

- **Daemon Mode:** Background execution with health check verification
- **Pre-flight Check:** Automatically detects if an instance is already running before startup
- **PID File:** Creates `opentelwatcher.pid` with process ID for daemon tracking
- **Version Compatibility:** CLI checks that the running instance has a compatible major version
- **Instance Detection:** Prevents starting multiple instances on the same port
- **Graceful Shutdown:** Sends shutdown signal and waits up to 30 seconds for clean termination
- **Automatic Help:** System.CommandLine generates comprehensive help with examples
- **Exit Codes:**
  - `0` - Success
  - `1` - User error (invalid arguments, instance already running, etc.)
  - `2` - System error (connection failure, timeout, etc.)

### API Documentation

OpenTelWatcher includes interactive API documentation via Swagger UI:

```bash
# Access Swagger UI at:
http://127.0.0.1:4318/swagger

# View OpenAPI specification (JSON):
http://127.0.0.1:4318/openapi/v1.json
```

The API documentation provides:
- Complete endpoint reference with request/response schemas
- Interactive testing interface ("Try it out" feature)
- Detailed descriptions and examples for all endpoints
- Automatic schema generation from endpoint metadata

## Configuring OpenTelemetry Endpoints

OpenTelWatcher accepts telemetry data on standard OTLP endpoints. Configure your OpenTelemetry SDK to send data to `http://127.0.0.1:4318` using the **HTTP/Protobuf** protocol.

### Endpoint URLs

| Signal | Endpoint URL | Purpose |
|--------|--------------|---------|
| Traces | `http://127.0.0.1:4318/v1/traces` | Distributed tracing spans |
| Logs | `http://127.0.0.1:4318/v1/logs` | Application logs |
| Metrics | `http://127.0.0.1:4318/v1/metrics` | Application metrics |

**Important:**
- Use `HttpProtobuf` protocol (NOT gRPC or JSON)
- Content-Type must be `application/x-protobuf`
- Base endpoint is `http://127.0.0.1:4318` (SDK appends `/v1/{signal}`)

### .NET / C# Configuration

#### Traces Setup

```csharp
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("my-service")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = "development",
                ["service.version"] = "1.0.0"
            }))
        .AddAspNetCoreInstrumentation()  // Auto-instrument ASP.NET Core
        .AddHttpClientInstrumentation()  // Auto-instrument HttpClient
        .AddSource("MyApp.*")            // Custom activity sources
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://127.0.0.1:4318");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));

var app = builder.Build();
app.Run();
```

#### Metrics Setup

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("my-service"))
        .AddAspNetCoreInstrumentation()    // ASP.NET Core metrics
        .AddHttpClientInstrumentation()    // HttpClient metrics
        .AddRuntimeInstrumentation()       // .NET runtime metrics
        .AddMeter("MyApp.*")               // Custom meters
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://127.0.0.1:4318");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));

var app = builder.Build();
app.Run();
```

#### Combined Traces and Metrics

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("my-service")
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = "development"
    });

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("my-service"))
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://127.0.0.1:4318");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://127.0.0.1:4318");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));

var app = builder.Build();
app.Run();
```

**Required NuGet Packages:**
```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.Runtime
```

### Python Configuration

#### Traces Setup

```python
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.sdk.resources import Resource
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter

# Configure resource attributes
resource = Resource.create({
    "service.name": "my-python-service",
    "service.version": "1.0.0",
    "deployment.environment": "development"
})

# Set up tracer provider
trace.set_tracer_provider(TracerProvider(resource=resource))
tracer = trace.get_tracer(__name__)

# Configure OTLP exporter
otlp_exporter = OTLPSpanExporter(
    endpoint="http://127.0.0.1:4318/v1/traces",
    headers={}
)

# Add batch processor
trace.get_tracer_provider().add_span_processor(
    BatchSpanProcessor(otlp_exporter)
)

# Use the tracer
with tracer.start_as_current_span("my-operation") as span:
    span.set_attribute("custom.attribute", "value")
    # Your application code
```

#### Metrics Setup

```python
from opentelemetry import metrics
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter

# Configure resource
resource = Resource.create({
    "service.name": "my-python-service",
    "deployment.environment": "development"
})

# Configure OTLP metric exporter
otlp_exporter = OTLPMetricExporter(
    endpoint="http://127.0.0.1:4318/v1/metrics",
    headers={}
)

# Set up metric reader and provider
reader = PeriodicExportingMetricReader(otlp_exporter, export_interval_millis=5000)
provider = MeterProvider(resource=resource, metric_readers=[reader])
metrics.set_meter_provider(provider)

# Create meters and instruments
meter = metrics.get_meter(__name__)
request_counter = meter.create_counter(
    "http.server.requests",
    description="Total HTTP requests"
)

# Record metrics
request_counter.add(1, {"http.method": "GET", "http.status_code": 200})
```

**Required Packages:**
```bash
pip install opentelemetry-api
pip install opentelemetry-sdk
pip install opentelemetry-exporter-otlp-proto-http
```

### Node.js / JavaScript Configuration

#### Traces Setup

```javascript
const { NodeTracerProvider } = require('@opentelemetry/sdk-trace-node');
const { Resource } = require('@opentelemetry/resources');
const { SemanticResourceAttributes } = require('@opentelemetry/semantic-conventions');
const { OTLPTraceExporter } = require('@opentelemetry/exporter-trace-otlp-http');
const { BatchSpanProcessor } = require('@opentelemetry/sdk-trace-base');

// Configure resource
const resource = Resource.default().merge(
  new Resource({
    [SemanticResourceAttributes.SERVICE_NAME]: 'my-node-service',
    [SemanticResourceAttributes.SERVICE_VERSION]: '1.0.0',
  })
);

// Create provider
const provider = new NodeTracerProvider({ resource });

// Configure OTLP exporter
const exporter = new OTLPTraceExporter({
  url: 'http://127.0.0.1:4318/v1/traces',
  headers: {},
});

// Add processor
provider.addSpanProcessor(new BatchSpanProcessor(exporter));
provider.register();

// Use tracer
const tracer = provider.getTracer('my-app');
const span = tracer.startSpan('operation-name');
span.setAttribute('custom.key', 'value');
span.end();
```

#### Metrics Setup

```javascript
const { MeterProvider, PeriodicExportingMetricReader } = require('@opentelemetry/sdk-metrics');
const { Resource } = require('@opentelemetry/resources');
const { SemanticResourceAttributes } = require('@opentelemetry/semantic-conventions');
const { OTLPMetricExporter } = require('@opentelemetry/exporter-metrics-otlp-http');

// Configure resource
const resource = new Resource({
  [SemanticResourceAttributes.SERVICE_NAME]: 'my-node-service',
});

// Configure OTLP exporter
const exporter = new OTLPMetricExporter({
  url: 'http://127.0.0.1:4318/v1/metrics',
  headers: {},
});

// Create meter provider
const metricReader = new PeriodicExportingMetricReader({
  exporter: exporter,
  exportIntervalMillis: 5000,
});

const meterProvider = new MeterProvider({
  resource: resource,
  readers: [metricReader],
});

// Create meter and instruments
const meter = meterProvider.getMeter('my-app');
const counter = meter.createCounter('http_requests_total');

// Record metrics
counter.add(1, { method: 'GET', status: 200 });
```

**Required Packages:**
```bash
npm install @opentelemetry/api
npm install @opentelemetry/sdk-node
npm install @opentelemetry/sdk-trace-node
npm install @opentelemetry/sdk-metrics
npm install @opentelemetry/exporter-trace-otlp-http
npm install @opentelemetry/exporter-metrics-otlp-http
npm install @opentelemetry/resources
npm install @opentelemetry/semantic-conventions
```

### Java / Spring Boot Configuration

#### application.properties

```properties
# OTLP Exporter Configuration
otel.exporter.otlp.endpoint=http://127.0.0.1:4318
otel.exporter.otlp.protocol=http/protobuf

# Service configuration
otel.service.name=my-java-service
otel.resource.attributes=service.version=1.0.0,deployment.environment=development

# Enable traces and metrics
otel.traces.exporter=otlp
otel.metrics.exporter=otlp
```

#### Programmatic Configuration

```java
import io.opentelemetry.api.OpenTelemetry;
import io.opentelemetry.api.common.Attributes;
import io.opentelemetry.exporter.otlp.http.trace.OtlpHttpSpanExporter;
import io.opentelemetry.exporter.otlp.http.metrics.OtlpHttpMetricExporter;
import io.opentelemetry.sdk.OpenTelemetrySdk;
import io.opentelemetry.sdk.resources.Resource;
import io.opentelemetry.sdk.trace.SdkTracerProvider;
import io.opentelemetry.sdk.trace.export.BatchSpanProcessor;
import io.opentelemetry.sdk.metrics.SdkMeterProvider;
import io.opentelemetry.sdk.metrics.export.PeriodicMetricReader;
import io.opentelemetry.semconv.resource.attributes.ResourceAttributes;

public class OpenTelemetryConfig {

    public static OpenTelemetry initOpenTelemetry() {
        // Configure resource
        Resource resource = Resource.getDefault().merge(
            Resource.create(Attributes.builder()
                .put(ResourceAttributes.SERVICE_NAME, "my-java-service")
                .put(ResourceAttributes.SERVICE_VERSION, "1.0.0")
                .build())
        );

        // Configure trace exporter
        OtlpHttpSpanExporter traceExporter = OtlpHttpSpanExporter.builder()
            .setEndpoint("http://127.0.0.1:4318/v1/traces")
            .build();

        SdkTracerProvider tracerProvider = SdkTracerProvider.builder()
            .setResource(resource)
            .addSpanProcessor(BatchSpanProcessor.builder(traceExporter).build())
            .build();

        // Configure metric exporter
        OtlpHttpMetricExporter metricExporter = OtlpHttpMetricExporter.builder()
            .setEndpoint("http://127.0.0.1:4318/v1/metrics")
            .build();

        SdkMeterProvider meterProvider = SdkMeterProvider.builder()
            .setResource(resource)
            .registerMetricReader(
                PeriodicMetricReader.builder(metricExporter)
                    .setInterval(Duration.ofSeconds(5))
                    .build())
            .build();

        return OpenTelemetrySdk.builder()
            .setTracerProvider(tracerProvider)
            .setMeterProvider(meterProvider)
            .buildAndRegisterGlobal();
    }
}
```

**Required Maven Dependencies:**
```xml
<dependency>
    <groupId>io.opentelemetry</groupId>
    <artifactId>opentelemetry-api</artifactId>
</dependency>
<dependency>
    <groupId>io.opentelemetry</groupId>
    <artifactId>opentelemetry-sdk</artifactId>
</dependency>
<dependency>
    <groupId>io.opentelemetry</groupId>
    <artifactId>opentelemetry-exporter-otlp</artifactId>
</dependency>
```

### Go Configuration

#### Traces Setup

```go
package main

import (
    "context"
    "log"

    "go.opentelemetry.io/otel"
    "go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp"
    "go.opentelemetry.io/otel/sdk/resource"
    sdktrace "go.opentelemetry.io/otel/sdk/trace"
    semconv "go.opentelemetry.io/otel/semconv/v1.17.0"
)

func initTracer() func(context.Context) error {
    // Configure resource
    res, err := resource.New(
        context.Background(),
        resource.WithAttributes(
            semconv.ServiceName("my-go-service"),
            semconv.ServiceVersion("1.0.0"),
        ),
    )
    if err != nil {
        log.Fatal(err)
    }

    // Configure OTLP HTTP exporter
    exporter, err := otlptracehttp.New(
        context.Background(),
        otlptracehttp.WithEndpoint("127.0.0.1:4318"),
        otlptracehttp.WithInsecure(),
        otlptracehttp.WithURLPath("/v1/traces"),
    )
    if err != nil {
        log.Fatal(err)
    }

    // Create tracer provider
    tp := sdktrace.NewTracerProvider(
        sdktrace.WithBatcher(exporter),
        sdktrace.WithResource(res),
    )
    otel.SetTracerProvider(tp)

    return tp.Shutdown
}
```

#### Metrics Setup

```go
package main

import (
    "context"
    "log"
    "time"

    "go.opentelemetry.io/otel"
    "go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetrichttp"
    "go.opentelemetry.io/otel/sdk/metric"
    "go.opentelemetry.io/otel/sdk/resource"
    semconv "go.opentelemetry.io/otel/semconv/v1.17.0"
)

func initMeter() func(context.Context) error {
    // Configure resource
    res, err := resource.New(
        context.Background(),
        resource.WithAttributes(
            semconv.ServiceName("my-go-service"),
        ),
    )
    if err != nil {
        log.Fatal(err)
    }

    // Configure OTLP HTTP exporter
    exporter, err := otlpmetrichttp.New(
        context.Background(),
        otlpmetrichttp.WithEndpoint("127.0.0.1:4318"),
        otlpmetrichttp.WithInsecure(),
        otlpmetrichttp.WithURLPath("/v1/metrics"),
    )
    if err != nil {
        log.Fatal(err)
    }

    // Create meter provider
    mp := metric.NewMeterProvider(
        metric.WithReader(metric.NewPeriodicReader(exporter,
            metric.WithInterval(5*time.Second))),
        metric.WithResource(res),
    )
    otel.SetMeterProvider(mp)

    return mp.Shutdown
}
```

**Required Packages:**
```bash
go get go.opentelemetry.io/otel
go get go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp
go get go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetrichttp
go get go.opentelemetry.io/otel/sdk
```

### Environment Variable Configuration

Most OpenTelemetry SDKs support configuration via environment variables:

```bash
# Endpoint configuration
export OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4318
export OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf

# Service identification
export OTEL_SERVICE_NAME=my-service
export OTEL_RESOURCE_ATTRIBUTES=service.version=1.0.0,deployment.environment=development

# Signal-specific endpoints (optional)
export OTEL_EXPORTER_OTLP_TRACES_ENDPOINT=http://127.0.0.1:4318/v1/traces
export OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://127.0.0.1:4318/v1/metrics
export OTEL_EXPORTER_OTLP_LOGS_ENDPOINT=http://127.0.0.1:4318/v1/logs
```

### Verification

After configuring your application, verify data is being sent:

1. **Check health status:**
   ```bash
   curl http://127.0.0.1:4318/healthz
   ```

2. **Check diagnostics (via CLI):**
   ```bash
   dotnet run --project opentelwatcher -- diagnose
   ```

3. **Check diagnostics (via API):**
   ```bash
   curl http://127.0.0.1:4318/api/diagnose | jq
   ```

4. **Verify files are created:**
   ```bash
   ls -lh telemetry-data/
   ```

5. **Inspect telemetry data:**
   ```bash
   # View traces
   cat telemetry-data/traces.*.ndjson | jq

   # View metrics
   cat telemetry-data/metrics.*.ndjson | jq
   ```

### Troubleshooting Endpoint Configuration

**Common Issues:**

1. **Wrong Protocol Error (HTTP 415)**
   - Ensure you're using `HttpProtobuf` not `Grpc` or `HttpJson`
   - Check Content-Type is `application/x-protobuf`

2. **Connection Refused**
   - Verify OpenTelWatcher is running: `curl http://127.0.0.1:4318/healthz`
   - Check firewall settings
   - Ensure correct port (default: 4318)

3. **No Data Appearing**
   - Check application logs for export errors
   - Verify endpoint URLs include protocol: `http://`
   - Check `/diagnose` endpoint for error messages
   - Ensure batching settings allow data to be sent (not waiting indefinitely)

4. **SDK-Specific Issues**
   - .NET: Verify `OtlpExportProtocol.HttpProtobuf` is used
   - Python: Ensure using `proto.http` exporters, not `proto.grpc`
   - Node.js: Use `@opentelemetry/exporter-*-otlp-http` packages
   - Java: Set `otel.exporter.otlp.protocol=http/protobuf`

## Building from Source

### Build All Projects

```bash
dotnet build
```

This builds:
- `opentelwatcher` - Main application
- `unit_tests` - Unit tests
- `e2e_tests` - End-to-end tests

### Build for Release

```bash
dotnet build -c Release
```

### Build Specific Project

```bash
dotnet build opentelwatcher/opentelwatcher.csproj
```

## Publishing and Deployment

OpenTelWatcher can be published as a **completely self-contained, single-file executable** with no additional files or dependencies required.

### Publish Single-File Executable

```bash
# Publish for current platform (auto-detects your OS)
dotnet publish opentelwatcher -c Release -r win-x64 --self-contained

# The executable will be in:
# artifacts/publish/opentelwatcher/release/opentelwatcher.exe (Windows)
# artifacts/publish/opentelwatcher/release/opentelwatcher (Linux/macOS)
```

### Platform-Specific Publishing

```bash
# Windows (x64)
dotnet publish opentelwatcher -c Release -r win-x64 --self-contained

# Linux (x64)
dotnet publish opentelwatcher -c Release -r linux-x64 --self-contained

# macOS (ARM64)
dotnet publish opentelwatcher -c Release -r osx-arm64 --self-contained

# macOS (x64 Intel)
dotnet publish opentelwatcher -c Release -r osx-x64 --self-contained
```

### What Gets Published

The publish process creates **only a single executable file** (~58 MB):
- ✅ **opentelwatcher.exe** (Windows) or **opentelwatcher** (Linux/macOS)
- ❌ No appsettings.json files
- ❌ No NLog.config files
- ❌ No web.config or other configuration files
- ❌ No .pdb debug symbols

**What's embedded in the executable:**
- .NET 10 runtime (self-contained)
- All application dependencies
- OTLP protobuf definitions
- Web dashboard HTML and CSS (embedded resources)
- Default configuration values

### Running the Published Executable

The executable is **completely portable** and requires no installation:

```bash
# Copy the single file anywhere
cp artifacts/publish/opentelwatcher/release/opentelwatcher.exe /path/to/deployment/

# Run it directly - no dependencies needed
cd /path/to/deployment
./opentelwatcher.exe start

# Or just run with full path
/path/to/deployment/opentelwatcher.exe start --port 4318 --output-dir ./data
```

### Configuration Without appsettings.json

Since no configuration files are published, use **CLI arguments** or **environment variables**:

**Via CLI arguments (recommended):**
```bash
./opentelwatcher.exe start \
  --port 4318 \
  --output-dir ./telemetry-data \
  --log-level Information
```

**Via environment variables:**
```bash
# Override port (default: 4318)
export ASPNETCORE_URLS="http://127.0.0.1:5000"

# Override output directory (default: ./telemetry-data)
export OpenTelWatcher__OutputDirectory="/var/log/telemetry"

# Override max file size (default: 100 MB)
export OpenTelWatcher__MaxFileSizeMB="200"

# Run the application
./opentelwatcher.exe start
```

**Windows PowerShell:**
```powershell
$env:OpenTelWatcher__OutputDirectory = "C:\telemetry-data"
$env:OpenTelWatcher__MaxFileSizeMB = "200"
.\opentelwatcher.exe start
```

The application will create the telemetry output directory automatically if it doesn't exist.

## Running Tests

### Run All Tests

```bash
dotnet test
```

Expected output:
```
Test Run Successful.
Total tests: 92
     Passed: 92
 Total time: 5.26 seconds (unit tests)

Test Run Successful.
Total tests: 38
     Passed: 38
 Total time: 6.54 seconds (e2e tests)
```

**Test Summary:**
- **Unit tests:** 168 tests covering configuration, serialization, services, CLI, utilities, and error detection
- **E2E tests:** 64 tests covering OTLP endpoints, status page, API endpoints, CLI integration, and error file behavior
- **Total:** 232 tests with 100% pass rate

**Note:** E2E tests respect the `OutputDirectory` setting from `appsettings.json` and will write test output to `opentelwatcher/telemetry-data/` during test execution.

### Run Unit Tests Only

```bash
dotnet test unit_tests/unit_tests.csproj
```

### Run E2E Tests Only

```bash
dotnet test e2e_tests/e2e_tests.csproj
```

### Run Tests with Detailed Output

```bash
dotnet test --verbosity detailed
```

## Configuration

OpenTelWatcher is configured via `appsettings.json`:

```json
{
  "OpenTelWatcher": {
    "OutputDirectory": "./telemetry-data",
    "MaxFileSizeMB": 100,
    "PrettyPrint": false,
    "MaxErrorHistorySize": 50,
    "MaxConsecutiveFileErrors": 10,
    "RequestTimeoutSeconds": 30
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `OutputDirectory` | string | `"./telemetry-data"` | Directory where NDJSON files are written |
| `MaxFileSizeMB` | int | `100` | Maximum file size in MB before rotation |
| `PrettyPrint` | bool | `false` | Enable JSON indentation for readability |
| `MaxErrorHistorySize` | int | `50` | Number of recent errors to retain (10-1000) |
| `MaxConsecutiveFileErrors` | int | `10` | Consecutive failures before health degradation (3-100) |
| `RequestTimeoutSeconds` | int | `30` | HTTP request timeout in seconds |

### Environment-Specific Configuration

Create `appsettings.Development.json` for development overrides:

```json
{
  "OpenTelWatcher": {
    "PrettyPrint": true
  }
}
```

Run with development settings:

```bash
dotnet run --environment Development
```

### Command-Line Configuration

Override settings via environment variables:

```bash
export OpenTelWatcher__OutputDirectory="/tmp/telemetry"
export OpenTelWatcher__PrettyPrint="true"
dotnet run
```

Windows (PowerShell):
```powershell
$env:OpenTelWatcher__OutputDirectory = "C:\temp\telemetry"
$env:OpenTelWatcher__PrettyPrint = "true"
dotnet run
```

## Usage

### Starting the Application

```bash
cd opentelwatcher
dotnet run
```

By default, OpenTelWatcher listens on `http://127.0.0.1:4318` (standard OTLP port).

**Verify the application is running:**
```bash
# The console output should show:
# ================================================================================
# OpenTelWatcher v0.5.0.0 - OTLP/HTTP Receiver
# https://github.com/mmc41/opentelwatcher
# ================================================================================
# Status Dashboard: http://127.0.0.1:4318/
# Output Directory: D:\repos\opentelwatcher_spec\watcher\telemetry-data
# OTLP Endpoints:
#   - Traces:  http://127.0.0.1:4318/v1/traces
#   - Logs:    http://127.0.0.1:4318/v1/logs
#   - Metrics: http://127.0.0.1:4318/v1/metrics
#
# WARNING: No authentication enabled. For local development use only.
# ================================================================================

# Test the health endpoint:
curl http://127.0.0.1:4318/healthz

# Expected response:
# {"status":"healthy","consecutiveErrors":0}
```

**To change the port:**

Option 1: Edit `opentelwatcher/Properties/launchSettings.json` and change `applicationUrl`

Option 2: Use command-line override:
```bash
dotnet run --urls "http://127.0.0.1:5000"
```

Option 3: Use environment variable:
```bash
export ASPNETCORE_URLS="http://127.0.0.1:5000"
dotnet run
```

### Available Endpoints

#### OTLP Endpoints (Telemetry Ingestion)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/v1/traces` | POST | Accept OTLP trace data (Protobuf) |
| `/v1/logs` | POST | Accept OTLP log data (Protobuf) |
| `/v1/metrics` | POST | Accept OTLP metric data (Protobuf) |

#### Monitoring Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/` | GET | Status dashboard (web UI) |
| `/healthz` | GET | Health check status (200=healthy, 503=degraded) |
| `/diagnose` | GET | Runtime diagnostics and statistics (legacy, deprecated) |

#### API Endpoints (Management & Control)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/version` | GET | Get application version and identity (for CLI detection) |
| `/api/diagnose` | GET | Get diagnostics information (health, files, configuration) |
| `/api/shutdown` | POST | Initiate graceful shutdown of the service |

#### Documentation Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/swagger` | GET | Interactive API documentation (Swagger UI) |
| `/openapi/v1.json` | GET | OpenAPI 3.0 specification (JSON) |

### Health Check

Check if the service is running and healthy:

```bash
curl http://127.0.0.1:4318/healthz
```

**Healthy Response (HTTP 200):**
```json
{
  "status": "healthy",
  "consecutiveErrors": 0
}
```

**Degraded Response (HTTP 503):**
```json
{
  "status": "degraded",
  "consecutiveErrors": 12
}
```

The service becomes degraded after consecutive file write failures (configured via `MaxConsecutiveFileErrors`).

### Diagnostics

Get detailed runtime information via the API:

```bash
curl http://127.0.0.1:4318/api/diagnose
```

**Response:**
```json
{
  "health": {
    "status": "healthy",
    "consecutiveErrors": 0,
    "recentErrors": []
  },
  "files": [
    {
      "path": "./telemetry-data/traces.20251108_143022.ndjson",
      "sizeBytes": 1048576,
      "lastModified": "2025-11-08T14:30:45.0000000Z"
    }
  ],
  "configuration": {
    "outputDirectory": "./telemetry-data"
  }
}
```

**Filter by signal type:**
```bash
curl "http://127.0.0.1:4318/api/diagnose?signal=traces"
```

**Note:** The legacy `/diagnose` endpoint is also available but deprecated. Use `/api/diagnose` for new integrations.

### Version Information

Get application version and identity (used by CLI for compatibility checking):

```bash
curl http://127.0.0.1:4318/api/version
```

**Response:**
```json
{
  "application": "OpenTelWatcher",
  "version": "1.0.0",
  "versionComponents": {
    "major": 1,
    "minor": 0,
    "patch": 0
  }
}
```

### Graceful Shutdown

Remotely shut down the service via API:

```bash
curl -X POST http://127.0.0.1:4318/api/shutdown
```

**Response:**
```json
{
  "message": "Shutdown initiated",
  "timestamp": "2025-11-08T14:30:45.0000000Z"
}
```

The service will complete the HTTP response and then gracefully shut down within 1ms.

### File Output Format

Telemetry is written to NDJSON (Newline Delimited JSON) files:

**File Naming:**
- Normal files: `{signal}.yyyyMMdd_HHmmss.ndjson` (UTC timestamp)
- Error files: `{signal}.yyyyMMdd_HHmmss.errors.ndjson` (same timestamp as normal file)
- Examples:
  - `traces.20251108_143022.ndjson`
  - `traces.20251108_143022.errors.ndjson`
  - `logs.20251108_150130.ndjson`
  - `logs.20251108_150130.errors.ndjson`

**File Content:**
Each line is a complete JSON object representing one OTLP export request (protobuf converted to JSON):

```bash
head -n 1 telemetry-data/traces.20251108_143022.ndjson | jq
```

The JSON preserves the complete OTLP data structure including:
- **Traces:** Resource attributes, instrumentation scopes, spans with attributes, events, and links
- **Logs:** Resource attributes, log records with severity, body, and attributes
- **Metrics:** Resource attributes, metric instruments (gauges, counters, histograms) with data points

### File Rotation

Files automatically rotate when they exceed `MaxFileSizeMB`:

```
telemetry-data/
├── traces.20251108_143022.ndjson         (100 MB - rotated)
├── traces.20251108_143022.errors.ndjson  (15 MB - rotated, contains only errors)
├── traces.20251108_150145.ndjson         (45 MB - current)
├── traces.20251108_150145.errors.ndjson  (8 MB - current, contains only errors)
├── logs.20251108_143022.ndjson           (30 MB - current)
├── logs.20251108_143022.errors.ndjson    (5 MB - current, contains only errors)
```

Error files share the same rotation settings as normal files and rotate independently based on their own size.

### Analyzing Telemetry Data

**Using jq (JSON processor):**

```bash
# Count total traces
wc -l telemetry-data/traces.*.ndjson

# Count error traces only
wc -l telemetry-data/traces.*.errors.ndjson

# View first trace (pretty-printed)
head -n 1 telemetry-data/traces.*.ndjson | jq

# View first error trace
head -n 1 telemetry-data/traces.*.errors.ndjson | jq

# Search for specific data
grep "user-service" telemetry-data/traces.*.ndjson

# Find all errors with specific exception type
grep "InvalidOperationException" telemetry-data/*.errors.ndjson
```

**Using Claude Code or AI tools:**

OpenTelWatcher's NDJSON format is optimized for AI/LLM consumption. Tools like Claude Code can:
- Read and analyze trace patterns
- Identify performance bottlenecks
- Suggest optimizations based on telemetry data
- Generate test scenarios from production traces

## Troubleshooting

### Cannot Connect / No Response from Endpoints

**Symptom:** `curl http://127.0.0.1:4318/healthz` returns "connection refused" or no response

**Cause:** Application may be running on a different port or not running at all

**Solution:**

1. **Check if the application is running (via CLI diagnose command):**
   ```bash
   dotnet run --project opentelwatcher -- diagnose
   ```

   This will show diagnostic information if an instance is running, or an error message if not.

2. **Check the startup banner in console output:**
   ```bash
   # Look for:
   # ================================================================================
   # OpenTelWatcher v1.0.0 - OTLP/HTTP Receiver
   # https://github.com/mmc41/opentelwatcher
   # ================================================================================
   # Status Dashboard: http://127.0.0.1:4318/
   ```

3. **Verify which port is actually in use:**
   ```bash
   # Windows (PowerShell)
   netstat -ano | findstr :4318

   # Linux/macOS
   lsof -i :4318
   # or
   ss -tlnp | grep 4318
   ```

4. **Check launchSettings.json:**
   ```bash
   cat opentelwatcher/Properties/launchSettings.json
   # Look for "applicationUrl" value
   ```

5. **Try the actual port shown in console output:**
   ```bash
   # If console shows "Status Dashboard: http://127.0.0.1:5011/"
   curl http://127.0.0.1:5011/healthz
   ```

6. **Force the port explicitly:**
   ```bash
   dotnet run --urls "http://127.0.0.1:4318"
   ```

### Port Already in Use / Instance Already Running

**Modern Error (with pre-flight check):**
```
Error: Instance already running on port 4318.
  Application: OpenTelWatcher
  Version:     0.5.0

Use 'opentelwatcher stop' to stop the running instance first.
```

This friendly error appears when the pre-flight check detects an instance already running.

**Legacy Error (if pre-flight check fails):**
```
System.IO.IOException: Failed to bind to address http://127.0.0.1:4318: address already in use.
```

**Solution:**
1. Stop the running instance:
   ```bash
   dotnet run --project opentelwatcher -- stop
   ```
2. Or use a different port:
   ```bash
   dotnet run --project opentelwatcher -- start --port 5000
   ```
3. Check for orphaned processes:
   ```bash
   # Windows
   netstat -ano | findstr :4318
   taskkill /PID <process_id> /F

   # Linux/macOS
   lsof -i :4318
   kill <process_id>
   ```

### Daemon Mode Issues

**Problem: Daemon fails to start**

If `opentelwatcher start --daemon` reports startup failure:
```
Error: Watcher failed to start (no response after 10 seconds)
Child process exited unexpectedly with code: -532462766
Tip: Run without --daemon to see detailed output and error messages.
```

**Solution:**
1. Run without `--daemon` to see full error output:
   ```bash
   dotnet run --project opentelwatcher -- start
   ```
2. Check NLog configuration and log files in `./artifacts/logs/`
3. Verify output directory exists and is writable
4. Check for configuration errors in `appsettings.json`

**Problem: Can't find daemon process**

Check the PID file:
```bash
cat opentelopentelwatcher.pid
# Shows process ID of daemon

# Check if process is running (Linux/macOS)
ps -p $(cat opentelopentelwatcher.pid)

# Check if process is running (Windows)
tasklist /FI "PID eq $(cat opentelopentelwatcher.pid)"
```

**Problem: Orphaned PID file**

If `opentelwatcher.pid` exists but process isn't running:
```bash
# Remove stale PID file
rm opentelopentelwatcher.pid

# Then start normally
dotnet run --project opentelwatcher -- start --daemon
```

### Output Directory Not Writable

**Error:**
```
Configuration error: OutputDirectory cannot be null or whitespace
```

**Solution:**
1. Ensure the directory exists and is writable:
   ```bash
   mkdir -p ./telemetry-data
   chmod 755 ./telemetry-data
   ```
2. Or configure a different directory in `appsettings.json`

### Health Status Degraded

**Symptom:** `/healthz` returns HTTP 503 with status "degraded"

**Cause:** File write operations failed consecutively (exceeds `MaxConsecutiveFileErrors`)

**Solution:**
1. Check diagnostics for error details:
   ```bash
   curl http://127.0.0.1:4318/diagnose | jq '.health.recentErrors'
   ```
2. Common causes:
   - Disk full: Free up disk space
   - Permission denied: Fix directory permissions
   - File system issues: Check mount points

3. Restart the service after fixing the underlying issue

### No Data Being Written

**Checklist:**
1. Verify service is running: `curl http://127.0.0.1:4318/healthz`
2. Check diagnostics (via CLI): `dotnet run --project opentelwatcher -- diagnose`
3. Check diagnostics (via API): `curl http://127.0.0.1:4318/api/diagnose`
4. Check your application's OTLP configuration:
   - Endpoint: `http://127.0.0.1:4318`
   - Protocol: `HttpProtobuf` (not gRPC or JSON)
5. Verify output directory exists and is writable
6. Check application logs for export failures

### Testing Without Application

Send a test request using curl with empty OTLP protobuf:

```bash
# Send minimal empty trace request
printf '\x0a\x00' | curl -X POST http://127.0.0.1:4318/v1/traces \
  -H "Content-Type: application/x-protobuf" \
  --data-binary @-
```

This sends a minimal valid OTLP protobuf message (empty ExportTraceServiceRequest).

Check if file was created:
```bash
ls -lh telemetry-data/
cat telemetry-data/traces.*.ndjson
# Output: {"resourceSpans":[]}
```

For more realistic testing, use the OpenTelemetry SDK examples provided in the [Configuring OpenTelemetry Endpoints](#configuring-opentelemetry-endpoints) section.

## Limitations

OpenTelWatcher is designed for **development and testing only**. It does not support:

- **gRPC protocol** - HTTP/Protobuf only
- **JSON encoding** - Protobuf only
- **Compression** - Uncompressed payloads only
- **Authentication** - No security features
- **Data forwarding** - Files only, no export to other systems
- **Dashboards** - No built-in visualization
- **Production workloads** - Not optimized for high-throughput scenarios

For production observability, use:
- [OpenTelemetry Collector](https://opentelemetry.io/docs/collector/)
- Commercial observability platforms (Datadog, New Relic, etc.)

## Contributing

This project follows strict Test-Driven Development (TDD):

1. Write tests first (they must fail initially)
2. Implement functionality (tests must pass)
3. All code must have zero compiler warnings
4. 80% code coverage goal

## License

[MIT License](LICENSE.md)

## Support

For issues, questions, or feature requests, please [open an issue](repository-url/issues).
