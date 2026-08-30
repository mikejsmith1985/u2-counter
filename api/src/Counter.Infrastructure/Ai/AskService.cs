namespace Counter.Infrastructure.Ai;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Counter.Domain.Availability;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Microsoft.Extensions.Logging;

/// <summary>
/// A question in words, answered from the ERP through the MCP server.
/// </summary>
/// <remarks>
/// This is the part that makes the MCP server necessary rather than decorative.
/// Without a model in the loop, a protocol designed for a model to call tools is
/// doing nothing an ordinary function call would not do, and anybody who works
/// with this software would notice.
///
/// The tools below come in two kinds, and the difference is the point.
///
/// Some are compiled to this demonstration's layout — searching parts, reading
/// availability, comparing prices. They are fast paths for the questions a
/// counter asks all day, and they would not survive being pointed at a different
/// schema.
///
/// The rest ask the database what it contains: name the files, read a file's
/// dictionary, run a selection against any of them. Those work anywhere, because
/// a MultiValue database describes itself and the dictionary is the field
/// mapping. Without them an assistant is only ever as useful as the questions
/// somebody anticipated, which against an unfamiliar ERP is not useful at all.
///
/// Every one of them is a read. The selection tool takes a statement, and both
/// this code and the MCP server refuse anything whose verb is not SELECT or
/// SSELECT — so the guarantee does not rest on the model being well behaved, and
/// widening it would take a deliberate change in two places.
///
/// Every call is recorded with the raw record it returned, because that
/// transcript is the only thing that lets a reader tell a genuine MultiValue
/// record from a convincing imitation of one.
/// </remarks>
/// <param name="catalogue">The searchable catalogue.</param>
/// <param name="availability">Live stock, read per request.</param>
/// <param name="erp">The MCP client, for reading a record in its stored form.</param>
/// <param name="ledger">What has been spent today.</param>
/// <param name="options">Every limit that applies.</param>
/// <param name="logger">For reporting a call that failed.</param>
public sealed class AskService(
    CatalogueProjection catalogue,
    AvailabilityReader availability,
    Mcp.IErpReader erp,
    PriceComparison prices,
    SpendLedger ledger,
    AskOptions options,
    ILogger<AskService> logger)
{
    /// <summary>Most parts to hand back from one search.</summary>
    private const int SearchResults = 8;

    /// <summary>Most keys a generic selection may return.</summary>
    private const int QueryKeys = 25;

    /// <summary>How many of those to read back in full, so an answer has values in it.</summary>
    private const int QueryRecords = 5;

    /// <summary>How the assistant is told to behave.</summary>
    /// <remarks>
    /// Short on purpose. The instructions that matter are the ones the tools
    /// enforce; anything asked for in prose here is a request, not a control.
    /// What prose can usefully do is stop the model inventing a number when a
    /// tool returned nothing, which is the failure that would matter most.
    /// </remarks>
    private const string Instructions =
        "You answer questions for a trade counter, from a MultiValue ERP.\n\n" +
        "Use the tools for every fact. Never state a quantity, price or part " +
        "number that a tool did not return — if the tools do not answer the " +
        "question, say exactly that.\n\n" +
        "Free to sell is on hand minus committed. When someone asks whether a " +
        "part is available, free to sell is the number that answers them; on " +
        "hand is not, and saying otherwise promises stock that is already " +
        "somebody else's.\n\n" +
        "A price belongs to a price class, not to a customer, and customers are " +
        "assigned to a class. So \"who gets the best price on this\" is one call to " +
        "compare_prices, not one call per account.\n\n" +
        "Answer rather than ask. If a question covers several parts -- \"the most " +
        "15A AFCI breakers\" may match eight of them -- check them and give the " +
        "answer. You may make several calls at once. Ask which one was meant only " +
        "when the question genuinely cannot be answered without knowing, not " +
        "because answering would take a few more lookups.\n\n" +
        "Two or three sentences. Name the branch and the number. No preamble.";

    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly AvailabilityReader _availability = availability;
    private readonly Mcp.IErpReader _erp = erp;
    private readonly PriceComparison _prices = prices;
    private readonly SpendLedger _ledger = ledger;
    private readonly AskOptions _options = options;
    private readonly ILogger<AskService> _logger = logger;

    /// <summary>Whether an assistant is configured at all.</summary>
    public bool IsConfigured => _options.IsConfigured;

    /// <summary>The model that answers, so a caller can report it without guessing.</summary>
    public static string Model => AskOptions.Model;

    /// <summary>
    /// Answer one question, showing every call it took.
    /// </summary>
    /// <param name="question">What was asked, in words.</param>
    /// <param name="looking">What is on the person's screen, so a pronoun resolves.</param>
    /// <param name="questionsAlreadyAsked">How many this session has asked.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>The answer and its working, or the reason there is none.</returns>
    public async Task<(AskResult? Result, AskRefusal Refusal)> AnswerAsync(
        string question,
        ScreenContext looking,
        int questionsAlreadyAsked,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return (null, AskRefusal.NotConfigured);
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return (null, AskRefusal.NothingAsked);
        }

        if (questionsAlreadyAsked >= _options.QuestionsPerSession)
        {
            return (null, AskRefusal.SessionLimitReached);
        }

        if (!_ledger.HasBudgetToday(_options.TokensPerDay))
        {
            return (null, AskRefusal.DailyLimitReached);
        }

        return (await RunAsync(question, looking, questionsAlreadyAsked, cancellationToken), AskRefusal.None);
    }

    /// <summary>
    /// Drive the tool loop until the assistant answers or runs out of calls.
    /// </summary>
    /// <param name="question">What was asked.</param>
    /// <param name="questionsAlreadyAsked">How many this session has asked.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>The answer and its working.</returns>
    private async Task<AskResult> RunAsync(
        string question,
        ScreenContext looking,
        int questionsAlreadyAsked,
        CancellationToken cancellationToken)
    {
        // Constructed with no arguments so the key is read from the environment
        // and never appears in this code, in configuration this application
        // reads, or in anything it logs.
        AnthropicClient client = new();

        List<MessageParam> conversation =
            [new MessageParam { Role = "user", Content = question }];

        List<AskStep> steps = [];
        int inputTokens = 0;
        int outputTokens = 0;
        string answer = string.Empty;

        int callsMade = 0;

        for (int round = 0; round <= _options.MaxToolRounds; round++)
        {
            Message reply = await client.Messages.Create(
                new MessageCreateParams
                {
                    Model = AskOptions.Model,
                    MaxTokens = _options.MaxTokens,
                    System = Instructions + looking.ForModel(),
                    Messages = conversation,
                    Tools = AskTools.Definitions.Select(ToolUnion (tool) => tool).ToList(),
                },
                cancellationToken);

            inputTokens += (int)reply.Usage.InputTokens;
            outputTokens += (int)reply.Usage.OutputTokens;

            answer = TextOf(reply);

            ToolUseBlock[] wanted = ToolCallsIn(reply);

            if (wanted.Length == 0)
            {
                break;
            }

            // The last round is the model's chance to answer, not to ask again.
            // Without this an agent that keeps finding one more thing to check
            // turns one question into an unbounded number of requests.
            if (round == _options.MaxToolRounds || callsMade + wanted.Length > _options.MaxToolCallsInTotal)
            {
                // One last request, with no tools offered.
                //
                // Stopping here used to keep whatever text preceded the final
                // tool call, which is a running commentary rather than an
                // answer: "I can see there are records. Let me read one of
                // these to see the structure better:" was presented to somebody
                // as the reply to their question. It reads as broken software,
                // and the model had in fact gathered enough to say something
                // useful.
                //
                // The requested calls are answered with a note rather than run,
                // because the protocol requires a result for every call made and
                // the budget is precisely what has been spent.
                conversation.Add(new MessageParam { Role = "assistant", Content = EchoOf(reply) });

                conversation.Add(new MessageParam
                {
                    Role = "user",
                    Content = wanted
                        .Select(call => new ToolResultBlockParam
                        {
                            ToolUseID = call.ID,
                            Content =
                                "No further lookups are available for this question. "
                                    + "Answer from what you have already read, and say "
                                    + "plainly what you were not able to check.",
                        })
                        .Cast<ContentBlockParam>()
                        .ToList(),
                });

                Message closing = await client.Messages.Create(
                    new MessageCreateParams
                    {
                        Model = AskOptions.Model,
                        MaxTokens = _options.MaxTokens,
                        System = Instructions + looking.ForModel(),
                        Messages = conversation,
                    },
                    cancellationToken);

                inputTokens += (int)closing.Usage.InputTokens;
                outputTokens += (int)closing.Usage.OutputTokens;

                string closed = TextOf(closing);

                answer = string.IsNullOrWhiteSpace(closed)
                    ? "I ran out of lookups before I could answer that. Naming a "
                        + "specific part number would let me answer in one."
                    : closed;

                break;
            }

            conversation.Add(new MessageParam
            {
                Role = "assistant",
                Content = EchoOf(reply),
            });

            List<ToolResultBlockParam> results = [];

            callsMade += wanted.Length;

            foreach (ToolUseBlock call in wanted)
            {
                (string body, AskStep step) = await ExecuteAsync(call, cancellationToken);
                steps.Add(step);
                results.Add(new ToolResultBlockParam { ToolUseID = call.ID, Content = body });
            }

            conversation.Add(new MessageParam
            {
                Role = "user",
                Content = results.Select(ContentBlockParam (result) => result).ToList(),
            });
        }

        _ledger.Record(inputTokens + outputTokens);

        return new AskResult(
            answer,
            steps,
            AskOptions.Model,
            inputTokens,
            outputTokens,
            Math.Max(0, _options.QuestionsPerSession - questionsAlreadyAsked - 1),
            looking.ForReader());
    }

    /// <summary>
    /// Run one tool call and describe it for the transcript.
    /// </summary>
    /// <param name="call">What the assistant asked for.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>What to send back, and the record of what happened.</returns>
    private async Task<(string Body, AskStep Step)> ExecuteAsync(
        ToolUseBlock call,
        CancellationToken cancellationToken)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string arguments = JsonSerializer.Serialize(call.Input);

        try
        {
            return call.Name switch
            {
                AskTools.SearchParts => Search(call, arguments, timer),
                AskTools.ReadAvailability =>
                    await ReadAvailabilityAsync(call, arguments, timer, cancellationToken),
                AskTools.ReadRecord =>
                    await ReadRecordAsync(call, arguments, timer, cancellationToken),
                AskTools.ReadPrice =>
                    await ReadPriceAsync(call, arguments, timer, cancellationToken),
                AskTools.ComparePrices =>
                    await ComparePricesAsync(call, arguments, timer, cancellationToken),
                AskTools.ListFiles =>
                    await ListFilesAsync(call, arguments, timer, cancellationToken),
                AskTools.DescribeFile =>
                    await DescribeFileAsync(call, arguments, timer, cancellationToken),
                AskTools.Query =>
                    await QueryAsync(call, arguments, timer, cancellationToken),
                _ => Failed(call, arguments, timer, $"There is no tool called {call.Name}."),
            };
        }
