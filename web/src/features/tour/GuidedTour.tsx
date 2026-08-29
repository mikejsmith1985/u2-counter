/**
 * A spotlight over the real interface, driving it rather than describing it.
 *
 * The overlay dims the page, cuts a hole around the element a step names, and
 * puts a card beside it. Nothing is faked: the highlighted control is the real
 * one, and when a step needs a part selected the tour selects it, so the reader
 * watches the application work instead of reading that it would.
 *
 * Keyboard-operable throughout, because a tour that traps somebody who does not
 * use a mouse is worse than no tour. Escape leaves, arrows and Enter move, and
 * focus is held inside the card while it is open.
 */

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { TOUR_STEPS, type TourStep } from "./steps";

/**
 * How long to keep looking for a step's target before giving up on it.
 *
 * Long enough for the branch grid's own request on a cold deployment, short
 * enough that a genuinely missing target does not leave the card hunting.
 */
const TARGET_WAIT_MS = 4000;

/** How often to look. */
const TARGET_POLL_MS = 150;

/** Space left between the spotlight and the element it surrounds. */
const SPOTLIGHT_PADDING = 6;

/** How far the card sits from the spotlight. */
const CARD_GAP = 14;

/**
 * The card's size before it has been measured.
 *
 * A starting guess only, replaced on the first frame by what the card actually
 * is. Guessing was enough while the card was one paragraph and stopped being
 * enough when some steps grew to four lines and others shrank to two: a card
 * placed by an estimate of its own height overlaps the thing it is pointing at
 * by however much the estimate was wrong.
 */
const CARD_ESTIMATE = { width: 340, height: 210 };

interface Props {
  /** Close the tour, whether it was finished or abandoned. */
  onClose: () => void;
  /** Put the application into the state a step needs. */
  onPrepare: (step: TourStep) => void;
}

/** Where to cut the hole, in viewport coordinates. */
interface Spotlight {
  top: number;
  left: number;
  width: number;
  height: number;
}

