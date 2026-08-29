namespace Counter.Api.Contracts;

/// <summary>One result of a catalogue search.</summary>
/// <param name="PartNumber">The key.</param>
/// <param name="Description">What the part is.</param>
/// <param name="Manufacturer">Who makes it.</param>
/// <param name="UnitOfMeasure">How it is sold.</param>
/// <param name="IsDiscontinued">Shown, marked, never hidden.</param>
/// <param name="TotalFreeToSell">Units available across every branch.</param>
public sealed record SearchResult(
    string PartNumber,
    string Description,
    string Manufacturer,
    string UnitOfMeasure,
    bool IsDiscontinued,
    int TotalFreeToSell);

/// <summary>What a search returned.</summary>
/// <param name="Results">Matches, best first.</param>
/// <param name="Envelope">How much of the answer this is.</param>
public sealed record SearchResponse(
    IReadOnlyList<SearchResult> Results,
    ResponseEnvelope Envelope);

/// <summary>The part being looked at.</summary>
/// <param name="PartNumber">The key.</param>
/// <param name="Description">What it is.</param>
/// <param name="Manufacturer">Who makes it.</param>
/// <param name="ManufacturerPartNumber">Their number, which customers quote.</param>
/// <param name="UnitOfMeasure">How it is sold.</param>
/// <param name="IsDiscontinued">Whether it is still stocked going forward.</param>
public sealed record PartSummary(
    string PartNumber,
    string Description,
    string Manufacturer,
    string ManufacturerPartNumber,
    string UnitOfMeasure,
    bool IsDiscontinued);

/// <summary>One branch's position.</summary>
/// <param name="BranchCode">As the inventory record names it.</param>
/// <param name="BranchName">The branch's name, or the bare code when unknown.</param>
/// <param name="City">Where it is.</param>
/// <param name="OnHand">Units present.</param>
/// <param name="Committed">Units promised.</param>
/// <param name="FreeToSell">Units sellable now.</param>
/// <param name="OnOrder">Units expected from a supplier. Never counted as available.</param>
/// <param name="Bin">Where it sits, empty when unrecorded.</param>
/// <param name="StockState">Available, AllCommitted, or None.</param>
/// <param name="IsBranchKnown">
/// False when the inventory record names a branch that BRANCH does not. The row
/// is still shown: data nobody has cleaned up should still tell the truth.
/// </param>
public sealed record BranchAvailability(
    string BranchCode,
    string BranchName,
    string City,
    int OnHand,
    int Committed,
    int FreeToSell,
    int OnOrder,
    string Bin,
    string StockState,
    bool IsBranchKnown);

/// <summary>Terms that were available but not applied, and why.</summary>
/// <param name="TermsDescription">The terms.</param>
/// <param name="Reason">Why they did not apply, in words a person can repeat.</param>
public sealed record DisregardedTermsView(string TermsDescription, string Reason);

/// <summary>What the customer pays, and how that was arrived at.</summary>
/// <param name="ListPrice">Undiscounted.</param>
/// <param name="NetPrice">What this customer pays.</param>
/// <param name="Multiplier">Fraction of list, null when no terms applied.</param>
/// <param name="Basis">Contract or List.</param>
/// <param name="TermsDescription">The agreement that produced the price.</param>
/// <param name="DisregardedTerms">
/// Terms on file that did not apply. Present so an unexpectedly high price can be
/// explained on the call rather than escalated.
/// </param>
public sealed record PricingView(
    decimal ListPrice,
    decimal NetPrice,
    decimal? Multiplier,
    string Basis,
    string? TermsDescription,
    IReadOnlyList<DisregardedTermsView> DisregardedTerms);

/// <summary>Everything the part screen needs, in one answer.</summary>
/// <param name="Part">The catalogue item.</param>
/// <param name="IsStockKnown">
/// False when the part has no inventory record. Distinct from zero stock: telling
/// a customer there is none when nobody has counted is a different, worse answer.
/// </param>
/// <param name="TotalFreeToSell">Units available across every branch.</param>
/// <param name="Branches">Every branch holding a position.</param>
/// <param name="Pricing">What this customer pays, or list when none is selected.</param>
/// <param name="Envelope">How much of the answer this is.</param>
public sealed record AvailabilityResponse(
    PartSummary Part,
    bool IsStockKnown,
    int TotalFreeToSell,
    IReadOnlyList<BranchAvailability> Branches,
    PricingView Pricing,
    ResponseEnvelope Envelope);

/// <summary>One order holding stock.</summary>
/// <param name="OrderNumber">The order.</param>
/// <param name="CustomerName">Whose it is.</param>
/// <param name="Quantity">Units held.</param>
/// <param name="State">Why it holds them.</param>
/// <param name="PromisedDate">When the customer expects it.</param>
public sealed record CommitmentView(
    string OrderNumber,
    string CustomerName,
    int Quantity,
    string State,
    DateOnly PromisedDate);

/// <summary>What is holding a branch's committed stock.</summary>
/// <param name="BranchCode">The branch.</param>
/// <param name="CommittedTotal">What the inventory record says is committed.</param>
/// <param name="AccountedFor">What the listed orders explain.</param>
/// <param name="Unaccounted">
/// The difference. Reported rather than hidden: an order closed without releasing
/// its allocation leaves stock committed to nothing, and a discrepancy someone can
/// see is worth more than a screen that quietly adds up.
/// </param>
/// <param name="Commitments">The orders themselves.</param>
/// <param name="Envelope">How much of the answer this is.</param>
public sealed record CommitmentsResponse(
    string BranchCode,
    int CommittedTotal,
    int AccountedFor,
    int Unaccounted,
    IReadOnlyList<CommitmentView> Commitments,
    ResponseEnvelope Envelope);

/// <summary>A separator, described so the client can render it visibly.</summary>
/// <param name="Character">The character itself.</param>
/// <param name="Code">Its code point.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Separates">What it separates.</param>
public sealed record MarkDescription(string Character, int Code, string Name, string Separates);

/// <summary>The stored record, beside the structured form built from it.</summary>
/// <param name="FileName">Which file it came from.</param>
/// <param name="RecordId">Its key.</param>
/// <param name="RawRecord">
/// The record exactly as stored, separators included and not stripped. The client
/// renders them; the contract's job is not to lie about what the record holds.
/// </param>
/// <param name="Marks">How to display and label each separator present.</param>
/// <param name="Parsed">The same record as fields and values.</param>
/// <param name="Query">What was run to retrieve it.</param>
/// <param name="Envelope">How much of the answer this is.</param>
public sealed record RecordResponse(
    string FileName,
    string RecordId,
    string RawRecord,
    IReadOnlyList<MarkDescription> Marks,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Parsed,
    string Query,
    ResponseEnvelope Envelope);

/// <summary>A customer, for the selector.</summary>
/// <param name="AccountNumber">The key.</param>
/// <param name="Name">Trading name.</param>
/// <param name="AddressLine">
/// The first line of their address, which is what tells two customers of the
/// same name apart in the selector.
///
/// Not a city: the CUSTOMER file holds address lines and no city field, and
/// calling this one a city would put a street address under a heading that says
/// otherwise -- an interface stating something the record does not.
/// </param>
/// <param name="PriceClass">Empty when they pay list.</param>
public sealed record CustomerSummary(
    string AccountNumber,
    string Name,
    string AddressLine,
    string PriceClass);

/// <summary>What customers matched.</summary>
/// <param name="Results">Matches.</param>
/// <param name="Envelope">How much of the answer this is.</param>
public sealed record CustomerSearchResponse(
    IReadOnlyList<CustomerSummary> Results,
    ResponseEnvelope Envelope);
