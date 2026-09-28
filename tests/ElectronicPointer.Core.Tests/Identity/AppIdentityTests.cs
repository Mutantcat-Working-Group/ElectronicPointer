using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Identity;

/// <summary>
/// The build stamps the version into the assembly, and <see cref="AppIdentity"/> reads it
/// back. These tests guard the contract the packaging scripts rely on: the version has to
/// carry a date stamp and never git build metadata, and the configuration directory has
/// to follow the same application id the installers write.
/// </summary>
public class AppIdentityTests
{
    [Fact]
    public void Version_CarriesADateStamp()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d{8}$"), AppIdentity.Version);
    }

    [Fact]
    public void Version_IsNotTheUnstampedFallback()
    {
        Assert.NotEqual("0.0.0-dev", AppIdentity.Version);
    }

    [Fact]
    public void Version_HasNoGitBuildMetadata()
    {
        Assert.DoesNotContain("+", AppIdentity.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationIds_FollowTheReverseDomainPrefix()
    {
        foreach (var id in new[]
                 {
                     AppIdentity.ApplicationId,
                 })
        {
            Assert.StartsWith(AppIdentity.OrganizationDomain + ".", id, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ApplicationIds_EndWithTheProductName()
    {
        Assert.EndsWith(AppIdentity.ProductName.ToLowerInvariant(), AppIdentity.ApplicationId, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayName_IsTheChineseName()
    {
        Assert.Equal(AppIdentity.ChineseName, AppIdentity.GetDisplayName());
        Assert.DoesNotContain("版", AppIdentity.GetDisplayName(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigDirectory_UsesTheConventionOfTheCurrentPlatform()
    {
        var name = AppIdentity.GetConfigDirectoryName();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal("Mutantcat", name.Split(Path.DirectorySeparatorChar)[0]);
            Assert.Equal(AppIdentity.ProductName, name.Split(Path.DirectorySeparatorChar)[1]);
        }
        else
        {
            Assert.Contains(AppIdentity.ApplicationId, name, StringComparison.Ordinal);
        }

        Assert.EndsWith(name, AppIdentity.GetConfigDirectory(), StringComparison.Ordinal);
    }
}
