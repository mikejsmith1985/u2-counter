/**
 * What the tour points at, in the order it points at it.
 *
 * Somebody opening this for the first time is looking at a search box and a
 * mostly empty screen. Everything the application is actually for — the branch
 * grid, the contract price, the stored record, the audit trail — is behind a
 * part number they have no way to guess. The pickers fixed the guessing; this
 * fixes the not-knowing-there-is-anything-to-look-for.
 *
 * The tour drives the application rather than describing it. A step that needs a
 * part selected selects one, so the reader watches the thing happen instead of
 * being told it would. That is the difference between a tour and a manual.
 *
 * Each step names its target with a CSS selector rather than a ref, so a step
 * whose element is not on screen can be skipped instead of pointing at nothing.
 * Steps are data because the order and the wording are the parts most likely to
 * change, and neither should require touching the overlay that draws them.
 */

/** A part that exists in the demonstration data, used to drive the tour. */
export const TOUR_PART_NUMBER = "E-BRK00008";

/** One stop on the tour. */
export interface TourStep {
  /** Stable id, used as a React key and in tests. */
  id: string;
  /** The heading on the card. */
  title: string;
  /** What to say. One or two sentences; this is a tour, not documentation. */
  body: string;
  /**
   * What to spotlight, as a CSS selector. Absent means centre the card and dim
   * everything, which is right for the opening and closing steps.
   */
  target?: string;
  /**
   * Whether this step needs a part selected before it can be shown.
   *
   * The branch grid, the price and the record only exist once a part has been
   * chosen, so the tour chooses one. Declared here rather than done inside the
   * overlay so that the overlay stays a thing that draws steps.
   */
  needsPart?: boolean;
  /** Whether this step needs the record drawer open. */
  needsRecord?: boolean;
  /** Whether this step needs the activity panel open. */
  needsActivity?: boolean;
}

export const TOUR_STEPS: TourStep[] = [
  {
    id: "welcome",
    title: "A counter, over a 1970s database",
    body:
      "This answers one question a trade counter asks all day: can I promise this " +
      "part to this customer today? The data behind it is a MultiValue database — " +
      "the kind that still runs distribution. Two minutes, and you can leave at " +
      "any point with Escape.",
  },
  {
    id: "ask",
    title: "Ask it in words",
    body:
      "Claude answers from the database, not from memory — and every call it makes " +
      "is listed underneath, down to the raw record with its separators marked. " +
      "That transcript is the point: it is how you tell a real MultiValue record " +
      "from a convincing imitation of one.",
    target: "[data-tour='ask']",
  },
  {
    id: "explore",
    title: "The screen is built from your dictionary",
    body:
      "No field names are written into this page. The files come from the account, " +
      "the fields and headings from each file's own dictionary. Point it at a " +
      "different database and it shows that one — there is no mapping to write, " +
      "because a MultiValue file already carries it.",
    target: "[data-tour='explore']",
  },
  {
    id: "parts",
    title: "Start with a part",
    body:
      "Click the box and the catalogue opens — three thousand parts, no need to " +
      "know one. Typing narrows it, and a part number found on a box matches " +
      "however its hyphens survived the journey.",
    target: "[data-tour='part-search']",
  },
  {
    id: "customer",
    title: "Then say who is asking",
    body:
      "Price depends on the customer's contract, so the customer is chosen once " +
      "and kept for the call. Click to browse the account file; the price class " +
      "is on every row.",
    target: "[data-tour='customer-picker']",
  },
  {
    id: "branches",
    title: "Free to sell, not on hand",
    body:
      "On hand minus what orders already hold. The distinction is the whole point: " +
      "a branch with forty on the shelf and thirty-nine promised has one to sell, " +
      "and promising the forty is how a customer is let down.",
    target: "[data-tour='branch-grid']",
    needsPart: true,
  },
  {
    id: "record",
    title: "The record, exactly as stored",
    body:
      "Attribute, value and subvalue marks — the real separators, not JSON dressed " +
      "up. Position n of every field belongs to the same branch, which is the trap " +
      "this kind of data sets for anyone who has only met SQL.",
    // The panel, not the wrapper around it. The drawer is positioned fixed, so
    // the element containing it has almost no height -- spotlighting that drew a
    // two-thousand-pixel strip twelve pixels tall across the screen.
    target: ".drawer__panel",
    needsPart: true,
    needsRecord: true,
  },
  {
    id: "governance",
    title: "Who asked, and what it may do",
    body:
      "The database sees one shared login, so it cannot tell callers apart — said " +
      "plainly rather than buried. This application names the person instead, and " +
      "it cannot write: no mutating verb exists in the path to the database.",
    target: "[data-tour='governance']",
  },
];
