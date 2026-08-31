namespace Counter.UnitTests.Ai;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Ai;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Working out what a part costs across every kind of customer.
/// </summary>
/// <remarks>
/// The question this answers -- "which customer gets the best price on this?"
/// -- was asked by hand on a running deployment and answered with a refusal,
/// while the screen beside it was showing a contract price at that moment. This
/// code was written to make that question answerable, and then had no test.
///
/// The shape of the answer matters as much as the arithmetic. Prices in a
/// MultiValue ERP belong to a price *class*, and customers are assigned to one,
/// so "which customer is cheapest" is really "which class is cheapest, and who
/// is in it". An answer naming a customer without saying that would be true and
/// useless: the next customer in the same class pays exactly the same.
///
/// Everything below is the real code driven by a fake ERP holding records in
/// their stored form, so the parsing is exercised rather than stepped over.
/// </remarks>
public sealed class PriceComparisonTests
{
    /// <summary>A date on which the terms below are in force.</summary>
    private static readonly DateTimeOffset Today =
        new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock that does not move.</summary>
    /// <param name="now">The moment it reports.</param>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Three classes buying one breaker on three different footings.</summary>
    /// <returns>The ERP's contents.</returns>
    private static Dictionary<string, Dictionary<string, string>> AWorldWithThreeClasses() =>
        new()
        {
            ["PRODUCT"] = new() { ["E-BRK00008"] = StoredRecords.Product("15A AFCI Breaker", "BRK", "409.26") },
            ["CUSTOMER"] = new()
            {
                ["C-10000"] = StoredRecords.Customer("Front Range Electric", "A1"),
                ["C-10001"] = StoredRecords.Customer("Mesa Verde Contracting", "A1"),
                ["C-10002"] = StoredRecords.Customer("Wazee Brothers Electric", "B2"),
                ["C-10003"] = StoredRecords.Customer("Cherry Creek Supply", "D1"),
            },
            ["PRICING"] = new()
            {
                ["A1*BRK"] = StoredRecords.Terms("0.70"),
                ["B2*BRK"] = StoredRecords.Terms("0.85"),

                // D1 has no terms for breakers at all, so it pays list.
            },
        };

    /// <summary>Build the comparison over a small, complete world.</summary>
    /// <param name="records">The ERP's contents.</param>
    /// <returns>The comparison, with its catalogue already built.</returns>
    private static async Task<PriceComparison> BuildAsync(
        Dictionary<string, Dictionary<string, string>> records)
    {
        InMemoryErp erp = new(records);

        CatalogueProjection catalogue = new(erp, NullLogger<CatalogueProjection>.Instance);
        await catalogue.BuildAsync(CancellationToken.None);

        return new PriceComparison(
            catalogue,
            new CustomerDirectory(erp, NullLogger<CustomerDirectory>.Instance),
            new PricingReader(erp),
            new FixedClock(Today));
    }

    [Fact]
    public async Task Classes_are_returned_cheapest_first()
    {
        // The order is the answer. A comparison the reader has to sort is one
        // the reader can sort wrongly.
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        PriceSpread? spread =
            await comparison.AcrossCustomersAsync("E-BRK00008", CancellationToken.None);

        Assert.NotNull(spread);
        Assert.Equal(["A1", "B2", "D1"], spread.Classes.Select(entry => entry.PriceClass));
    }

    [Fact]
    public async Task Each_class_is_priced_from_its_own_terms()
    {
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        PriceSpread spread =
            (await comparison.AcrossCustomersAsync("E-BRK00008", CancellationToken.None))!;

        Assert.Equal(286.48m, Math.Round(spread.Classes[0].NetPrice, 2));
        Assert.Equal(347.87m, Math.Round(spread.Classes[1].NetPrice, 2));
    }

    [Fact]
    public async Task A_class_with_no_terms_pays_list_and_says_so()
    {
        // Silence here would read as "no discount found, something is broken".
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        PriceSpread spread =
            (await comparison.AcrossCustomersAsync("E-BRK00008", CancellationToken.None))!;

        ClassPrice dearest = spread.Classes[^1];

        Assert.Equal("D1", dearest.PriceClass);
        Assert.Equal(409.26m, dearest.NetPrice);
        Assert.Null(dearest.Multiplier);
        Assert.Contains("list", dearest.Terms, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Each_class_names_customers_actually_in_it()
    {
        // Because "which customer is cheapest" is really "which class, and who
        // is in it" -- an answer with no names cannot be acted on, and an
        // answer with the wrong names sends somebody to the wrong account.
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        PriceSpread spread =
            (await comparison.AcrossCustomersAsync("E-BRK00008", CancellationToken.None))!;

        ClassPrice cheapest = spread.Classes[0];

        Assert.Equal(2, cheapest.CustomerCount);
        Assert.Contains(cheapest.Examples, name => name.Contains("C-10000", StringComparison.Ordinal));
        Assert.DoesNotContain(cheapest.Examples, name => name.Contains("C-10002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_part_is_null_rather_than_an_empty_comparison()
    {
        // An empty spread reads as "nobody gets a discount on this", which is a
        // claim about a part that does not exist.
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        Assert.Null(await comparison.AcrossCustomersAsync("NOT-A-PART", CancellationToken.None));
    }

    [Fact]
    public async Task One_customer_is_quoted_from_their_class()
    {
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        CustomerQuote? quote = await comparison.ForCustomerAsync(
            "E-BRK00008", "C-10001", CancellationToken.None);

        Assert.NotNull(quote);
        Assert.Equal("A1", quote.PriceClass);
        Assert.Equal(409.26m, quote.ListPrice);
        Assert.Equal(286.48m, Math.Round(quote.NetPrice, 2));
    }

    [Fact]
    public async Task An_unknown_account_is_null_rather_than_a_list_price_quote()
    {
        // Quoting list for an account nobody has would answer confidently about
        // a customer that does not exist.
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        Assert.Null(await comparison.ForCustomerAsync(
            "E-BRK00008", "C-99999", CancellationToken.None));
    }

    [Fact]
    public async Task The_spread_reports_how_much_of_the_account_file_it_read()
    {
        // It scans a capped number of accounts inside a question somebody is
        // waiting on, so the answer must be able to say it saw a subset rather
        // than quietly comparing one and calling it the whole file.
        PriceComparison comparison = await BuildAsync(AWorldWithThreeClasses());

        PriceSpread spread =
            (await comparison.AcrossCustomersAsync("E-BRK00008", CancellationToken.None))!;

        Assert.Equal(4, spread.AccountsScanned);
        Assert.Equal(4, spread.AccountsTotal);
    }
}
