namespace Counter.UnitTests;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Catalogue;

/// <summary>
/// Matching what a customer read off a box against what the catalogue holds.
/// </summary>
/// <remarks>
/// The gap this covers is the one between how a part number is stored and how it
/// reaches the counter. It is read aloud over a phone, copied off a label, or
/// typed from memory, and the hyphens and spaces rarely survive the journey. A
/// search that only matches the stored form fails on every one of those and looks
/// to the representative like the part not existing.
/// </remarks>
public sealed class PartSearchTests
{
    private static readonly Part Breaker = new(
        PartNumber: "SQD-QO120",
        Description: "20A Single Pole GFCI Breaker",
        Manufacturer: "Square D",
        ManufacturerPartNumber: "QO120GFI",
        UnitOfMeasure: "EA",
        CategoryCode: "BRK",
        ListPrice: 84.50m,
        IsDiscontinued: false);

    private static readonly Part Wire = new(
        PartNumber: "SOU-WIR00042",
        Description: "12 AWG Black THHN Wire",
        Manufacturer: "Southwire",
        ManufacturerPartNumber: "THHN12BK",
        UnitOfMeasure: "FT",
        CategoryCode: "WIR",
        ListPrice: 0.42m,
        IsDiscontinued: false);

    [Theory]
    [InlineData("SQD-QO120")]
    [InlineData("sqd-qo120")]
    [InlineData("SQDQO120")]
    [InlineData("sqd qo120")]
    [InlineData("  SQD-QO120  ")]
    [InlineData("Sqd.Qo120")]
    public void A_part_number_is_found_however_it_was_written(string typed)
    {
        IReadOnlyList<Part> found = Search(typed);

        Assert.Equal(Breaker.PartNumber, Assert.Single(found).PartNumber);
    }

    [Theory]
    [InlineData("gfci")]
    [InlineData("GFCI")]
    [InlineData("  gfci  ")]
    public void Description_words_match_whatever_case_they_were_typed_in(string typed)
    {
        Assert.Contains(Search(typed), part => part.PartNumber == Breaker.PartNumber);
    }

    [Fact]
    public void Words_match_in_any_order()
    {
        // Someone describing a part says the words in whichever order they think
        // of them. Both of these are the same request.
        Assert.NotEmpty(Search("gfci breaker"));
        Assert.NotEmpty(Search("breaker gfci"));
    }

    [Fact]
    public void Every_word_has_to_match_so_adding_one_narrows_the_search()
    {
        // The behaviour someone relies on without thinking about it: typing more
        // should give fewer results, not more.
        Assert.Equal(2, Search("wire").Count + Search("breaker").Count);
        Assert.Empty(Search("breaker wire"));
    }

    [Fact]
    public void A_manufacturers_own_number_finds_the_part()
    {
        // Customers quote the manufacturer's number at least as often as ours,
        // because that is what is printed on the item.
        Assert.Contains(Search("QO120GFI"), part => part.PartNumber == Breaker.PartNumber);
    }

    [Fact]
    public void The_manufacturers_name_finds_their_parts()
    {
        Assert.Contains(Search("Southwire"), part => part.PartNumber == Wire.PartNumber);
    }

    [Fact]
    public void An_exact_part_number_outranks_a_part_that_merely_mentions_it()
    {
        Part mentioning = Wire with
        {
            PartNumber = "GEN-00001",
            Description = "Replacement for SQD-QO120",
        };

        IReadOnlyList<Part> found = SearchWithin([mentioning, Breaker], "SQD-QO120");

        // Someone who typed a part number wants that part, not everything that
        // refers to it.
        Assert.Equal(Breaker.PartNumber, found[0].PartNumber);
    }

    [Fact]
    public void No_match_returns_nothing_rather_than_failing()
    {
        // Empty is a legitimate answer to a search, and the screen shows a
        // different thing for it than for a failure. Throwing here would collapse
        // the two.
        Assert.Empty(Search("zzzznothinglikethis"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_search_returns_nothing_rather_than_everything(string typed)
    {
        // Returning the whole catalogue for an empty box would look like the
        // search having run, and would bury the type-ahead in three thousand rows.
        Assert.Empty(Search(typed));
    }

    [Fact]
    public void The_limit_is_honoured()
    {
        IReadOnlyList<Part> found = SearchWithin([Breaker, Wire], "0", limit: 1);

        Assert.Single(found);
    }

    /// <summary>Search the two-part catalogue used by most of these tests.</summary>
    private static IReadOnlyList<Part> Search(string typed) =>
        SearchWithin([Breaker, Wire], typed);

    /// <summary>Search a catalogue built for one test.</summary>
    private static IReadOnlyList<Part> SearchWithin(
        IReadOnlyList<Part> parts,
        string typed,
        int limit = 25)
    {
        CatalogueProjection catalogue = CatalogueProjection.Over(parts);

        return catalogue.Search(typed, limit);
    }
}
