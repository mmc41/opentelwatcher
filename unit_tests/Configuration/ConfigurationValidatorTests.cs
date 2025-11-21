using FluentAssertions;
using OpenTelWatcher.Configuration;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Configuration;

public class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_WithValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestConstants.FileNames.DefaultOutputDirectory,
            MaxFileSizeMB = TestConstants.DefaultConfig.MaxFileSizeMB,
            MaxErrorHistorySize = TestConstants.DefaultConfig.MaxErrorHistorySize,
            MaxConsecutiveFileErrors = TestConstants.DefaultConfig.MaxConsecutiveFileErrors,
            RequestTimeoutSeconds = 30
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(5)]     // Below minimum (10)
    public void Validate_WithInvalidMaxErrorHistorySize_ReturnsFailure(int invalidSize)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxErrorHistorySize = invalidSize
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("MaxErrorHistorySize");
    }

    [Theory]
    [InlineData(2)]    // Below minimum (3)
    public void Validate_WithInvalidMaxConsecutiveFileErrors_ReturnsFailure(int invalidValue)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = invalidValue
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("MaxConsecutiveFileErrors");
    }

    [Theory]
    [InlineData(0)]
    public void Validate_WithNonPositiveMaxFileSizeMB_ReturnsFailure(int invalidSize)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxFileSizeMB = invalidSize
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("MaxFileSizeMB");
    }

    [Theory]
    [InlineData(0)]
    public void Validate_WithNonPositiveRequestTimeoutSeconds_ReturnsFailure(int invalidTimeout)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            RequestTimeoutSeconds = invalidTimeout
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("RequestTimeoutSeconds");
    }

    [Fact]
    public void Validate_WithMultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxErrorHistorySize = 5,          // Too small
            MaxConsecutiveFileErrors = 101,   // Too large
            MaxFileSizeMB = 0                 // Invalid
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().Contain(e => e.Contains("MaxErrorHistorySize"));
        result.Errors.Should().Contain(e => e.Contains("MaxConsecutiveFileErrors"));
        result.Errors.Should().Contain(e => e.Contains("MaxFileSizeMB"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidOutputDirectory_ReturnsFailure(string? invalidDirectory)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = invalidDirectory!
        };

        // Act
        var result = ConfigurationValidator.Validate(options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("OutputDirectory");
    }
}
