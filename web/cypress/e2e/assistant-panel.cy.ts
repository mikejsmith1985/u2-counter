/**
 * The assistant's panel, driven with stubbed answers.
 *
 * Every reply here is intercepted rather than asked for. Two reasons, and the
 * second is the important one:
 *
 * The panel had no browser coverage at all, because exercising it for real
 * costs money on a personal API key and a suite nobody can afford to run is a
 * suite nobody runs. Stubbing removes that.
 *
 * More to the point, what the panel does with an answer is not the same
 * question as whether the answer is right. Whether it is right is checked
 * against the database by `scripts/overnight/ask-eval.mjs`, which asks real
 * questions and compares the figures. What is checked here is the half that
 * evaluation cannot see: that the working-out is shown, that a refusal reads
 * as an explanation rather than a stack trace, and that the screen says which
 * limit was reached — because "come back tomorrow" and "this was never
 * switched on" are different messages and must not share one.
 *
 * Those refusal states are the ones a visitor is most likely to meet and the
 * ones no other test touches: to see them for real you would have to exhaust a
 * budget first.
 */

/** A stubbed answer with its working-out, shaped like the real reply. */
const AN_ANSWER = {
  answer: "373 units are free to sell in total. GRJ holds the most, with 299.",
  steps: [
    {
      tool: "read_availability",
      arguments: '{"partNumber":"E-BRK00008"}',
      file: "INVENTORY",
      recordId: "E-BRK00008",
      query: "",
      summary: "6 branch positions read live",
      detail: "GRJ | on hand 310 | committed 11 | free to sell 299",
      durationMs: 42,
    },
  ],
  looking: null,
};

/**
 * Put a question to the panel.
 *
 * @param question What to type.
 */
function ask(question: string): void {
  cy.get(".ask__input").clear().type(question);
  cy.get(".ask form button[type=submit], .ask__form button[type=submit]").first().click();
}

/**
 * Stand in for the assistant with a fixed reply.
 *
 * @param status The HTTP status to answer with.
 * @param body The payload.
 */
function replyWith(status: number, body: unknown): void {
  cy.intercept("POST", "**/api/v1/ask", { statusCode: status, body }).as("ask");
}

describe("the assistant's panel", () => {
  beforeEach(() => {
    cy.visit("/");

    // The panel is only rendered where an assistant is configured. On a
    // deployment without a key there is nothing to test and skipping is the
    // honest outcome.
    cy.request("/api/v1/ask/status").then((status) => {
      if (status.body.isConfigured !== true) {
        cy.log("no assistant is configured on this deployment");
        (Cypress as unknown as { mocha: { getRunner: () => { suite: Mocha.Suite } } }).mocha
          .getRunner()
          .suite.ctx.skip();
      }
    });
  });

  it("shows the answer and the calls it took to get there", () => {
    // The working-out is the reason this screen is interesting. An answer with
    // no visible calls behind it is a chatbot; an answer with them is a
    // demonstration that the database was actually read.
    replyWith(200, AN_ANSWER);

    ask("how many are free to sell?");
    cy.wait("@ask");

    cy.get(".ask__said").should("contain.text", "373");
    cy.get(".ask__steps .ask__step").should("have.length", 1);
    cy.get(".ask__tool").should("contain.text", "read_availability");
    cy.get(".ask__file").should("contain.text", "INVENTORY");
  });

  it("names the record it read, so the answer can be checked by hand", () => {
    replyWith(200, AN_ANSWER);

    ask("how many are free to sell?");
    cy.wait("@ask");

    cy.get(".ask__key").should("contain.text", "E-BRK00008");
  });

  it("says the session's allowance is spent, and does not say it broke", () => {
    // This is a limit working, not a failure, and it has to read that way. The
    // allowance is small on purpose because the key behind it is a personal one.
    replyWith(429, {
      type: "session-limit-reached",
      title: "The assistant did not answer",
      detail:
        "This session has asked its allowance of questions. The demonstration " +
        "runs on a personal API key, so the allowance is small on purpose.",
    });

    ask("one question too many");
    cy.wait("@ask");

    cy.get(".ask__failure")
      .should("be.visible")
      .invoke("text")
      .should("match", /allowance|questions/i);
  });

  it("distinguishes the day's ceiling from the session's", () => {
    // Two different messages on purpose: one means wait, the other means come
    // back tomorrow, and a visitor given the wrong one waits for nothing.
    replyWith(429, {
      type: "daily-limit-reached",
      title: "The assistant did not answer",
      detail:
        "The assistant has spent its allowance for today. Everything else on " +
        "this screen still works; the assistant returns tomorrow.",
    });

    ask("the question after the ceiling");
    cy.wait("@ask");

    cy.get(".ask__failure").invoke("text").should("match", /tomorrow|today/i);
  });

  it("says plainly when no assistant is configured at all", () => {
    // A deployment without a key is a supported state, not a broken one, and
    // the rest of the application still works.
    replyWith(503, {
      type: "assistant-not-configured",
      title: "The assistant did not answer",
      detail: "No assistant is configured on this deployment. Everything else works.",
    });

    ask("anything");
    cy.wait("@ask");

    cy.get(".ask__failure").invoke("text").should("match", /configured|everything else/i);
  });

  it("survives an answer with no steps rather than rendering an empty list", () => {
    // The budget-exhaustion path answers without making any further calls, so
    // an empty steps array is a real reply and not a malformed one.
    replyWith(200, { ...AN_ANSWER, steps: [] });

    ask("something it answers from what it already read");
    cy.wait("@ask");

    cy.get(".ask__said").should("be.visible");
    cy.get(".ask__step").should("not.exist");
  });

  it("will not submit an empty question", () => {
    // Otherwise a stray click spends one of the twelve.
    cy.get(".ask__input").clear();
    cy.get(".ask form button[type=submit], .ask__form button[type=submit]")
      .first()
      .should("be.disabled");
  });

  it("has no serious accessibility violation with an answer on screen", () => {
    replyWith(200, AN_ANSWER);

    ask("how many are free to sell?");
    cy.wait("@ask");

    // The project's own command, which scopes axe to WCAG 2.1 A and AA at
    // serious and above -- the standard this project actually claims.
    cy.checkAccessibility(".ask");
  });
});
