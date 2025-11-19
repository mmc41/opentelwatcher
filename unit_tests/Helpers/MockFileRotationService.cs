using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of IFileRotationService for testing.
/// </summary>
public class MockFileRotationService : IFileRotationService
{
    private readonly string _baseDirectory;
    private readonly Dictionary<string, string> _currentPaths = new();

    public bool ShouldRotateResult { get; set; } = false;
    public int ShouldRotateCallCount { get; private set; }
    public int RotateFileCallCount { get; private set; }

    public MockFileRotationService(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public bool ShouldRotate(string filePath, int maxFileSizeMB)
    {
        ShouldRotateCallCount++;
        return ShouldRotateResult;
    }

    public string GenerateNewFilePath(string outputDirectory, SignalType signal)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        return Path.Combine(outputDirectory, $"{signal.ToLowerString()}.{timestamp}.ndjson");
    }

    public string GetOrCreateFilePath(string outputDirectory, SignalType signal)
    {
        var signalKey = signal.ToLowerString();
        if (!_currentPaths.TryGetValue(signalKey, out var path))
        {
            path = GenerateNewFilePath(outputDirectory, signal);
            _currentPaths[signalKey] = path;
        }
        return path;
    }

    public string RotateFile(string outputDirectory, SignalType signal)
    {
        RotateFileCallCount++;
        var signalKey = signal.ToLowerString();
        var newPath = GenerateNewFilePath(outputDirectory, signal);
        _currentPaths[signalKey] = newPath;
        return newPath;
    }
}
