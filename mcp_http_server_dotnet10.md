# Building an HTTP-Based MCP Server in ASP.NET Core WebAPI (.NET 10, C#)

## Overview

The Model Context Protocol (MCP) allows applications to expose tools and
contextual data to AI models through a standard JSON-RPC interface. MCP
supports **streamable HTTP** using **Server-Sent Events (SSE)**,
enabling real-time event-driven interactions.

This document describes how to integrate an **MCP server into an
existing ASP.NET Core WebAPI (.NET 10)** application, running on the
**same port** via a dedicated route, without authentication.

------------------------------------------------------------------------

## 1. Install MCP SDK Packages

``` bash
dotnet add package ModelContextProtocol --prerelease
dotnet add package ModelContextProtocol.AspNetCore --prerelease
```

------------------------------------------------------------------------

## 2. Register MCP Services in `Program.cs`

``` csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
```

------------------------------------------------------------------------

## 3. Map MCP Endpoints

``` csharp
var app = builder.Build();
app.MapMcp(); // exposes /sse (GET) and /message (POST)
```

To customize the route:

``` csharp
app.MapMcp("mcp"); // exposes /mcp for SSE + POST messaging
```

------------------------------------------------------------------------

## 4. Expose WebAPI Endpoints as MCP Tools

Annotate controllers and actions:

``` csharp
using ModelContextProtocol.Server;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
[McpServerToolType]
public class WeatherForecastController : ControllerBase
{
    [HttpGet]
    [McpServerTool]
    public IEnumerable<WeatherForecast> Get()
    {
        return Enumerable.Range(1, 5).Select(i => new WeatherForecast {
            Date = DateTime.Now.AddDays(i),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = "Sample"
        });
    }
}
```

------------------------------------------------------------------------

## 5. Ensure MCP Endpoints Require No Authentication

If your API uses authentication globally:

``` csharp
[AllowAnonymous]
[McpServerToolType]
public class WeatherForecastController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [McpServerTool]
    public IEnumerable<WeatherForecast> Get() { ... }
}
```

------------------------------------------------------------------------

## 6. How MCP Streamable HTTP Works

### SSE Connection

Client opens:

    GET /sse
    Accept: text/event-stream

Server: - Returns `Content-Type: text/event-stream` - Provides a session
ID - Keeps connection open

### Client Request

Client sends:

    POST /message?sessionId=xyz
    { "jsonrpc": "2.0", "method": "tools/list", "id": 1 }

Server: - Returns `202 Accepted` - Streams responses on the SSE channel

### Response Streaming

Each SSE event contains a JSON-RPC response:

    event: message
    data: { ... }

------------------------------------------------------------------------

## 7. Hosting Considerations

-   Enable long-lived connections (SSE requires no buffering or forced
    timeouts)
-   Ensure proxies/load balancers support `text/event-stream`
-   Configure CORS if needed (browser-based clients)

------------------------------------------------------------------------

## Summary

This guide shows how to:

-   Add the MCP server to an existing ASP.NET Core WebAPI (.NET 10)
-   Use HTTP+SSE transport
-   Expose controllers as MCP tools
-   Support unauthenticated MCP access
-   Stream results in compliance with MCP specifications

Your API becomes an AI-ready tool layer through a minimal and clean
integration.

------------------------------------------------------------------------
