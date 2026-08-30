/**
 * Ask in words, and see exactly what was asked of the database.
 *
 * The answer is the smaller half of this. A sentence saying "299 free to sell at
 * Grand Junction" is worth no more than the reader's willingness to believe it,
 * and somebody evaluating this has no reason to believe anything yet.
 *
 * So every call the model made is shown underneath: which tool, which file,
 * which key, how long it took, and — for a record read — the bytes that came
 * back with their separators marked. A relational row cannot produce attribute
 * and value marks at the byte level, which makes the transcript the one part of
 * this screen that proves what the data actually is.
 */

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../../api/client";
import type { AskResult, AskStep, ScreenContext } from "../../api/types";
import { MarkedRecord } from "./MarkedRecord";

/**
 * Questions offered when the box is empty, so nobody has to invent one.
 *
 * Chosen to cover both halves of what the assistant can do, because the first
 * question somebody types decides what they think it is. Two exercise the tools
 * built for this counter; two exercise the ones that ask the database what it
 * holds, and those are the ones that would still work against a schema nobody
 * wrote code for. Offering only the first kind makes a general assistant look
 * like a demo script.
 */
const SUGGESTIONS = [
  "Which branch has the most 15A AFCI breakers free to sell?",
  "Which customers get the best price on E-BRK00008?",
  "What files are in this database?",
  "What does the ORDER file contain, and how is it structured?",
];

interface Props {
  /**
   * What the person can see. Sent with the question so "these" resolves.
   *
   * People at a counter do not repeat themselves: with a part on screen they ask
   * "how quickly can we get twenty of these to Denver?", and an assistant that
   * replies "which part?" when the number is six inches away has failed at the
   * thing it was added for.
   */
  looking: ScreenContext;
}

export function AskPanel({ looking }: Props): React.JSX.Element | null {
  const [question, setQuestion] = useState("");

  // Asked once. A deployment without a key has no assistant, and the honest
  // thing is to render nothing rather than a box that takes somebody's typing
  // and then tells them what it knew before they started.
  const { data: status } = useQuery({
    queryKey: ["ask-status"],
    queryFn: ({ signal }) => api.askStatus(signal),
    staleTime: Infinity,
    retry: false,
  });

  const ask = useMutation<AskResult, Error, string>({
    mutationFn: (asked: string) => api.ask(asked, looking),
  });

  if (!status?.isConfigured) {
    return null;
  }

  const failure = ask.error instanceof ApiFailure ? ask.error : null;
  const answer = ask.data;

  function submit(asked: string): void {
    const trimmed = asked.trim();
    if (trimmed.length > 0 && !ask.isPending) {
      setQuestion(trimmed);
      ask.mutate(trimmed);
    }
  }

  return (
    <section className="ask" data-tour="ask" aria-label="Ask a question">
      <form
        className="ask__form"
        onSubmit={(event) => {
          event.preventDefault();
          submit(question);
        }}
      >
        <input
          className="ask__input"
          type="text"
          value={question}
          placeholder="Ask in words — “which branch can cover 20 of these today?”"
          aria-label="Ask a question about stock"
          onChange={(event) => setQuestion(event.target.value)}
          disabled={ask.isPending}
        />
        <button type="submit" className="button" disabled={ask.isPending || !question.trim()}>
          {ask.isPending ? "Asking…" : "Ask"}
        </button>
      </form>

      {!answer && !ask.isPending && !failure && (
        <ul className="ask__suggestions">
          {SUGGESTIONS.map((suggestion) => (
            <li key={suggestion}>
              <button
                type="button"
                className="button button--quiet"
                onClick={() => submit(suggestion)}
              >
                {suggestion}
              </button>
            </li>
          ))}
        </ul>
      )}

      {ask.isPending && (
        <p className="ask__working">Reading the database…</p>
      )}

      {failure && (
        <p className="ask__failure" role="status">
          {failure.message}
        </p>
      )}

      {answer && !ask.isPending && (
        <div className="ask__answer">
          <p className="ask__said">{answer.answer}</p>

          <details className="ask__working-out" open>
            <summary>
              What it asked the database
              <span className="ask__meta">
                {answer.steps.length} call{answer.steps.length === 1 ? "" : "s"} ·{" "}
                {answer.model} · {answer.inputTokens + answer.outputTokens} tokens ·{" "}
                {answer.questionsLeft} question{answer.questionsLeft === 1 ? "" : "s"} left
                {answer.looking && (
                  <>
                    {" · "}
                    <span className="ask__looking" title="What it was told you had on screen. No figures came with it.">
                      shown {answer.looking}
                    </span>
                  </>
                )}
              </span>
            </summary>

            <ol className="ask__steps">
              {answer.steps.map((step, index) => (
                <Step key={`${step.tool}-${index}`} step={step} />
              ))}
            </ol>
          </details>
        </div>
      )}
    </section>
  );
}

/**
 * One call, and what it returned.
 *
 * @param step What the assistant asked for.
 */
function Step({ step }: { step: AskStep }): React.JSX.Element {
  return (
    <li className="ask__step">
      <div className="ask__step-head">
        <code className="ask__tool">{step.tool}</code>
        {step.file && <span className="ask__file">{step.file}</span>}
        {step.recordId && <span className="ask__key">{step.recordId}</span>}
        <span className="header__spacer" />
        <span className="ask__duration figures">{step.durationMs}ms</span>
      </div>

      <p className="ask__step-summary">{step.summary}</p>

      {/* What actually came back. A step reading "8 parts matched" tells a reader
          a number was produced and nothing about which parts -- so somebody
          checking whether the answer follows from the data has been handed the
          answer twice and the data never. */}
      {step.rawRecord ? (
        <MarkedRecord raw={step.rawRecord} />
      ) : (
        step.result && <pre className="ask__result">{step.result}</pre>
      )}
    </li>
  );
}
