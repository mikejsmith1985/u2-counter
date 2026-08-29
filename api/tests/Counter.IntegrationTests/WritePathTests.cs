namespace Counter.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Counter.Infrastructure.Mcp;

/// <summary>
/// The write path exists, is separate, and is off here.
/// </summary>
/// <remarks>
/// The question worth answering is whether this understands CRUD against the underlying
/// database. The answer has two halves, and the second is the one that takes
/// work: writes exist, and they are kept where they cannot weaken the read-only
/// claim the rest of the application makes.
///
/// So these assert the separation rather than the writing. That the writing is
/// correct is asserted where writing happens — in the store's own suite and the
/// MCP server's — because that is where a parallel field can actually be moved.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class WritePathTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public void The_reader_still_has_no_write_on_it()
    {
        // The sentence the rest of the project relies on, asserted about the
        // type rather than about anybody's care. Anything holding an IErpReader
        // cannot acquire a write, because the interface does not have one -- so
        // adding a write path elsewhere could not have weakened this.
        string[] methods = [.. typeof(IErpReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)];

        foreach (string forbidden in new[] { "Write", "Update", "Delete", "Create", "Save" })
        {
            Assert.DoesNotContain(
                methods,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_write_allowlist_holds_one_tool_and_no_delete()
    {
        // Permit exactly what is used. The flow supported is "find a record,
        // review it, change one value"; anything broader would be a broader
        // claim than the application makes.
        Assert.Single(ErpWriteTools.Permitted);
        Assert.Contains(ErpWriteTools.UpdateValue, ErpWriteTools.Permitted);

        foreach (string forbidden in new[] { "delete_record", "write_record", "execute_query" })
        {
            Assert.DoesNotContain(forbidden, ErpWriteTools.Permitted);
        }
    }

    [Fact]
    public void The_read_allowlist_gained_nothing()
    {
        // The write tool must not have leaked into the list the reader checks.
        Assert.DoesNotContain(ErpWriteTools.UpdateValue, ErpTools.Permitted);
    }

    [Fact]
    public async Task This_deployment_reports_that_it_cannot_write()
    {
        // Asked before an edit control is offered, so nobody is invited to type
        // a change that cannot be made.
        JsonElement status = await _fixture.Client.ReadJsonAsync("/api/v1/records/status");

        Assert.False(status.GetProperty("canWrite").GetBoolean());
    }

    [Fact]
    public async Task An_attempt_to_change_a_record_is_refused_with_a_reason()
    {
        // Not a 500, and not a silent success. The distinction the answer has to
        // carry is "this deployment has no write path" rather than "something
        // went wrong" -- one of those is a decision and the other is a fault.
        using HttpResponseMessage response = await _fixture.Client.PostAsJsonAsync(
            "/api/v1/records/INVENTORY/E-BRK00008/value",
            new { position = 2, index = 0, value = "1" });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("reads only", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nothing_reached_the_erp_data()
    {
        // The check that matters more than the status code. A refused write that
        // had already touched the file would be worse than one that succeeded,
        // because nobody would go looking.
        using (await _fixture.Client.PostAsJsonAsync(
            "/api/v1/records/INVENTORY/E-BRK00008/value",
            new { position = 2, index = 0, value = "999999" }))
        {
            // The response is asserted elsewhere; this is about the files.
        }

        Assert.Equal(_fixture.BaselineHashes, _fixture.HashErpData());
    }
}
