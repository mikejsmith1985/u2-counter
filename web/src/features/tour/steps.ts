/**
 * What the tour points at, in the order it points at it.
 *
 * Somebody opening this for the first time is looking at a screen full of
 * controls that do not announce themselves. The pickers open lists, the
 * assistant reaches a real database through a protocol they have probably not
 * met, the table builds itself from a dictionary, and the strip along the bottom
 * makes claims nobody asked it to make. None of that is guessable.
 *
 * So the tour covers everything a reader would otherwise have to find by
 * accident, and it drives the application rather than describing it: a step that
 * needs a part selected selects one, so the reader watches the thing happen.
 *
 * The wording assumes no knowledge of MultiValue and none of MCP. Anybody who
 * already knows will skim; anybody who does not is the person this is for.
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
   * everything, which is right for the opening step.
   */
  target?: string;
  /**
   * Whether this step is about the assistant, and so has nothing to point at on
   * a deployment with no API key configured.
   *
   * The only reason a step is ever dropped, and it is declared rather than
   * detected. The first version looked for each step's element after a short
   * delay, which is a race: the assistant's panel renders nothing until its own
   * request comes back, so on a slower deployment the tour concluded there was
   * no assistant and silently skipped the two steps explaining the whole point
   * of the thing.
   */
  needsAssistant?: boolean;
  /** Whether this step needs a part selected before it can be shown. */
  needsPart?: boolean;
  /** Whether this step needs the record drawer open. */
  needsRecord?: boolean;
  /** Whether this step needs the activity panel open. */
  needsActivity?: boolean;
  /** Whether this step needs the "use your own data" panel open. */
  needsConnect?: boolean;
}

export const TOUR_STEPS: TourStep[] = [
  {
    id: "welcome",
    title: "A trade counter, over a 1970s database",
    body:
      "One question, asked all day at a wholesaler's counter: can I promise this " +
      "part to this customer today? Behind it is a MultiValue database — the kind " +
      "that still runs distribution. Escape leaves at any point.",
  },
  {
    id: "ask",
    title: "Ask it in plain words",
    body:
      "Type a question and Claude answers it — but only from what it reads out of " +
      "this database. It has no memory of this data and cannot invent a number: " +
      "every figure in the answer came back from a lookup you are about to see.",
    target: "[data-tour='ask']",
    needsAssistant: true,
  },
  {
    id: "mcp",
    title: "How it reaches the database",
    body:
      "Under each answer is every call it made, and what came back. Claude never " +
      "touches the database itself — it asks a separate server over a protocol " +
      "called MCP, and that server offers exactly three tools. All three are " +
      "reads. There is no write in the list for it to reach for.",
    target: "[data-tour='ask']",
    needsAssistant: true,
  },
  {
    id: "explore",
    title: "This table built itself",
    body:
      "No field names are written into this page. The files come from the account " +
      "and the columns from each file's own dictionary — which is how a MultiValue " +
      "database describes itself. Point it at your database and it shows yours.",
    target: "[data-tour='explore']",
  },
  {
    id: "parts",
    title: "Pick a part",
    body:
      "A dropdown, not a search box you have to guess at. Click it and the " +
      "catalogue opens — three thousand parts. Typing narrows the list, and a part " +
      "number read off a box matches however its hyphens survived the journey.",
    target: "[data-tour='part-search']",
  },
  {
    id: "customer",
    title: "Say who is asking",
    body:
      "Price depends on the customer's contract, so the customer is chosen once " +
      "and kept for the call. Click to open the account file; each row shows the " +
      "price class you would be quoting against.",
    target: "[data-tour='customer-picker']",
  },
  {
    id: "branches",
    title: "Free to sell, not on hand",
    body:
      "On hand minus what orders already hold. The distinction is the whole job: a " +
      "branch with forty on the shelf and thirty-nine promised has one to sell, " +
      "and promising the forty is how a customer gets let down.",
    target: "[data-tour='branch-grid']",
    needsPart: true,
  },
  {
    id: "record",
    title: "The record, exactly as stored",
    body:
      "Real attribute and value marks, not JSON dressed up to look like them. " +
      "Position three of every field describes the same branch — the trap this " +
      "kind of data sets for anyone who has only met SQL.",
    target: ".drawer__panel",
    needsPart: true,
    needsRecord: true,
  },
  {
    id: "activity",
    title: "Who asked what",
    body:
      "Every request is written down against the person who made it. That matters " +
      "here because the database only ever sees one shared login — so if this " +
      "application does not name the person, nothing does.",
    target: ".drawer__panel",
    needsActivity: true,
  },
  {
    id: "governance",
    title: "What it may and may not do",
    body:
      "The strip along the bottom says the awkward parts out loud: one shared " +
      "database login, demonstration data, read-only. It also says the whole thing " +
      "powers itself down when idle, which is why your first visit was slow.",
    target: "[data-tour='governance']",
  },
  {
    id: "connect",
    title: "Point it at your own database",
    body:
      "Four settings and one command, which connects, proves the write refusal by " +
      "attempting one, and reads your dictionaries. Your data and your API key — " +
      "nothing here asks you to trust mine.",
    target: ".drawer__panel",
    needsConnect: true,
  },
  {
    id: "shortcuts",
    title: "Two shortcuts, and you are done",
    body:
      "Slash jumps to the part box, R opens the stored record, Escape closes " +
      "whatever is open. The tour is in the header if you want it again.",
    target: "[data-tour='shortcuts']",
  },
];
