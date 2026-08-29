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

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { TOUR_STEPS, type TourStep } from "./steps";

/**
 * How long to let the application paint before deciding which steps to show.
 *
 * The optional panels fetch before they render, so their anchors do not exist on
 * the tour's first frame. Long enough for that, short enough that nobody reads
 * it as a delay.
 */
const SETTLE_MS = 350;

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

  // Only the steps whose target is actually on the page.
  //
  // Two are conditional. The assistant is absent when no key is configured, and
  // the explorer is absent once a part has been selected. A tour that stopped on
  // either would dim the screen and point at nothing, which reads as broken --
  // and on a deployment without a key it would advertise a feature that is not
  // there.
  //
  // Decided after the application has painted, not during the tour's first
  // render. Computing it at mount ran before the panels had rendered their
  // anchors, so every optional step was judged missing and the tour offered one
  // step of eight. The panels also fetch before they appear, which is why this
  // waits rather than reading on the next tick.
  //
  // Decided once, because the tour itself selects a part partway through, and a
  // list recomputed after that would renumber the steps behind somebody midway.
  //
  // Only steps the tour cannot conjure are tested this way -- see canBeShown.
  const [steps, setSteps] = useState<TourStep[] | null>(null);

  useEffect(() => {
    const settle = setTimeout(() => {
      setSteps(TOUR_STEPS.filter(canBeShown));
    }, SETTLE_MS);

    return () => clearTimeout(settle);
  }, []);

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

  // Measured after paint, and again shortly after: a step that opens a drawer
  // changes the layout, and measuring only once catches the page mid-move.
  //
  // This is the case the "no setState in an effect" rule exempts. Where the
  // spotlight goes is a fact about laid-out geometry, which does not exist until
  // the browser has laid the page out -- so it cannot be derived during render
  // and cannot come from the event that caused the change. The DOM is the
  // external system here.
  useLayoutEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    measure();

    const settle = setTimeout(measure, 120);
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);

    return () => {
      clearTimeout(settle);
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [measure]);

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
 * Whether a step can be shown at all.
 *
 * @param step The step being considered.
 * @returns True when it has something to point at, now or later.
 *
 * @remarks
 * The distinction is which absences the tour can fix. A step that declares it
 * needs a part, a record or the activity panel is a step whose target the tour
 * creates itself when it gets there, so its target is legitimately missing at
 * the start and must not be filtered out. Filtering on presence alone dropped
 * exactly those steps -- the branch grid and the stored record, two of the most
 * worth showing -- because the tour had not yet selected the part that makes
 * them exist.
 *
 * Everything else is absent for a reason the tour cannot change: no API key
 * means no assistant, and a selected part means no explorer.
 */
function canBeShown(step: TourStep): boolean {
  if (step.target === undefined) {
    return true;
  }

  if (step.needsPart || step.needsRecord || step.needsActivity) {
    return true;
  }

  return document.querySelector(step.target) !== null;
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
