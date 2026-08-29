/**
 * The counter screen.
 *
 * One question, answered on one screen: does this part exist, where is it, what
 * does this customer pay, and what is already spoken for. Everything is
 * reachable from the keyboard, because the person using it has a telephone in
 * one hand.
 */

import { useCallback, useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../api/client";
import { PartSearch } from "../features/search/PartSearch";
import { BranchGrid } from "../features/availability/BranchGrid";
import { PricingPanel } from "../features/pricing/PricingPanel";
import { BranchCommitments } from "../features/commitments/BranchCommitments";
import { RecordDrawer } from "../features/record/RecordDrawer";
import { GovernanceStrip } from "../features/governance/GovernanceStrip";
import { ActivityPanel } from "../features/governance/ActivityPanel";
import { CustomerSelector } from "../features/pricing/CustomerSelector";
import { buildSummary } from "../features/availability/copySummary";
import {
  EmptyState,
  FailureState,
  LoadingState,
  StockUnknownState,
} from "../features/availability/states/ResultStates";

export function App(): React.JSX.Element {
  const [partNumber, setPartNumber] = useState<string | null>(null);
  const [expandedBranch, setExpandedBranch] = useState<string | null>(null);
  const [isRecordOpen, setIsRecordOpen] = useState(false);
  const [isActivityOpen, setIsActivityOpen] = useState(false);
  const [customerAccount, setCustomerAccount] = useState<string | null>(null);
  const [customerName, setCustomerName] = useState<string | null>(null);
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");

  const { data: session } = useQuery({
    queryKey: ["session"],
    queryFn: ({ signal }) => api.session(signal),
    retry: false,
  });

  const {
    data: availability,
    error,
    isPending,
    refetch,
  } = useQuery({
    queryKey: ["availability", partNumber, customerAccount],
    queryFn: ({ signal }) => api.availability(partNumber!, customerAccount, signal),
    enabled: partNumber !== null,
  });

  const failure = error instanceof ApiFailure ? error : null;

  // `R` opens the record view. Not a shortcut anyone needs on a call, but the
  // one a colleague checking the data reaches for repeatedly.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      const target = event.target as HTMLElement | null;
      const isTyping = target?.tagName === "INPUT" || target?.tagName === "TEXTAREA";

      if (isTyping || event.metaKey || event.ctrlKey) {
        return;
      }

      if ((event.key === "r" || event.key === "R") && partNumber) {
        event.preventDefault();
        setIsRecordOpen(true);
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [partNumber]);

  const selectPart = useCallback((chosen: string) => {
    setPartNumber(chosen);
    setExpandedBranch(null);
    setIsRecordOpen(false);
    setCopyState("idle");
  }, []);

  async function copySummary(): Promise<void> {
    if (!availability) return;

    try {
      await navigator.clipboard.writeText(buildSummary(availability, customerName));
      setCopyState("copied");
      setTimeout(() => setCopyState("idle"), 2000);
    } catch {
      // Clipboard access can be refused by the browser. Saying so beats a
      // button that silently does nothing.
      setCopyState("failed");
    }
  }

  const expanded = availability?.branches.find(
    (branch) => branch.branchCode === expandedBranch,
  );

  return (
    <div className="app">
      <header className="header">
        <span className="header__mark">Counter</span>
        <PartSearch onSelect={selectPart} />
        <CustomerSelector
          selectedAccount={customerAccount}
          onSelect={(account, name) => {
            setCustomerAccount(account);
            setCustomerName(name);
          }}
        />
        <span className="header__spacer" />
        <span className="hints" aria-hidden="true">
          <span>
            <kbd>/</kbd> search
          </span>
          <span>
            <kbd>R</kbd> record
          </span>
          <span>
            <kbd>Esc</kbd> close
          </span>
        </span>
      </header>

      <main className="main">
        {partNumber === null && <EmptyState />}

        {partNumber !== null && isPending && <LoadingState />}

        {partNumber !== null && failure && (
          <FailureState
            failure={failure}
            partNumber={partNumber}
            onRetry={() => void refetch()}
          />
        )}

        {availability && (
          <>
            {!availability.envelope.isComplete && availability.envelope.warning && (
              <p className="warning" role="status">
                {availability.envelope.warning}
              </p>
            )}

            <div className="part-heading">
              <span className="part-heading__number">{availability.part.partNumber}</span>
              <span className="part-heading__description">
                {availability.part.description}
              </span>
              <span className="pricing__label">
                {availability.part.manufacturer}
                {availability.part.isDiscontinued && " · discontinued"}
              </span>
            </div>

            {availability.isStockKnown ? (
              <>
                <div className="headline">
                  <span
                    className={
                      availability.totalFreeToSell > 0
                        ? "headline__figure headline__figure--available figures"
                        : "headline__figure headline__figure--none figures"
                    }
                  >
                    {availability.totalFreeToSell}
                  </span>
                  <span className="headline__where">
                    {availability.totalFreeToSell > 0
                      ? `free to sell across ${availability.branches.length} branches`
                      : "free to sell anywhere — every unit is committed or absent"}
                  </span>

                  <span className="header__spacer" />

                  <button type="button" className="button button--quiet" onClick={() => void copySummary()}>
                    {copyState === "copied"
                      ? "Copied"
                      : copyState === "failed"
                        ? "Could not copy"
                        : "Copy summary"}
                  </button>

                  <button
                    type="button"
                    className="button button--quiet"
                    onClick={() => setIsRecordOpen(true)}
                  >
                    Show the record
                  </button>
                </div>

                <BranchGrid
                  branches={availability.branches}
                  homeBranchCode={session?.homeBranchCode ?? ""}
                  onExpand={setExpandedBranch}
                />
              </>
            ) : (
              <StockUnknownState />
            )}

            <PricingPanel pricing={availability.pricing} customerName={customerName} />

            {expanded && partNumber && (
              <BranchCommitments
                partNumber={partNumber}
                branchCode={expanded.branchCode}
                branchName={expanded.branchName}
                onClose={() => setExpandedBranch(null)}
              />
            )}
          </>
        )}
      </main>

      <GovernanceStrip session={session ?? null} onShowActivity={() => setIsActivityOpen(true)} />

      {isRecordOpen && partNumber && (
        <RecordDrawer partNumber={partNumber} onClose={() => setIsRecordOpen(false)} />
      )}

      {isActivityOpen && <ActivityPanel onClose={() => setIsActivityOpen(false)} />}
    </div>
  );
}
