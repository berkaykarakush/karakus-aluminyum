using KarakusAluminyum.Web.Models.Crm;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class QuoteRulesTests
{
    [Fact]
    public void Calculate_AppliesDiscountBeforeTax()
    {
        var items = new[]
        {
            new QuoteItem { Description = "Profil", Quantity = 2, UnitPrice = 100m },
            new QuoteItem { Description = "Aksesuar", Quantity = 1, UnitPrice = 50m }
        };
        var totals = QuoteRules.Calculate(items, 10m, 20m);
        Assert.Equal(250m, totals.Subtotal);
        Assert.Equal(25m, totals.DiscountAmount);
        Assert.Equal(45m, totals.TaxAmount);
        Assert.Equal(270m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_RoundsLineAmountsAwayFromZero()
    {
        var totals = QuoteRules.Calculate(new[] { new QuoteItem { Quantity = 1, UnitPrice = 1.005m } }, 0m, 0m);
        Assert.Equal(1.01m, totals.Subtotal);
        Assert.Equal(1.01m, totals.GrandTotal);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Sent, true)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Accepted, true)]
    [InlineData(QuoteStatus.Sent, QuoteStatus.Rejected, true)]
    [InlineData(QuoteStatus.Accepted, QuoteStatus.Converted, true)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Accepted, false)]
    [InlineData(QuoteStatus.Rejected, QuoteStatus.Accepted, false)]
    [InlineData(QuoteStatus.Converted, QuoteStatus.Sent, false)]
    public void CanTransition_EnforcesWorkflow(QuoteStatus current, QuoteStatus next, bool expected)
        => Assert.Equal(expected, QuoteRules.CanTransition(current, next));

    [Fact]
    public void Calculate_RejectsInvalidRates()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QuoteRules.Calculate(Array.Empty<QuoteItem>(), 101m, 20m));
}
