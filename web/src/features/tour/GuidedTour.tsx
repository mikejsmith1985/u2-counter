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

/** Roughly the card's size, for deciding which side of the target it fits on. */
const CARD_WIDTH = 340;
const CARD_HEIGHT = 210;

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
        style={cardPosition(spotlight)}
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
 * Where to put the card so it neither covers the spotlight nor leaves the page.
 *
 * @param spotlight The hole, or null when the step has no target.
 * @returns Inline position for the card.
 */
function cardPosition(spotlight: Spotlight | null): React.CSSProperties {
  if (!spotlight) {
    return { top: "50%", left: "50%", transform: "translate(-50%, -50%)" };
  }

  const below = spotlight.top + spotlight.height + CARD_GAP;
  const fitsBelow = below + CARD_HEIGHT < window.innerHeight;

  const top = fitsBelow
    ? below
    : Math.max(CARD_GAP, spotlight.top - CARD_HEIGHT - CARD_GAP);

  // Kept fully on screen horizontally. A card half off the right edge is one
  // whose buttons cannot be reached.
  const left = Math.min(
    Math.max(CARD_GAP, spotlight.left),
    Math.max(CARD_GAP, window.innerWidth - CARD_WIDTH - CARD_GAP),
  );

  return { top, left };
}
