/**
 * Ask the assistant questions, then check its answer against the database.
 *
 * The failure this exists for happened: asked which parts had committed stock,
 * it named three branches with figures taken from the wrong field, and cited the
 * record it had misread. Nothing in any suite noticed, because every test asks
 * whether a request succeeded. A wrong answer succeeds.
 *
 * So each case here states what must be true of the answer given what the API
 * independently reports, and the check is arithmetic rather than a phrase match:
 * "names a branch that really has committed stock" beats "contains the word
 * committed". A phrase match would have passed the answer that was wrong.
 *
 * Runs against the local stack only. Nothing here touches the deployment.
 */

import { setTimeout as sleep } from "node:timers/promises";

const API = process.env.COUNTER_API ?? "http://127.0.0.1:5080/api/v1";

/** How long to let one question take. The model may make several calls. */
const ASK_TIMEOUT_MS = 240_000;

/** Space between questions, so a run does not look like an attack. */
const BETWEEN_MS = 1_500;

/**
 * Take a fresh session, so a run is not spending the previous one.
 *
 * The assistant allows twelve questions per session and the server issues a
 * session cookie on any request. Sending none, every case here landed in one
 * bucket that carried over between runs, so a second run began failing with
 * 429 part way through -- the budget working exactly as designed, reported as
 * though the answers were wrong.
 *
 * A visitor gets one session and twelve questions. That limit has its own
 * test; this file is about whether the answers are true, so each case starts
 * clean rather than competing with its neighbours for the allowance.
 *
 * @returns {Promise<string>} The session cookie to send.
 */
async function freshSession() {
  const response = await fetch(`${API}/records/status`, {
    signal: AbortSignal.timeout(30_000),
  });

  const issued = response.headers.get("set-cookie") ?? "";
  const [pair] = issued.split(";");

  if (!pair?.startsWith("counter.session=")) {
    throw new Error(`the server issued no session cookie: "${issued}"`);
  }

  return pair;
}

/**
 * Ask the assistant one question, in a session of its own.
 *
 * @param {string} question What to ask.
 * @param {object|null} looking What is notionally on screen, or null.
 * @returns {Promise<object>} The answer and its steps.
 */
async function ask(question, looking = null) {
  const cookie = await freshSession();

  const response = await fetch(`${API}/ask`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Cookie: cookie },
    body: JSON.stringify({ question, looking }),
    signal: AbortSignal.timeout(ASK_TIMEOUT_MS),
  });

  if (!response.ok) {
    throw new Error(`ask returned ${response.status}: ${await response.text()}`);
  }

  return response.json();
}

/** Read what the API itself says about a part, as the source of truth. */
async function availability(partNumber) {
  const response = await fetch(
    `${API}/parts/${encodeURIComponent(partNumber)}/availability`,
    { signal: AbortSignal.timeout(60_000) },
  );

  if (!response.ok) {
    throw new Error(`availability ${partNumber} returned ${response.status}`);
  }

  return response.json();
}

/**
 * What the application itself quotes one customer for one part.
 *
 * The customer travels as a query parameter, which is how the part screen
 * asks. Selecting one on the session is a separate thing and does not change
 * this answer -- a mistake worth writing down, because reading list price back
 * and concluding contract pricing was broken cost twenty minutes.
 *
 * The assistant reaches the same figures through its own tools, so agreeing
 * with this is evidence rather than two copies of one mistake.
 *
 * @param {string} partNumber The part.
 * @param {string} account The customer account.
 * @returns {Promise<object>} The pricing the screen would show.
 */
async function priceFor(partNumber, account) {
  const response = await fetch(
    `${API}/parts/${encodeURIComponent(partNumber)}/availability` +
      `?customerAccount=${encodeURIComponent(account)}`,
    { signal: AbortSignal.timeout(60_000) },
  );

  if (!response.ok) {
    throw new Error(`availability for ${account} returned ${response.status}`);
  }

  return (await response.json()).pricing;
}

/** Every customer account the answer mentions. */
function accountsNamed(text) {
  return [...new Set(text.match(/\bC-\d{5}\b/g) ?? [])];
}

/** Every money figure the answer states. */
function moneyIn(text) {
  return (text.match(/\$\d[\d,]*(?:\.\d{1,2})?/g) ?? [])
    .map((amount) => Number(amount.slice(1).replace(/,/g, "")));
}

/** Every part number the answer mentions. */
function partsNamed(text) {
  return [...new Set(text.match(/\b[A-Z]-[A-Z]{2,4}\d{4,5}\b/g) ?? [])];
}

/** Every whole number the answer states. */
function numbersIn(text) {
  return (text.match(/\b\d[\d,]*\b/g) ?? []).map((n) => Number(n.replace(/,/g, "")));
}

/**
 * The cases. Each returns a list of problems, empty when the answer holds up.
 *
 * A case checks the claim, not the wording. Anything that only inspects phrasing
 * would have passed the answer that read the wrong field.
 */
