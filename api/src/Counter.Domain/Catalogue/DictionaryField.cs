namespace Counter.Domain.Catalogue;

/// <summary>
/// One field, as the database's own dictionary describes it.
/// </summary>
/// <remarks>
/// A MultiValue database describes itself: the dictionary says which position
/// holds what, how to display it, and whether it is multi-valued. Reading that
/// rather than compiling a layout is what lets a screen fit whatever account it
/// is pointed at.
///
/// The heading is what a person should see. The position is what the record is
/// actually indexed by, and the two are not interchangeable -- a screen showing
/// "field 2" has told the reader nothing, and a screen that has forgotten the
/// position cannot read the value.
/// </remarks>
/// <param name="Name">The dictionary item's own name, e.g. ON.HAND.</param>
/// <param name="Position">Which field of the record it describes. Zero is the key.</param>
/// <param name="Heading">The column title a person reads.</param>
/// <param name="Format">Width and justification, as the dictionary states it.</param>
/// <param name="IsMultiValued">
/// Whether one record holds many of these. For the parallel fields that carry a
/// branch position, this is the only warning a reader gets that position n of
/// each belongs to the same branch.
/// </param>
/// <param name="Conversion">
/// How a stored value becomes a displayed one -- MD2 for two implied decimals,
/// D2/ for a date. Empty when the value needs no conversion. A reader that
/// ignored this would report every price a hundred times too large.
/// </param>
public sealed record DictionaryField(
    string Name,
    int Position,
    string Heading,
    string Format,
    bool IsMultiValued,
    string Conversion);