#pragma warning disable CA1031 // A failed lookup is an answer, not a crash.
        catch (Exception error)
#pragma warning restore CA1031
        {
            // Deliberately broad, and handed back to the model rather than
            // thrown. An assistant told that a lookup failed can say so; one
            // whose request disappeared into an exception leaves the person
            // looking at a spinner.
            _logger.LogWarning(error, "The tool {Tool} could not be run", call.Name);

            return Failed(call, arguments, timer, error.Message);
        }
    }

    /// <summary>Name every file in the account.</summary>
    private async Task<(string, AskStep)> ListFilesAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> files = await _erp.ListFilesAsync(cancellationToken);
        string result = string.Join(", ", files);

        return (
            result,
            new AskStep(
                call.Name,
                arguments,
                string.Empty,
                string.Empty,
                string.Empty,
                $"{files.Count} file(s) in the account",
                result,
                (int)timer.ElapsedMilliseconds));
    }

    /// <summary>Read one file's dictionary.</summary>
    private async Task<(string, AskStep)> DescribeFileAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string fileName = Argument(call, "file");

        IReadOnlyList<Domain.Catalogue.DictionaryField> fields =
            await _erp.ListDictionaryAsync(fileName, cancellationToken);

        if (fields.Count == 0)
        {
            return Failed(call, arguments, timer, $"{fileName} has no dictionary, or does not exist.");
        }

        StringBuilder text = new();
        foreach (Domain.Catalogue.DictionaryField field in fields)
        {
            text.Append($"{field.Position}: {field.Name}");
            if (!string.IsNullOrWhiteSpace(field.Heading))
            {
                text.Append($" \"{field.Heading}\"");
            }

            text.Append(field.IsMultiValued ? " multi-valued" : " single-valued");
            if (!string.IsNullOrWhiteSpace(field.Conversion))
            {
                text.Append($", conversion {field.Conversion}");
            }

            text.AppendLine();
        }

        string result = text.ToString();

        return (
            result,
            new AskStep(
                call.Name,
                arguments,
                fileName,
                string.Empty,
                string.Empty,
                $"{fields.Count} field(s) described by {fileName}'s own dictionary",
                result,
                (int)timer.ElapsedMilliseconds));
    }

    /// <summary>
    /// Run a read-only selection and read back what it found.
    /// </summary>
    /// <remarks>
    /// Refused here as well as at the server. The MCP server allows read verbs
    /// only and would reject anything else, so this check adds no safety it does
    /// not already have -- what it adds is a sentence the model can act on,
    /// instead of a transport error, and one fewer round trip spent finding out.
    /// </remarks>
    private async Task<(string, AskStep)> QueryAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string statement = Argument(call, "statement").Trim();
        string fileName = Argument(call, "file");

        string verb = statement.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Empty;

        if (!verb.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            && !verb.Equals("SSELECT", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                call,
                arguments,
                timer,
                $"Refused: a statement must begin with SELECT or SSELECT, not {verb}. "
                    + "There is no write path through this tool.");
        }

        IReadOnlyList<string> keys = await _erp.SelectKeysAsync(
            statement, QueryKeys, cancellationToken);

        if (keys.Count == 0)
        {
            // Says what an empty result does and does not mean. Asked to look at
            // ORDER, the model ran a narrow selection, got nothing back, and told
            // the reader the file was empty -- it holds 25 records. An empty
            // result is a fact about the criteria, not about the file.
            string none =
                $"Nothing matched. That is a fact about the criteria, not about {fileName}: "
                    + $"run SELECT {fileName} with no conditions to see whether it holds records at all.";
            return (
                none,
                new AskStep(
                    call.Name, arguments, fileName, string.Empty, string.Empty,
                    "Nothing matched", none, (int)timer.ElapsedMilliseconds));
        }

        IReadOnlyDictionary<string, string> records = await _erp.ReadRecordsAsync(
            fileName, [.. keys.Take(QueryRecords)], cancellationToken);

        StringBuilder text = new();
        text.AppendLine($"{keys.Count} key(s): {string.Join(", ", keys)}");

        foreach ((string key, string raw) in records)
        {
            // Marks turned into readable separators. The stored bytes are what
            // read_record is for; here the model needs the values, not proof of
            // what they were stored as.
            text.AppendLine($"{key}: {raw.Replace('þ', '|').Replace('ý', ';')}");
        }

        string result = text.ToString();

        return (
            result,
            new AskStep(
                call.Name,
                arguments,
                fileName,
                string.Empty,
                string.Empty,
                $"{keys.Count} key(s) selected, {records.Count} read back",
                result,
                (int)timer.ElapsedMilliseconds));
    }

    /// <summary>What one customer pays for one part.</summary>
    private async Task<(string, AskStep)> ReadPriceAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string partNumber = Argument(call, "partNumber");
        string account = Argument(call, "customerAccount");

        CustomerQuote? quote = await _prices.ForCustomerAsync(
            partNumber, account, cancellationToken);

        if (quote is null)
        {
            return Failed(
                call,
                arguments,
                timer,
                $"No price for part {partNumber} and account {account}: one of them is unknown.");
        }

        string result =
            $"{quote.Name} ({quote.Account}), price class {quote.PriceClass}: " +
            $"list {quote.ListPrice:F2}, net {quote.NetPrice:F2}" +
            (quote.Multiplier is null ? string.Empty : $", multiplier {quote.Multiplier:F2}") +
            $". {quote.Terms}";

        return (
            result,
            new AskStep(
                call.Name,
                arguments,
                "PRICE",
                quote.Account,
                string.Empty,
                $"Priced {partNumber} for {quote.Account}",
                result,
                (int)timer.ElapsedMilliseconds));
    }

    /// <summary>What every price class pays for a part.</summary>
    private async Task<(string, AskStep)> ComparePricesAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string partNumber = Argument(call, "partNumber");

        PriceSpread? spread = await _prices.AcrossCustomersAsync(partNumber, cancellationToken);

        if (spread is null)
        {
            return Failed(call, arguments, timer, $"No part called {partNumber}.");
        }

        StringBuilder text = new();
        text.Append($"{spread.PartNumber} {spread.Description}, category {spread.CategoryCode}, ");
        text.AppendLine($"list {spread.ListPrice:F2}.");

        // Said out loud when the scan did not cover everything, because "cheapest"
        // over a subset is a different claim from "cheapest" and reads the same.
        if (spread.AccountsScanned < spread.AccountsTotal)
        {
            text.AppendLine(
                $"Read {spread.AccountsScanned} of {spread.AccountsTotal} accounts; " +
                "a class held only by an account beyond that is not listed.");
        }

        // The answer first, named as the answer.
        //
        // This used to print seven classes in price order and leave the reader to
        // work out that the first one was the point. Ordered is not the same as
        // answered: a reader scanning a wall of near-identical lines has to do
        // the comparison the tool already did. The model has the same problem,
        // and the same fix helps both.
        ClassPrice? cheapest = spread.Classes.FirstOrDefault();

        if (cheapest is not null)
        {
            text.AppendLine();
            text.AppendLine($"CHEAPEST -- class {cheapest.PriceClass} at {cheapest.NetPrice:F2}");
            text.AppendLine($"  {Describe(cheapest)}");

            if (spread.Classes.Count > 1)
            {
                text.AppendLine();
                text.AppendLine("Every other class, dearest saving first:");

                foreach (ClassPrice entry in spread.Classes.Skip(1))
                {
                    decimal difference = entry.NetPrice - cheapest.NetPrice;
                    text.AppendLine(
                        $"  class {entry.PriceClass} at {entry.NetPrice:F2} "
                            + $"(+{difference:F2}) -- {Describe(entry)}");
                }
            }
        }

        string result = text.ToString();

        return (
            result,
            new AskStep(
                call.Name,
                arguments,
                "PRICE",
                spread.PartNumber,
                string.Empty,
                $"Compared {spread.Classes.Count} price class(es) across {spread.AccountsScanned} account(s)",
                result,
                (int)timer.ElapsedMilliseconds));
    }

    /// <summary>One price class, said the way a person would say it.</summary>
    /// <param name="entry">The class and what it pays.</param>
    private static string Describe(ClassPrice entry)
    {
        string multiplier = entry.Multiplier is null
            ? "no terms in force"
            : $"x{entry.Multiplier:F2}";

        string examples = entry.Examples.Count > 0
            ? $", including {string.Join(", ", entry.Examples)}"
            : string.Empty;

        return $"{multiplier}, {entry.CustomerCount} account(s){examples}";
    }

    /// <summary>Search the catalogue.</summary>
    private (string, AskStep) Search(ToolUseBlock call, string arguments, Stopwatch timer)
    {
        string text = Argument(call, "query");

        IReadOnlyList<Domain.Catalogue.Part> found = _catalogue.Search(text, SearchResults);
        timer.Stop();

        string body = found.Count == 0
            ? "No part matches that."
            : string.Join("\n", found.Select(part =>
                $"{part.PartNumber} | {part.Description} | {part.Manufacturer}"));

        return (body, new AskStep(
            call.Name,
            arguments,
            ErpFiles.Product.Name,
            string.Empty,
            string.Empty,
            $"{found.Count} part(s) matched \"{text}\"",
            body,
            (int)timer.ElapsedMilliseconds));
    }

    /// <summary>Read one part's position at every branch.</summary>
    private async Task<(string, AskStep)> ReadAvailabilityAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string partNumber = Argument(call, "partNumber");

        PartAvailability position =
            await _availability.ReadAsync(partNumber, cancellationToken);

        timer.Stop();

        string body = position.Positions.Count == 0
            ? $"No inventory record exists for {partNumber}. That is not the same as none in stock."
            : string.Join("\n", position.Positions.Select(branch =>
                $"{branch.BranchCode} | on hand {branch.OnHand} | committed {branch.Committed} " +
                $"| free to sell {branch.FreeToSell}"));

        return (body, new AskStep(
            call.Name,
            arguments,
            ErpFiles.Inventory.Name,
            partNumber,
            string.Empty,
            $"{position.Positions.Count} branch position(s) read live",
            body,
            (int)timer.ElapsedMilliseconds));
    }

    /// <summary>Read a record exactly as the database holds it.</summary>
    private async Task<(string, AskStep)> ReadRecordAsync(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        CancellationToken cancellationToken)
    {
        string fileName = Argument(call, "file");
        string recordId = Argument(call, "recordId");

        if (!AskTools.ReadableFiles.Contains(fileName))
        {
            timer.Stop();
            return Failed(
                call,
                arguments,
                timer,
                $"{fileName} is not a file this assistant may read. " +
                $"It may read: {string.Join(", ", AskTools.ReadableFiles)}.");
        }

        string raw = await _erp.ReadRecordAsync(fileName, recordId, cancellationToken);
        timer.Stop();

        return (raw, new AskStep(
            call.Name,
            arguments,
            fileName,
            recordId,
            raw,
            "read in its stored form, separators included",
            raw,
            (int)timer.ElapsedMilliseconds));
    }

    /// <summary>Describe a call that could not be completed.</summary>
    private static (string, AskStep) Failed(
        ToolUseBlock call,
        string arguments,
        Stopwatch timer,
        string reason)
    {
        timer.Stop();

        return (reason, new AskStep(
            call.Name,
            arguments,
            string.Empty,
            string.Empty,
            string.Empty,
            reason,
            reason,
            (int)timer.ElapsedMilliseconds));
    }

    /// <summary>Read one argument from a tool call, as text.</summary>
    private static string Argument(ToolUseBlock call, string name) =>
        call.Input.TryGetValue(name, out System.Text.Json.JsonElement value)
            ? (value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : value.ToString().Trim())
            : string.Empty;

    /// <summary>
    /// The assistant's own turn, in the shape it has to be sent back in.
    /// </summary>
    /// <param name="reply">What came back.</param>
    /// <returns>The same turn as request blocks.</returns>
    /// <remarks>
    /// Mapped by hand rather than converted, because the SDK models a response
    /// block and a request block as different types and there is no conversion
    /// between them. Only text and tool calls are carried: those are the only
    /// block kinds this conversation can contain, and silently dropping anything
    /// else would be worse than not compiling if one ever appeared.
    /// </remarks>
    private static List<ContentBlockParam> EchoOf(Message reply)
    {
        List<ContentBlockParam> echoed = [];

        foreach (ContentBlock block in reply.Content)
        {
            if (block.TryPickText(out TextBlock? text))
            {
                echoed.Add(new TextBlockParam { Text = text.Text });
            }
            else if (block.TryPickToolUse(out ToolUseBlock? call))
            {
                echoed.Add(new ToolUseBlockParam
                {
                    ID = call.ID,
                    Name = call.Name,
                    Input = call.Input,
                });
            }
        }

        return echoed;
    }

    /// <summary>The tool calls in one reply, in the order they were asked for.</summary>
    /// <param name="reply">What came back.</param>
    /// <returns>Every tool call, which may be none.</returns>
    private static ToolUseBlock[] ToolCallsIn(Message reply)
    {
        List<ToolUseBlock> calls = [];

        foreach (ContentBlock block in reply.Content)
        {
            if (block.TryPickToolUse(out ToolUseBlock? call))
            {
                calls.Add(call);
            }
        }

        return [.. calls];
    }

    /// <summary>Everything the assistant said, joined.</summary>
    private static string TextOf(Message reply)
    {
        List<string> said = [];

        foreach (ContentBlock block in reply.Content)
        {
            if (block.TryPickText(out TextBlock? text))
            {
                said.Add(text.Text);
            }
        }

        return string.Join(" ", said).Trim();
    }
}
