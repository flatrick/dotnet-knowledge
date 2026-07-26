namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Unit")]
public sealed class ToolchainInventoryTests
{
    [TestMethod]
    public void FromListingsResolvesHighestPatchInExactSdkBand()
    {
        var inventory = ToolchainInventory.FromListings(
            """
            5.0.408 [C:\Program Files\dotnet\sdk]
            7.0.410 [C:\Program Files\dotnet\sdk]
            7.0.412 [C:\Program Files\dotnet\sdk]
            10.0.302 [C:\Program Files\dotnet\sdk]
            """,
            """
            Microsoft.NETCore.App 5.0.17 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.NETCore.App 7.0.20 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.NETCore.App 10.0.10 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            """);

        var sdk = inventory.ResolveSdk("7.0");

        Assert.AreEqual(new Version(7, 0, 412), sdk.Version);
        Assert.AreEqual(@"C:\Program Files\dotnet\sdk", sdk.Directory);
    }

    [TestMethod]
    public void FromListingsReportsInstalledBandsWhenRequestedSdkBandIsAbsent()
    {
        var inventory = ToolchainInventory.FromListings(
            """
            5.0.408 [C:\Program Files\dotnet\sdk]
            7.0.410 [C:\Program Files\dotnet\sdk]
            10.0.302 [C:\Program Files\dotnet\sdk]
            """,
            "");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => inventory.ResolveSdk("6.0"));

        Assert.AreEqual("Required .NET SDK band 6.0 is not installed. Installed bands: 5.0, 7.0, 10.0.", exception.Message);
    }

    [TestMethod]
    public void FromListingsReportsRuntimeByExactMajorMinorVersion()
    {
        var inventory = ToolchainInventory.FromListings(
            "10.0.302 [C:\\Program Files\\dotnet\\sdk]",
            """
            Microsoft.NETCore.App 5.0.17 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.NETCore.App 7.0.20 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.NETCore.App 10.0.10 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            """);

        Assert.IsTrue(inventory.HasRuntime("7.0"));
        Assert.IsFalse(inventory.HasRuntime("6.0"));
    }

    [TestMethod]
    public void FromListingsRejectsMalformedNonblankSdkLine()
    {
        var exception = Assert.ThrowsExactly<FormatException>(() =>
            ToolchainInventory.FromListings("not an SDK listing", ""));

        Assert.AreEqual("Malformed .NET SDK listing line: not an SDK listing", exception.Message);
    }

    [TestMethod]
    [DataRow("7.0 [C:\\Program Files\\dotnet\\sdk]", "", "Malformed .NET SDK listing line: 7.0 [C:\\Program Files\\dotnet\\sdk]")]
    [DataRow("", "Microsoft.NETCore.App 7.0 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]", "Malformed .NET runtime listing line: Microsoft.NETCore.App 7.0 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]")]
    public void FromListingsRejectsIncompleteSdkAndRuntimeVersions(string sdkListing, string runtimeListing, string expectedMessage)
    {
        var exception = Assert.ThrowsExactly<FormatException>(() => ToolchainInventory.FromListings(sdkListing, runtimeListing));

        Assert.AreEqual(expectedMessage, exception.Message);
    }
}