export function GuidedTour({ onClose, onPrepare }: Props): React.JSX.Element | null {
  const [index, setIndex] = useState(0);
  const [spotlight, setSpotlight] = useState<Spotlight | null>(null);
  const [card, setCard] = useState(CARD_ESTIMATE);
  const cardRef = useRef<HTMLDivElement>(null);

  // Which optional features this deployment has, asked of the API rather than
  // guessed from the page.
  //
  // The first version waited a third of a second and then looked for each step's
  // element. That is a race, and it lost: the assistant's panel renders nothing
  // until its own status request comes back, so on a deployment slower than the
  // timer the tour decided there was no assistant and silently dropped the step
  // that explains the whole point of the thing.
  //
  // Asking the API is deterministic. A tour that sometimes omits a feature is
  // worse than one that never mentions it, because nobody knows to look.
  const { data: assistant, isPending: isAskingAboutAssistant } = useQuery({
    queryKey: ["ask-status"],
    queryFn: ({ signal }) => api.askStatus(signal),
    staleTime: Infinity,
    retry: false,
  });

  // Derived, not stored. Which steps exist follows from one answer, and the
  // answer cannot change underneath somebody midway through: the query is
  // cached for the life of the page, so once it resolves the list is fixed.
  //
  // Null until the answer arrives, so nothing is drawn that would have to
  // renumber itself a moment later.
  const steps = useMemo<TourStep[] | null>(
    () =>
      isAskingAboutAssistant
        ? null
        : TOUR_STEPS.filter(
            (candidate) => !candidate.needsAssistant || assistant?.isConfigured === true,
          ),
    [isAskingAboutAssistant, assistant],
  );

  const step = steps?.[index];
  const isFirst = index === 0;
  const isLast = steps !== null && index === steps.length - 1;

  // Put the application into the state this step needs before measuring, so the
  // element a step points at exists by the time we look for it.
  useEffect(() => {
    if (step) {
      onPrepare(step);
    }
  }, [step, onPrepare]);

  /** Measure the target, if this step has one that is on screen. */
  const measure = useCallback((): void => {
    if (!step?.target) {
      setSpotlight(null);
      return;
    }

    const element = document.querySelector(step.target);

    if (!element) {
      // Not an error. A step whose element has not appeared yet simply has no
      // hole cut for it, and the card centres instead of pointing at nothing.
      setSpotlight(null);
      return;
    }

    const box = element.getBoundingClientRect();

    setSpotlight({
      top: box.top - SPOTLIGHT_PADDING,
      left: box.left - SPOTLIGHT_PADDING,
      width: box.width + SPOTLIGHT_PADDING * 2,
      height: box.height + SPOTLIGHT_PADDING * 2,
    });
  }, [step]);

  // Measured after paint, and then again until the target turns up.
  //
  // A single measure is not enough, and a fixed second one only looked like it
  // was. The steps that need a part select one when they open, and the branch
  // grid does not exist until its own request comes back -- so the tour measured
  // an element that was not there yet, found nothing, and centred the card over
  // a step whose whole purpose was to point at that grid.
  //
  // So it keeps looking for a short while, and stops as soon as it finds it.
  // Polling is the right shape here: what is being waited for is another
  // component's fetch, which this one has no handle on.
  useLayoutEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    measure();

    if (!step?.target) {
      return;
    }

    const started = Date.now();

    const looking = setInterval(() => {
      const found = document.querySelector(step.target!) !== null;

      if (found || Date.now() - started > TARGET_WAIT_MS) {
        clearInterval(looking);
      }

      measure();
    }, TARGET_POLL_MS);

    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);

    return () => {
      clearInterval(looking);
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [measure, step]);

  /** Move on, or finish. */
  const advance = useCallback((): void => {
    if (isLast) {
      onClose();
      return;
    }
    setIndex((current) => current + 1);
  }, [isLast, onClose]);

  /** Go back, stopping at the first step. */
  const retreat = useCallback((): void => {
    setIndex((current) => Math.max(0, current - 1));
  }, []);

  useEffect(() => {
    cardRef.current?.focus();
  }, [index]);

  // The card's own size, measured rather than assumed. Each step's text is a
  // different length, so a fixed estimate is wrong by a different amount on
  // every step -- and the amount it is wrong by is how far the card overlaps
  // whatever the step is pointing at.
  useLayoutEffect(() => {
    const measured = cardRef.current?.getBoundingClientRect();

    if (!measured) {
      return;
    }

    // oxlint-disable-next-line react/set-state-in-effect
    setCard((current) =>
      Math.abs(current.width - measured.width) < 1 &&
      Math.abs(current.height - measured.height) < 1
        ? current
        : { width: measured.width, height: measured.height },
    );
  }, [index, steps]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      } else if (event.key === "ArrowRight" || event.key === "Enter") {
        event.preventDefault();
        advance();
      } else if (event.key === "ArrowLeft") {
        event.preventDefault();
        retreat();
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [advance, retreat, onClose]);

  // Nothing is drawn until the page has settled and the steps are known. A card
  // that appeared and then renumbered itself would be worse than a short pause.
  if (steps === null || !step) {
    return null;
  }

  return (
    <div className="tour" role="dialog" aria-modal="true" aria-labelledby="tour-title">
      <TourBackdrop spotlight={spotlight} />

      <div
        ref={cardRef}
        className="tour__card"
        style={cardPosition(spotlight, card)}
        tabIndex={-1}
      >
        <p className="tour__count">
          Step {index + 1} of {steps.length}
        </p>

        <h2 className="tour__title" id="tour-title">
          {step.title}
        </h2>

        <p className="tour__body">{step.body}</p>

        <div className="tour__actions">
          <button type="button" className="button button--quiet" onClick={onClose}>
            {isLast ? "Close" : "Skip tour"}
          </button>

          <span className="header__spacer" />

          {!isFirst && (
            <button type="button" className="button button--quiet" onClick={retreat}>
              Back
            </button>
          )}

          <button type="button" className="button" onClick={advance}>
            {isLast ? "Done" : "Next"}
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * The dimmed page, with a hole where the spotlight is.
 *
 * Four panels rather than one box-shadow, because a shadow large enough to cover
 * the page is a shadow the browser repaints on every scroll. Four rectangles cost
 * nothing and produce the same picture.
 */
function TourBackdrop({ spotlight }: { spotlight: Spotlight | null }): React.JSX.Element {
  if (!spotlight) {
    return <div className="tour__backdrop tour__backdrop--full" />;
  }

  const { top, left, width, height } = spotlight;

  return (
    <>
      <div className="tour__backdrop" style={{ top: 0, left: 0, right: 0, height: Math.max(0, top) }} />
      <div className="tour__backdrop" style={{ top: top + height, left: 0, right: 0, bottom: 0 }} />
      <div className="tour__backdrop" style={{ top, left: 0, width: Math.max(0, left), height }} />
      <div className="tour__backdrop" style={{ top, left: left + width, right: 0, height }} />
      <div className="tour__ring" style={{ top, left, width, height }} aria-hidden="true" />
    </>
  );
}

/**
 * Where to put the card so it does not cover what the step is pointing at.
 *
 * @param spotlight The hole, or null when the step has no target.
 * @param card How big the card actually is, measured.
 * @returns Inline position for the card.
 *
 * @remarks
 * The first version tried below the spotlight, then above, then clamped to the
 * top of the screen. That last fallback is what went wrong: on a step
 * spotlighting a full-height drawer, neither side fits, so the card was clamped
 * to the top -- directly over the table headers and the first five rows of the
 * thing the step exists to show.
 *
 * So it now tries all four sides and takes the first that genuinely clears the
 * spotlight. When none do, because the spotlight fills the viewport, the card
 * docks to the bottom: a table is read downwards, so the bottom is where it
 * hides the least, and its headers and first rows stay visible.
 */
function cardPosition(
  spotlight: Spotlight | null,
  card: { width: number; height: number },
): React.CSSProperties {
  if (!spotlight) {
    return { top: "50%", left: "50%", transform: "translate(-50%, -50%)" };
  }

  const view = { width: window.innerWidth, height: window.innerHeight };

  // Horizontally aligned with the spotlight where possible, so the card reads as
  // belonging to it rather than floating loose.
  const alignedLeft = clamp(spotlight.left, CARD_GAP, view.width - card.width - CARD_GAP);
  const alignedTop = clamp(spotlight.top, CARD_GAP, view.height - card.height - CARD_GAP);

  const below = spotlight.top + spotlight.height + CARD_GAP;
  const above = spotlight.top - card.height - CARD_GAP;
  const right = spotlight.left + spotlight.width + CARD_GAP;
  const left = spotlight.left - card.width - CARD_GAP;

  const candidates: React.CSSProperties[] = [
    ...(below + card.height <= view.height - CARD_GAP
      ? [{ top: below, left: alignedLeft }]
      : []),
    ...(above >= CARD_GAP ? [{ top: above, left: alignedLeft }] : []),
    ...(right + card.width <= view.width - CARD_GAP
      ? [{ top: alignedTop, left: right }]
      : []),
    ...(left >= CARD_GAP ? [{ top: alignedTop, left }] : []),
  ];

  if (candidates.length > 0) {
    return candidates[0];
  }

  // Nothing clears it. Docked at the bottom, where a table hides least.
  return {
    top: view.height - card.height - CARD_GAP,
    left: clamp(
      (view.width - card.width) / 2,
      CARD_GAP,
      view.width - card.width - CARD_GAP,
    ),
  };
}

/** Keep a value inside a range, tolerating a range narrower than the value. */
function clamp(value: number, lowest: number, highest: number): number {
  return Math.max(lowest, Math.min(value, Math.max(lowest, highest)));
}
