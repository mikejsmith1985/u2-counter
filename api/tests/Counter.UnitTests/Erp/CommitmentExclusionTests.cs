namespace Counter.UnitTests.Erp;

using Counter.Domain.Orders;
using Counter.Infrastructure.Erp;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Which orders are counted as holding stock, and which are quietly not.
/// </summary>
/// <remarks>
/// This is the screen a representative reads before ringing a branch: so much
/// is committed, here is what is holding it, and here is the part nothing
/// accounts for. Every exclusion below changes that last figure, and the
/// unaccounted figure is the one somebody acts on.
///
/// The paths that had no test were all the ones that exclude something. An
/// order in a state nobody recognises is excluded, which is the safe direction
/// and also the silent one -- data drifting in the account looks identical to
/// data that is fine. An order that has shipped no longer holds anything. An
/// order naming the part at a different branch holds nothing here.
///
/// Getting any of them wrong produces a screen that adds up and is not true.
/// </remarks>
public sealed class CommitmentExclusionTests
{
    /// <summary>Separates fields.</summary>
    private const char AM = StoredRecords.AttributeMark;

    /// <summary>Separates values within a field.</summary>
    private const char VM = StoredRecords.ValueMark;

    /// <summary>The part every test below asks about.</summary>
    private const string ThePart = "E-BRK00008";

    /// <summary>The branch every test below asks about.</summary>
    private const string TheBranch = "GRJ";

    /// <summary>
    /// An order: customer, date, state, then parallel line fields.
    /// </summary>
    /// <param name="state">The order's state, as the ERP holds it.</param>
    /// <param name="parts">The part on each line.</param>
    /// <param name="quantities">The quantity on each line.</param>
    /// <param name="branches">The branch on each line.</param>
    /// <returns>The record as stored.</returns>
    private static string Order(
        string state,
        string[] parts,
        int[] quantities,
        string[] branches) =>
        string.Join(
            AM,
            "C-10000",
            "2026-08-01",
            state,
            string.Join(VM, parts),
            string.Join(VM, quantities),
            string.Join(VM, branches),
            string.Join(VM, parts.Select(_ => "2026-09-15")));

    /// <summary>Build a reader over the given orders.</summary>
    /// <param name="orders">The ORDER file's contents.</param>
    /// <returns>The reader under test.</returns>
    private static CommitmentReader Build(Dictionary<string, string> orders)
    {
        InMemoryErp erp = new(new Dictionary<string, Dictionary<string, string>>
        {
            ["ORDER"] = orders,
            ["CUSTOMER"] = new() { ["C-10000"] = StoredRecords.Customer("Front Range Electric", "A1") },
        });

        return new CommitmentReader(
            erp, new PricingReader(erp), NullLogger<CommitmentReader>.Instance);
    }

    /// <summary>Ask what is holding stock of the usual part at the usual branch.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="committedTotal">What inventory says is committed.</param>
    /// <returns>What the screen would show.</returns>
    private static Task<BranchCommitments> Read(CommitmentReader reader, int committedTotal = 10) =>
        reader.ReadAsync(ThePart, TheBranch, committedTotal, CancellationToken.None);

