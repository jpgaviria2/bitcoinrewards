using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class DisplayRewardsViewTests
{
    [Fact]
    public void WaitingDisplay_ReplacesIdleUi_WhenScannerInjectsReadyCard()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewPath = Path.Combine(
            repositoryRoot,
            "Plugins",
            "BTCPayServer.Plugins.BitcoinRewards",
            "Views",
            "UIBitcoinRewards",
            "DisplayRewards.cshtml");
        var view = File.ReadAllText(viewPath);

        Assert.Contains("new MutationObserver(applyCustomerReadyState)", view);
        Assert.Contains("/ready to reward/i", view);
        Assert.Contains("/next square payment/i", view);
        Assert.Contains("waitingDisplay.classList.add('customer-ready')", view);
        Assert.Contains(".customer-ready > :not(.customer-ready-card)", view);
        Assert.Contains("readyCard.scrollIntoView({ block: 'center'", view);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "btcpayserver-shopify-plugin.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Bitcoin Rewards repository root.");
    }
}
