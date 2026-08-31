namespace Counter.UnitTests.Erp;

using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Finding parts where some of the stock is already somebody else's.
/// </summary>
/// <remarks>
/// The distinction this whole application is built around -- free to sell is on
/// hand minus committed -- is invisible on most of the catalogue, because most
/// parts have nothing committed anywhere. Somebody picking a part at random
/// reads twelve identical zeroes in a committed column and reasonably concludes
/// the column is decoration.
///
/// So the empty screen offers a few parts where the difference is real. The
/// claim being made is that these came from the account rather than a fixture,
/// which puts weight on two behaviours: it must offer only parts that genuinely
/// have stock committed, and when nothing does it must offer nothing rather than
/// reach for something. Both are claims about honesty, and neither had a test.
///
/// This has already been the cause of one production failure -- taking its
/// readers as constructor arguments made a singleton hold per-request services
/// and the application would not start -- so it is exercised here through a real
/// container, the way it is actually built.
/// </remarks>
public sealed class PromisedStockTests
{
    /// <summary>Build the thing under test over a small, complete world.</summary>
    /// <param name="records">The ERP's contents.</param>
    /// <returns>The scanner, and the ERP it reads through.</returns>
    private static (PromisedStock Stock, InMemoryErp Erp) Build(
        Dictionary<string, Dictionary<string, string>> records)
    {
        InMemoryErp erp = new(records);

        ServiceCollection services = new();
        services.AddSingleton<IErpReader>(erp);
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        services.AddScoped<CatalogueProjection>();
        services.AddScoped<AvailabilityReader>();

        ServiceProvider provider = services.BuildServiceProvider();

        return (new PromisedStock(provider.GetRequiredService<IServiceScopeFactory>()), erp);
    }

    /// <summary>Four parts, of which two have stock committed somewhere.</summary>
    /// <returns>The ERP's contents.</returns>
    private static Dictionary<string, Dictionary<string, string>> AMixedCatalogue() =>
        new()
        {
            ["PRODUCT"] = new()
            {
                ["E-BRK00001"] = StoredRecords.Product("15A Breaker", "BRK", "40.00"),
                ["E-BOX00002"] = StoredRecords.Product("4in Square Box", "BOX", "3.20"),
                ["E-WIR00003"] = StoredRecords.Product("12/2 Romex", "WIR", "180.00"),
                ["E-LTG00004"] = StoredRecords.Product("LED Troffer", "LTG", "95.00"),
            },
            ["INVENTORY"] = new()
            {
                // Nothing committed anywhere.
                ["E-BRK00001"] = StoredRecords.Inventory(
                    ["LKW", "GRE", "DEN"], [10, 20, 30], [0, 0, 0]),

                // Committed at the middle branch, which is the interesting case:
                // picking the first or the largest on-hand would name the wrong one.
                ["E-BOX00002"] = StoredRecords.Inventory(
                    ["LKW", "GRE", "DEN"], [5, 40, 100], [0, 12, 0]),

                // No inventory record at all for E-WIR00003.

                // Committed at two branches; the busier one is second.
                ["E-LTG00004"] = StoredRecords.Inventory(
                    ["LKW", "GRE", "DEN"], [50, 60, 70], [4, 30, 0]),
            },
        };

    /// <summary>A catalogue where nothing anywhere is committed.</summary>
    /// <returns>The ERP's contents.</returns>
    private static Dictionary<string, Dictionary<string, string>> AQuietCatalogue() =>
        new()
        {
            ["PRODUCT"] = new()
            {
                ["E-BRK00001"] = StoredRecords.Product("15A Breaker", "BRK", "40.00"),
                ["E-BOX00002"] = StoredRecords.Product("4in Square Box", "BOX", "3.20"),
            },
            ["INVENTORY"] = new()
            {
                ["E-BRK00001"] = StoredRecords.Inventory(["LKW", "DEN"], [10, 30], [0, 0]),
                ["E-BOX00002"] = StoredRecords.Inventory(["LKW", "DEN"], [5, 100], [0, 0]),
            },
        };

