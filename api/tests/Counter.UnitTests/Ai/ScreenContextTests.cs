using Counter.Infrastructure.Ai;

namespace Counter.UnitTests.Ai;

/// <summary>
/// What the assistant is told about the screen, and what it must never be told.
/// </summary>
/// <remarks>
/// The feature exists because "how quickly can we get twenty of these to Denver?"
/// came back as "which part did you mean?" with the part number on screen. The
/// risk it introduces is the mirror image: an assistant handed the contents of
/// the screen will answer from them, and then a figure appears in an answer that
/// no tool ever returned.
///
/// So these pin both halves. The referent must get through, and nothing else may.
/// </remarks>
public sealed class ScreenContextTests
{
    [Fact]
    public void SaysNothingWhenNothingIsSelected()
    {
        // An empty context must add no text at all, not a sentence saying it is
        // empty: instructions that discuss the absence of a part invite questions
        // about the absence of a part.
        Assert.Equal(string.Empty, ScreenContext.Empty.ForModel());
        Assert.False(ScreenContext.Empty.HasAnything);
    }

    [Fact]
    public void NamesThePartSoAPronounResolves()
    {
        ScreenContext looking = new("B-CON00291", "1in EMT Conduit", null, null);

        string told = looking.ForModel();

        Assert.Contains("B-CON00291", told, StringComparison.Ordinal);
        Assert.Contains("1in EMT Conduit", told, StringComparison.Ordinal);
        Assert.Contains("these", told, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NamesTheCustomerWhenOneIsBeingServed()
    {
        ScreenContext looking = new(null, null, "ALPINE6", "Alpine Electrical Supply 6");

        string told = looking.ForModel();

        Assert.Contains("ALPINE6", told, StringComparison.Ordinal);
        Assert.Contains("Alpine Electrical Supply 6", told, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysOutLoudThatNoFiguresCameWithIt()
    {
        // The load-bearing sentence. Without it a model told "the person is
        // looking at part X" will produce a stock figure for part X that no tool
        // returned -- which is the single failure this whole application is
        // built to make impossible.
        ScreenContext looking = new("B-CON00291", "1in EMT Conduit", "ALPINE6", "Alpine 6");

        string told = looking.ForModel();

        Assert.Contains("No figures came with this", told, StringComparison.Ordinal);
        Assert.Contains("tool call", told, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesNoQuantityPriceOrBranch()
    {
        // The type has no field capable of carrying one, and that is the point:
        // a later hand cannot widen the context with "and it has 170 free to
        // sell" without changing the shape, which this notices.
        Assert.Equal(
            ["PartNumber", "PartDescription", "CustomerAccount", "CustomerName"],
            typeof(ScreenContext)
                .GetProperties()
                .Where(property => property.Name != "HasAnything")
                .Select(property => property.Name)
                .Where(name => name != "EqualityContract")
                .ToArray());
    }

    [Fact]
    public void TellsTheReaderWhatItWasShown()
    {
        // Shown in the transcript so a resolved pronoun does not read as the
        // model remembering the data.
        ScreenContext looking = new("B-CON00291", "1in EMT Conduit", "ALPINE6", "Alpine 6");

        Assert.Equal("B-CON00291 · ALPINE6", looking.ForReader());
        Assert.Equal(string.Empty, ScreenContext.Empty.ForReader());
    }

    [Fact]
    public void TreatsWhitespaceAsAbsent()
    {
        // A selection cleared to an empty string must not produce "the part ."
        ScreenContext looking = new("   ", "  ", "", null);

        Assert.False(looking.HasAnything);
        Assert.Equal(string.Empty, looking.ForModel());
    }
}
