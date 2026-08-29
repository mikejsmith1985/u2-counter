namespace Counter.IntegrationTests;

using System.Net;
using System.Reflection;
using Counter.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

/// <summary>
/// No route reaches ERP data with anything but a read.
/// </summary>
/// <remarks>
/// This proves intent: it reads the routing table and asserts that nothing which
/// touches the ERP is bound to a mutating verb. <see cref="ErpImmutabilityTests"/>
/// proves the outcome. Both are needed — a route table can be correct while a
/// handler writes anyway, and files can be unchanged simply because nobody
/// exercised the route that would have changed them.
///
/// The two mutating routes that do exist are named here rather than excluded by
/// pattern, so adding a third fails this test instead of quietly joining a list.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ReadOnlyRouteTests(CounterFixture fixture)
{
    /// <summary>
    /// The only routes allowed to accept a mutating verb.
    /// </summary>
    /// <remarks>
    /// Neither reaches the ERP. Signing in and choosing a customer change this
    /// application's own session and nothing in the database.
    /// </remarks>
    private static readonly string[] PermittedMutations =
    [
        "api/v1/session",
        "api/v1/session/customer",

        // Asking a question is a POST because a question is a body, not a path:
        // it can be long, it contains whatever somebody typed, and putting that
        // in a URL would log it in every proxy between here and the browser.
        //
        // It writes nothing to the ERP. The tools the assistant is given are
        // reads and only reads -- there is no write tool in the list for a model
        // to reach for -- so the verb here describes the shape of the request
        // rather than its effect on the database.
        //
        // Listed rather than exempted, because this test exists to make somebody
        // write that paragraph before adding a POST.
        "api/v1/ask",

        // The one route in this application that changes ERP data, and the only
        // one that ever should.
        //
        // It writes nothing on this deployment: IErpWriter is registered only
        // when Erp:Writable says so, the demonstration does not, and the endpoint
        // answers 501 with a reason. The route exists because "can it write?" is
        // a fair question about a database tool, and answering it with a refusal
        // proves only that the refusal works.
        //
        // What keeps the read-only claim intact is not this list. It is that the
        // write lives on its own interface with its own allowlist -- IErpReader
        // still has no write method on it, which WritePathTests asserts against
        // the type rather than against anybody's care.
        "api/v1/records/{fileName}/{recordId}/value",
    ];

    private static readonly string[] MutatingVerbs = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public void No_controller_binds_a_mutating_verb_to_an_erp_route()
    {
        List<string> offenders = [];

        foreach (Type controller in ControllerTypes())
        {
            string prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (IActionHttpMethodProvider binding in
                    action.GetCustomAttributes().OfType<IActionHttpMethodProvider>())
                {
                    if (!binding.HttpMethods.Any(verb => MutatingVerbs.Contains(verb)))
                    {
                        continue;
                    }

                    string template = (binding as IRouteTemplateProvider)?.Template ?? string.Empty;
                    string route = string.IsNullOrEmpty(template) ? prefix : $"{prefix}/{template}";

                    if (!PermittedMutations.Contains(route, StringComparer.Ordinal))
                    {
                        offenders.Add($"{controller.Name}.{action.Name} -> {route}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These routes accept a mutating verb and are not in the permitted list: " +
            string.Join("; ", offenders));
    }

    [Theory]
    [InlineData("/api/v1/parts")]
    [InlineData("/api/v1/parts/S-BRK00000/availability")]
    [InlineData("/api/v1/parts/S-BRK00000/record")]
    [InlineData("/api/v1/parts/S-BRK00000/commitments?branchCode=DEN")]
    [InlineData("/api/v1/customers")]
    [InlineData("/api/v1/activity")]
    public async Task Every_erp_route_refuses_every_mutating_verb(string path)
    {
        foreach (string verb in MutatingVerbs)
        {
            using HttpRequestMessage request = new(new HttpMethod(verb), path);
            using HttpResponseMessage response = await _fixture.Client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
                $"{verb} {path} returned {(int)response.StatusCode}, which is neither " +
                "405 nor 404. A mutating verb must not reach a handler at all.");
        }
    }

    /// <summary>Every controller in the application assembly.</summary>
    private static IEnumerable<Type> ControllerTypes() =>
        typeof(PartsController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);
}