    [Fact]
    public async Task Only_parts_with_stock_actually_committed_are_offered()
    {
        // The claim on the screen is that these are real. A part with nothing
        // committed shown as an example of committed stock would be the exact
        // misunderstanding the panel exists to prevent.
        (PromisedStock stock, _) = Build(AMixedCatalogue());

        IReadOnlyList<PromisedPart> found = await stock.FindAsync(5, CancellationToken.None);

        Assert.Equal(2, found.Count);
        Assert.All(found, part => Assert.True(part.Committed > 0));
        Assert.DoesNotContain(found, part => part.PartNumber == "E-BRK00001");
    }

    [Fact]
    public async Task The_branch_named_is_the_one_holding_the_most()
    {
        // Not the first branch in the record, and not the one with the most on
        // hand. Getting this wrong points somebody at a branch whose figures
        // look entirely reasonable and are not the ones being described.
        (PromisedStock stock, _) = Build(AMixedCatalogue());

        IReadOnlyList<PromisedPart> found = await stock.FindAsync(5, CancellationToken.None);

        PromisedPart box = found.Single(part => part.PartNumber == "E-BOX00002");
        Assert.Equal("GRE", box.BranchCode);
        Assert.Equal(12, box.Committed);
        Assert.Equal(40, box.OnHand);

        PromisedPart lighting = found.Single(part => part.PartNumber == "E-LTG00004");
        Assert.Equal("GRE", lighting.BranchCode);
        Assert.Equal(30, lighting.Committed);
    }

    [Fact]
    public async Task Free_to_sell_is_what_is_left_after_the_commitment()
    {
        // The whole point of showing the example.
        (PromisedStock stock, _) = Build(AMixedCatalogue());

        IReadOnlyList<PromisedPart> found = await stock.FindAsync(5, CancellationToken.None);

        PromisedPart box = found.Single(part => part.PartNumber == "E-BOX00002");
        Assert.Equal(28, box.FreeToSell);
    }

    [Fact]
    public async Task A_part_with_no_inventory_record_is_skipped_rather_than_failing_the_scan()
    {
        // One missing record must not take the panel down with it: the two
        // parts either side of it still turn up.
        (PromisedStock stock, _) = Build(AMixedCatalogue());

        IReadOnlyList<PromisedPart> found = await stock.FindAsync(5, CancellationToken.None);

        Assert.DoesNotContain(found, part => part.PartNumber == "E-WIR00003");
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task A_quiet_account_yields_nothing_rather_than_something()
    {
        // What a real ERP on a slow week would say. The panel is allowed to be
        // empty; it is not allowed to invent an example, because the screen
        // presents these as read from the account.
        (PromisedStock stock, _) = Build(AQuietCatalogue());

        Assert.Empty(await stock.FindAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task No_more_than_the_number_asked_for_is_returned()
    {
        (PromisedStock stock, _) = Build(AMixedCatalogue());

        Assert.Single(await stock.FindAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task The_account_is_scanned_once_and_the_answer_kept()
    {
        // It reads inventory records to fill a hint on a screen somebody is
        // waiting for. Rescanning per page load would be a poor trade, and the
        // caching is why this had to be a singleton in the first place.
        (PromisedStock stock, InMemoryErp erp) = Build(AMixedCatalogue());

        await stock.FindAsync(5, CancellationToken.None);
        int afterFirst = erp.RecordReads;

        await stock.FindAsync(5, CancellationToken.None);

        Assert.Equal(afterFirst, erp.RecordReads);
    }

    [Fact]
    public async Task Requests_arriving_together_still_scan_only_once()
    {
        // Several first requests can arrive at the same moment, which is
        // exactly when nothing is cached yet. Without the second check inside
        // the gate each of them runs the whole scan.
        (PromisedStock stock, InMemoryErp erp) = Build(AMixedCatalogue());

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => stock.FindAsync(5, CancellationToken.None)));

        (PromisedStock alone, InMemoryErp aloneErp) = Build(AMixedCatalogue());
        await alone.FindAsync(5, CancellationToken.None);

        Assert.Equal(aloneErp.RecordReads, erp.RecordReads);
    }
}