    [Fact]
    public async Task An_order_holding_the_part_at_this_branch_is_counted()
    {
        // The ordinary case, so the exclusions below mean something.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("CONFIRMED", [ThePart], [4], [TheBranch]),
        });

        BranchCommitments held = await Read(reader);

        Assert.Single(held.Commitments);
        Assert.Equal(4, held.Commitments[0].Quantity);
    }

    [Fact]
    public async Task A_shipped_order_no_longer_holds_anything()
    {
        // The stock has left the building. Counting it would show a branch as
        // committed against an order that is already on a lorry.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("SHIPPED", [ThePart], [4], [TheBranch]),
        });

        Assert.Empty((await Read(reader)).Commitments);
    }

    [Fact]
    public async Task A_cancelled_order_holds_nothing()
    {
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("CANCELLED", [ThePart], [4], [TheBranch]),
        });

        Assert.Empty((await Read(reader)).Commitments);
    }

    [Fact]
    public async Task A_quote_is_not_a_commitment()
    {
        // Nobody has agreed to anything yet, and a quote that held stock would
        // let one customer's maybe deny another customer's order.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("QUOTE", [ThePart], [4], [TheBranch]),
        });

        Assert.Empty((await Read(reader)).Commitments);
    }

    [Fact]
    public async Task A_state_nobody_recognises_is_excluded_rather_than_counted()
    {
        // The safe direction, and the silent one: an account whose states have
        // drifted looks exactly like an account that is fine. Excluding is
        // right -- counting a state we cannot interpret as holding stock would
        // deny a customer stock on the strength of a guess.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("AWAITING-PAPERWORK", [ThePart], [4], [TheBranch]),
        });

        Assert.Empty((await Read(reader)).Commitments);
    }

    [Fact]
    public async Task An_order_for_this_part_at_another_branch_holds_nothing_here()
    {
        // The parallel-field case. The order genuinely holds the part, and the
        // branch list is what says where -- read against the wrong position and
        // this branch is charged with another branch's commitment.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("CONFIRMED", [ThePart], [4], ["DEN"]),
        });

        Assert.Empty((await Read(reader)).Commitments);
    }

    [Fact]
    public async Task Only_the_line_naming_this_part_is_counted_from_a_multi_line_order()
    {
        // One order, three lines, and the quantity that belongs to this part at
        // this branch is the second. Taking the first would be wrong by exactly
        // the amount that looks plausible.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order(
                "ALLOCATED",
                ["E-BOX00001", ThePart, "E-WIR00003"],
                [50, 7, 200],
                ["GRJ", TheBranch, "GRJ"]),
        });

        BranchCommitments held = await Read(reader);

        Assert.Single(held.Commitments);
        Assert.Equal(7, held.Commitments[0].Quantity);
    }

    [Fact]
    public async Task Committed_stock_no_order_explains_is_reported_rather_than_hidden()
    {
        // It happens in real accounts -- an order closed without releasing its
        // allocation leaves stock committed to nothing. A screen that quietly
        // added up would hide the one thing worth ringing the branch about.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("CONFIRMED", [ThePart], [4], [TheBranch]),
        });

        BranchCommitments held = await Read(reader, committedTotal: 10);

        Assert.Equal(4, held.AccountedFor);
        Assert.Equal(6, held.Unaccounted);
    }

    [Fact]
    public async Task An_excluded_order_widens_the_gap_rather_than_closing_it()
    {
        // The consequence of every exclusion above, stated once. A cancelled
        // order accounts for nothing, so the shortfall is the whole committed
        // figure -- which is the honest answer and looks alarming, as it should.
        CommitmentReader reader = Build(new()
        {
            ["SO-1000"] = Order("CANCELLED", [ThePart], [4], [TheBranch]),
        });

        BranchCommitments held = await Read(reader, committedTotal: 10);

        Assert.Equal(0, held.AccountedFor);
        Assert.Equal(10, held.Unaccounted);
    }

    [Fact]
    public async Task An_order_that_vanished_between_the_select_and_the_read_is_skipped()
    {
        // Two round trips, and the account is live between them. One missing
        // record must not take the whole screen down.
        InMemoryErp erp = new(new Dictionary<string, Dictionary<string, string>>
        {
            ["ORDER"] = new()
            {
                ["SO-1000"] = Order("CONFIRMED", [ThePart], [4], [TheBranch]),
            },
            ["CUSTOMER"] = new() { ["C-10000"] = StoredRecords.Customer("Front Range", "A1") },
        });

        CommitmentReader reader = new(
            new VanishingOrder(erp), new PricingReader(erp), NullLogger<CommitmentReader>.Instance);

        BranchCommitments held = await reader.ReadAsync(
            ThePart, TheBranch, 10, CancellationToken.None);

        Assert.Empty(held.Commitments);
    }

    /// <summary>An account that lists an order and then cannot produce it.</summary>
    /// <param name="inner">The account underneath.</param>
    private sealed class VanishingOrder(InMemoryErp inner) : Counter.Infrastructure.Mcp.IErpReader
    {
        /// <inheritdoc />
        public Task<string> ReadRecordAsync(
            string fileName, string recordId, CancellationToken cancellationToken) =>
            fileName == "ORDER"
                ? throw new Counter.Infrastructure.Mcp.ErpRecordNotFoundException(
                    $"{recordId} was deleted a moment ago.")
                : inner.ReadRecordAsync(fileName, recordId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> SelectKeysAsync(
            string query, int maxKeys, CancellationToken cancellationToken) =>
            inner.SelectKeysAsync(query, maxKeys, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(
            string fileName, IReadOnlyList<string> recordIds, CancellationToken cancellationToken) =>
            inner.ReadRecordsAsync(fileName, recordIds, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<Counter.Domain.Catalogue.DictionaryField>> ListDictionaryAsync(
            string fileName, CancellationToken cancellationToken) =>
            inner.ListDictionaryAsync(fileName, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken) =>
            inner.ListFilesAsync(cancellationToken);
    }
}