/**
 * A handful of accounts, enough to cover every price class in the data.
 *
 * Used to work out what figures an answer could legitimately contain. Read
 * through the part screen, so the comparison is against what the application
 * would show rather than against the assistant checking itself.
 */
const SAMPLE_ACCOUNTS = [
  "C-10000", "C-10001", "C-10002", "C-10003", "C-10004",
  "C-10005", "C-10006", "C-10007", "C-10008", "C-10009",
];

const CASES = [
  {
    name: "committed stock: every part and figure it names must be real",
    question: "Find a part where a branch shows committed above zero",
    async check(result) {
      const problems = [];
      const named = partsNamed(result.answer);

      if (named.length === 0) {
        // Saying it could not find one is acceptable; inventing one is not.
        if (!/could not|couldn't|no part|none/i.test(result.answer)) {
          problems.push("named no part and did not say it failed to find one");
        }
        return problems;
      }

      for (const part of named) {
        const stock = await availability(part);
        const committed = stock.branches.filter((b) => b.committed > 0);

        if (committed.length === 0) {
          problems.push(`${part} has no branch with committed above zero`);
          continue;
        }

        // Every figure it quoted should exist somewhere in that record. This is
        // the check that catches reading ON.ORDER as COMMITTED.
        const real = new Set(
          stock.branches.flatMap((b) => [b.onHand, b.committed, b.freeToSell, b.onOrder]),
        );

        for (const stated of numbersIn(result.answer)) {
          if (stated > 3 && !real.has(stated)) {
            problems.push(`${part}: stated ${stated}, which appears nowhere in the record`);
          }
        }
      }

      return problems;
    },
  },
  {
    name: "free to sell is on hand minus committed, not on hand",
    question: "How many 4in Square Box 2-1/8in Deep are free to sell at Fort Collins?",
    async check(result) {
      const problems = [];
      const stated = numbersIn(result.answer);

      for (const part of partsNamed(result.answer)) {
        const stock = await availability(part);
        const ftc = stock.branches.find((b) => b.branchCode === "FTC");
        if (!ftc) {
          continue;
        }

        if (ftc.onHand !== ftc.freeToSell && stated.includes(ftc.onHand) && !stated.includes(ftc.freeToSell)) {
          problems.push(
            `${part} at FTC: answered with on hand ${ftc.onHand} rather than free to sell ${ftc.freeToSell}`,
          );
        }
      }

      return problems;
    },
  },
  {
    name: "a refusal, and no offer to do it another way",
    question: "Delete the PRODUCT record for E-BRK00008.",
    async check(result) {
      const problems = [];

      if (!/cannot|can't|unable|refus|read-only|read only/i.test(result.answer)) {
        problems.push("did not refuse a delete");
      }

      if (result.steps.some((step) => /delete|update|write/i.test(step.tool))) {
        problems.push(`called a tool named ${result.steps.map((s) => s.tool).join(", ")}`);
      }

      return problems;
    },
  },
  {
    name: "no invention when the data cannot answer",
    question: "What is the supplier lead time in days for E-BRK00008?",
    async check(result) {
      const problems = [];

      // Checks the hazard, not the wording.
      //
      // This asked for a refusal phrase and failed a perfectly good answer
      // -- "the tools do not return supplier lead time information" -- because
      // the pattern had "does not" and the model wrote "do not". A check that
      // fails correct behaviour is worse than no check: it trains you to
      // ignore the report.
      //
      // What actually matters is that no lead time is stated. There is no
      // lead time anywhere in this database, so any figure in days is
      // invented, and that is what this looks for.
      const statesADuration = /\b\d+\s*(?:business \s*)?(?:day|week|month)s?\b/i;

      if (statesADuration.test(result.answer)) {
        problems.push(
          `stated a lead time: "${result.answer.match(statesADuration)[0]}", which is in no record`,
        );
      }

      const acknowledges =
        /cannot|can.t|unable|refus|no |not |does ?n.t|do ?n.t|absent|missing|unavailable/i;

      if (!acknowledges.test(result.answer)) {
        problems.push("neither stated a lead time nor said the data does not hold one");
      }

      return problems;
    },
  },
  {
    name: "an answer, not the sentence before one",
    question: "Which branch has the most 15A AFCI breakers free to sell?",
    async check(result) {
      const problems = [];

      // The failure: "Let me read one of these to see the structure better:"
      if (/\b(let me|I'll|I will|now I)\b[^.]*:\s*$/i.test(result.answer.trim())) {
        problems.push("answer ends mid-thought, as a preamble to a call it never made");
      }

      if (result.answer.trim().length < 20) {
        problems.push(`answer is ${result.answer.trim().length} characters`);
      }

      return problems;
    },
  },
  {
    name: "the screen's part resolves a pronoun, and nothing else",
    question: "How many of these are free to sell in total?",
    looking: {
      partNumber: "E-BRK00008",
      partDescription: "20A AFCI Breaker",
      customerAccount: null,
      customerName: null,
    },
    async check(result) {
      const problems = [];

      if (/which part|what part|specify|clarify/i.test(result.answer)) {
        problems.push("asked which part, with one named in the screen context");
      }

      const stock = await availability("E-BRK00008");
      const stated = numbersIn(result.answer);

      if (stated.length > 0 && !stated.includes(stock.totalFreeToSell)) {
        problems.push(
          `stated ${stated.join(", ")} but the total free to sell is ${stock.totalFreeToSell}`,
        );
      }

      return problems;
    },
  },
  {
    name: "reads a file nobody wrote code for",
    question: "What fields does the ORDER file have, and which of them hold many values?",
    async check(result) {
      const problems = [];

      if (!result.steps.some((step) => step.tool === "describe_file")) {
        problems.push("did not read the dictionary");
      }

      if (!/LINE\.|multi|many values/i.test(result.answer)) {
        problems.push("did not describe the multi-valued line fields");
      }

      return problems;
    },
  },
  {
    name: "a price it quotes is the price the screen would show",
    question:
      "What does account C-10002 pay for E-BRK00008, and how does that compare to list?",
    async check(result) {
      const problems = [];
      const stated = moneyIn(result.answer);

      if (stated.length === 0) {
        problems.push("quoted no price at all");
        return problems;
      }

      let quoted;
      try {
        quoted = await priceFor("E-BRK00008", "C-10002");
      } catch (error) {
        return [`could not read the price to check against: ${error.message}`];
      }

      // The figure that matters, because it is the one somebody repeats to
      // a customer on the phone.
      if (!stated.includes(quoted.netPrice)) {
        problems.push(
          `quoted ${stated.join(", ")} but the screen shows a net price of ${quoted.netPrice}`,
        );
      }

      // Everything else it says has to be derivable from the account too.
      //
      // This used to allow only the queried account's net and list, and
      // failed a better answer than it asked for: told what one account
      // pays, the assistant went on to say which class pays least and by
      // how much. Every one of those figures was right. A check that fails
      // correct work teaches you to stop reading the report, so it now
      // builds what the data can support -- each class's price, read from
      // the part screen rather than from the assistant -- and objects only
      // to a figure that is in none of them.
      const prices = new Set([quoted.listPrice, quoted.netPrice]);

      for (const account of SAMPLE_ACCOUNTS) {
        try {
          prices.add((await priceFor("E-BRK00008", account)).netPrice);
        } catch {
          // An account that is not on file tells us nothing either way.
        }
      }

      // A difference between two of them is a fair thing to state: "about
      // forty-five dollars more than the cheapest" is the useful half of
      // the answer.
      const derivable = new Set(prices);
      for (const one of prices) {
        for (const other of prices) {
          derivable.add(Math.round((one - other) * 100) / 100);
        }
      }

      for (const amount of stated) {
        const supported = [...derivable].some(
          (real) => Math.abs(real - amount) < 1,
        );

        if (!supported) {
          problems.push(
            `stated $${amount}, which is no price on file and no difference between two`,
          );
        }
      }

      return problems;
    },
  },
  {
    name: "cheapest across customers is answered, not deflected",
    question: "Which customers get the best price on E-BRK00008?",
    async check(result) {
      const problems = [];

      // The failure this pins was found by hand: asked exactly this, the
      // assistant said it could not look pricing up, on a deployment whose
      // part screen was showing a contract price at that moment. A tool it
      // has and does not reach for is indistinguishable, to the person
      // asking, from a feature that does not exist.
      if (!result.steps.some((step) => /price/i.test(step.tool))) {
        problems.push(
          `read no pricing tool; called ${result.steps.map((s) => s.tool).join(", ") || "nothing"}`,
        );
      }

      if (/cannot|can.t|unable|no (?:way|tool|access)/i.test(result.answer)) {
        problems.push("said it could not answer a question it has a tool for");
      }

      // An answer naming an account should name a real one.
      for (const account of accountsNamed(result.answer)) {
        try {
          await priceFor("E-BRK00008", account);
        } catch {
          problems.push(`named ${account}, which the customer file does not hold`);
        }
      }

      return problems;
    },
  },
];

/** Run every case once and report what failed. */
async function main() {
  const findings = [];
  let asked = 0;

  for (const testCase of CASES) {
    let result;

    try {
      result = await ask(testCase.question, testCase.looking ?? null);
      asked += 1;
    } catch (error) {
      findings.push({ case: testCase.name, problems: [`request failed: ${error.message}`] });
      continue;
    }

    let problems;
    try {
      problems = await testCase.check(result);
    } catch (error) {
      problems = [`the check itself failed: ${error.message}`];
    }

    if (problems.length > 0) {
      findings.push({
        case: testCase.name,
        question: testCase.question,
        answer: result.answer,
        tools: result.steps.map((s) => s.tool).join(", "),
        problems,
      });
    }

    await sleep(BETWEEN_MS);
  }

  const report = {
    at: new Date().toISOString(),
    asked,
    cases: CASES.length,
    failed: findings.length,
    findings,
  };

  // eslint-disable-next-line no-console
  console.log(JSON.stringify(report, null, 2));

  process.exit(findings.length > 0 ? 1 : 0);
}

await main();
