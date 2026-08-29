namespace Counter.Infrastructure.Ai;

using System.Diagnostics;
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
/// The tools below are the same reads the screens use, and they are the whole
/// list. There is no write, no arbitrary query, and no way to name a file outside
/// the four this hands out — so the read-only guarantee does not depend on the
/// model being well behaved. It cannot ask for what is not there.
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
    SpendLedger ledger,
    AskOptions options,
    ILogger<AskService> logger)
{
    /// <summary>Most parts to hand back from one search.</summary>
    private const int SearchResults = 8;

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
        "Two or three sentences. Name the branch and the number. No preamble.";

    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly AvailabilityReader _availability = availability;
    private readonly Mcp.IErpReader _erp = erp;
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
    /// <param name="questionsAlreadyAsked">How many this session has asked.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>The answer and its working, or the reason there is none.</returns>
    public async Task<(AskResult? Result, AskRefusal Refusal)> AnswerAsync(
        string question,
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

        return (await RunAsync(question, questionsAlreadyAsked, cancellationToken), AskRefusal.None);
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
                    System = Instructions,
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
                answer = string.IsNullOrWhiteSpace(answer)
                    ? "I could not answer that within the number of lookups allowed. " +
                      "Try naming a specific part number."
                    : answer;
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
            Math.Max(0, _options.QuestionsPerSession - questionsAlreadyAsked - 1));
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
