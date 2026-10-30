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
    [InlineData(QuoteStatus.Sent, QuoteStatus.Expired, true)]
    [InlineData(QuoteStatus.Accepted, QuoteStatus.Converted, true)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Draft, false)]
    [InlineData(QuoteStatus.Expired, QuoteStatus.Sent, false)]
    [InlineData(QuoteStatus.Draft, QuoteStatus.Accepted, false)]
    [InlineData(QuoteStatus.Rejected, QuoteStatus.Accepted, false)]
    [InlineData(QuoteStatus.Converted, QuoteStatus.Sent, false)]
    public void CanTransition_EnforcesWorkflow(QuoteStatus current, QuoteStatus next, bool expected)
        => Assert.Equal(expected, QuoteRules.CanTransition(current, next));

    [Theory]
    [InlineData(101, 20)]
    [InlineData(-1, 20)]
    [InlineData(20, 101)]
    [InlineData(20, -1)]
    public void Calculate_RejectsInvalidRates(int discountRate, int taxRate)
        => Assert.Throws<ArgumentOutOfRangeException>(() => QuoteRules.Calculate(Array.Empty<QuoteItem>(), discountRate, taxRate));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    public void Calculate_AcceptsBoundaryRates(int discountRate, int taxRate)
        => Assert.Equal(0m, QuoteRules.Calculate(Array.Empty<QuoteItem>(), discountRate, taxRate).GrandTotal);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, -1)]
    public void Calculate_RejectsInvalidItems(int quantity, int unitPrice)
        => Assert.Throws<ArgumentException>(() => QuoteRules.Calculate(new[] { new QuoteItem { Quantity = quantity, UnitPrice = unitPrice } }, 0m, 0m));

    [Fact]
    public void Calculate_RoundsDiscountAndTaxToTwoDecimals()
    {
        // İskonto 0,5025 → 0,50; KDV 9,55 × %7 = 0,6685 → 0,67
        var totals = QuoteRules.Calculate(new[] { new QuoteItem { Quantity = 1, UnitPrice = 10.05m } }, 5m, 7m);
        Assert.Equal(0.50m, totals.DiscountAmount);
        Assert.Equal(0.67m, totals.TaxAmount);
        Assert.Equal(10.22m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_VatIncluded_DoesNotAddTax_AndExtractsItFromTotal()
    {
        var totals = QuoteRules.Calculate(new[] { new QuoteItem { Quantity = 1, UnitPrice = 950000m } }, 0m, 20m, VatMode.Included);
        Assert.Equal(950000m, totals.GrandTotal);
        Assert.Equal(158333.33m, totals.TaxAmount); // 950000 - 950000/1,20
    }

    [Fact]
    public void Calculate_VatNone_HasNoTax()
    {
        var totals = QuoteRules.Calculate(new[] { new QuoteItem { Quantity = 1, UnitPrice = 100m } }, 10m, 20m, VatMode.None);
        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(90m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_SetsLineTotals()
    {
        var line = new QuoteItem { Quantity = 3, UnitPrice = 12.5m };
        QuoteRules.Calculate(new[] { line }, 0m, 0m);
        Assert.Equal(37.5m, line.LineTotal);
    }
}
