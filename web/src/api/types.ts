/**
 * The shapes the API returns.
 *
 * Two fields appear on nearly everything and matter more than they look.
 * `isComplete` says whether an answer is whole; `isDemonstrationData` says the
 * figures are not production data. Both are rendered rather than dropped,
 * because a representative repeats what the screen says to a customer.
 */

/** What every response carries about its own trustworthiness. */
export interface ResponseEnvelope {
  /** False when a limit was applied, so the answer may be partial. */
  isComplete: boolean;
  /** What to tell the user when the answer may be partial. */
  warning: string | null;
  /** Always true here. Rendered wherever figures appear. */
  isDemonstrationData: boolean;
  /** When the figures were read. */
  retrievedAt: string;
}

/** One result of a catalogue search. */
export interface SearchResult {
  partNumber: string;
  description: string;
  manufacturer: string;
  unitOfMeasure: string;
  /** Shown, marked, never hidden: customers ask for parts discontinued last month. */
  isDiscontinued: boolean;
  totalFreeToSell: number;
}

export interface SearchResponse {
  results: SearchResult[];
  envelope: ResponseEnvelope;
}

export interface PartSummary {
  partNumber: string;
  description: string;
  manufacturer: string;
  manufacturerPartNumber: string;
  unitOfMeasure: string;
  isDiscontinued: boolean;
}

/**
 * How a branch's position reads at a glance.
 *
 * Three states rather than a boolean: "we have twelve but they are all spoken
 * for" is a different answer to a customer than "we have none".
 */
export type StockState = "Available" | "AllCommitted" | "None";

export interface BranchAvailability {
  branchCode: string;
  branchName: string;
  city: string;
  onHand: number;
  committed: number;
  freeToSell: number;
  /** Expected from a supplier. Shown, never counted as available. */
  onOrder: number;
  bin: string;
  stockState: StockState;
  /** False when the inventory record names a branch the directory does not hold. */
  isBranchKnown: boolean;
}

export interface DisregardedTermsView {
  termsDescription: string;
  reason: string;
}

export interface PricingView {
  listPrice: number;
  netPrice: number;
  multiplier: number | null;
  basis: "Contract" | "List";
  termsDescription: string | null;
  /** Terms on file that did not apply, so an unexpected price can be explained. */
  disregardedTerms: DisregardedTermsView[];
}

export interface AvailabilityResponse {
  part: PartSummary;
  /**
   * False when the part has no inventory record. Distinct from zero stock:
   * saying "none" when nobody has counted is a different, worse answer.
   */
  isStockKnown: boolean;
  totalFreeToSell: number;
  branches: BranchAvailability[];
  pricing: PricingView;
  envelope: ResponseEnvelope;
}

export interface CommitmentView {
  orderNumber: string;
  customerName: string;
  quantity: number;
  state: string;
  promisedDate: string;
}

export interface CommitmentsResponse {
  branchCode: string;
  committedTotal: number;
  accountedFor: number;
  /** Committed units no listed order explains. Shown, never hidden. */
  unaccounted: number;
  commitments: CommitmentView[];
  envelope: ResponseEnvelope;
}

export interface MarkDescription {
  character: string;
  code: number;
  name: string;
  separates: string;
}

export interface RecordResponse {
  fileName: string;
  recordId: string;
  /** The record exactly as stored, separators included and not stripped. */
  rawRecord: string;
  marks: MarkDescription[];
  parsed: Record<string, string[]>;
  query: string;
  envelope: ResponseEnvelope;
}

export interface CustomerSummary {
  accountNumber: string;
  name: string;
  /** First line of their address. Not a city -- the record holds no city. */
  addressLine: string;
  priceClass: string;
}

export interface CustomerSearchResponse {
  results: CustomerSummary[];
  envelope: ResponseEnvelope;
}

export interface SessionResponse {
  displayName: string;
  userSubject: string;
  homeBranchCode: string;
  selectedCustomerAccount: string | null;
  isReadOnly: boolean;
  databaseLogin: string;
  /** Surfaced to the user: a shared login is a limitation they should know about. */
  databaseLoginIsShared: boolean;
  isDemonstrationData: boolean;
}

/**
 * A demonstration identity.
 *
 * Not an account. There is no password behind it, and choosing one proves
 * nothing -- it exists so the activity record has a person to name and the
 * branch grid has a home branch to lead with.
 */
export interface Persona {
  subject: string;
  displayName: string;
  homeBranchCode: string;
  /** True for every persona: this release answers questions and changes nothing. */
  isReadOnly: boolean;
  /** What this persona is useful for showing. */
  description: string;
}

export interface ActivityEntry {
  occurredAt: string;
  displayName: string;
  action: string;
  targetKey: string;
  databaseLogin: string;
  databaseLoginIsShared: boolean;
  durationMs: number;
  outcome: string;
}

export interface ActivityResponse {
  entries: ActivityEntry[];
}

/**
 * A part as a picker row shows it.
 *
 * No quantities. Stock is read live when a part is opened, because a cached
 * quantity is a promise to a customer that cannot be kept -- and a picker exists
 * to find a part, not to report on one.
 */
export interface PartSummary {
  partNumber: string;
  description: string;
  manufacturer: string;
  manufacturerPartNumber: string;
  unitOfMeasure: string;
  isDiscontinued: boolean;
}

/**
 * What browsing returned, and how much there is altogether.
 *
 * The total is separate from the page on purpose. A page of twenty with no total
 * tells a reader they are looking at everything, which for a three thousand part
 * catalogue is wrong by two orders of magnitude.
 */
export interface BrowseResponse<TItem> {
  results: TItem[];
  totalCount: number;
  envelope: ResponseEnvelope;
}
