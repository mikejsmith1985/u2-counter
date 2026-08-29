/**
 * The boundary between looking and changing, made visible.
 *
 * This release answers questions and changes nothing. The honest way to present
 * that is not to leave the screen looking as though editing merely has not been
 * built yet — someone will eventually assume a control is missing and go looking
 * for it, or worse, assume it is there.
 *
 * So anything that would change ERP data is wrapped here. What renders is a
 * disabled control with the reason attached, in the same place the working
 * control would be. A reviewer can see exactly where the line falls, and a user
 * can see that it is a decision rather than an omission.
 *
 * The gate is not a security control and must never be treated as one. Nothing a
 * browser renders can be. The actual guarantee is on the other side: the API
 * offers no route that writes, the reader may call only tools that read, and the
 * ERP files are proved byte-identical after the whole test suite. This is the
 * explanation of that guarantee, not the guarantee itself.
 */

import type { ReactNode } from "react";

interface Props {
  /**
   * Whether the current session may change anything.
   *
   * Always false in this release. Kept as a parameter rather than assumed so the
   * screens do not have to be rewritten when it stops being.
   */
  canWrite: boolean;
  /** What the blocked action would have done, as a person would say it. */
  action: string;
  /**
   * The control that would perform it, rendered only when writing is possible.
   *
   * Optional, because this release has controls that exist only as the disabled
   * shape of themselves. Requiring a placeholder child for those would put an
   * empty element in the markup whose only purpose was to satisfy a type.
   */
  children?: ReactNode;
}

export function ReadOnlyGate({ canWrite, action, children }: Props): React.JSX.Element {
  if (canWrite) {
    return <>{children}</>;
  }

  return (
    <span className="read-only-gate">
      <button
        type="button"
        className="button button--quiet"
        disabled
        aria-disabled="true"
        // Named in the accessible label as well as the tooltip: a screen-reader
        // user reaching a disabled control otherwise learns only that it is
        // disabled, which is the least useful half of the message.
        aria-describedby="read-only-reason"
      >
        {action}
      </button>
      <span className="read-only-gate__reason" id="read-only-reason">
        Read only — this application never writes to the ERP
      </span>
    </span>
  );
}

/**
 * The standing statement of what this application does to the ERP.
 *
 * Rendered once, near the governance strip, so the claim has somewhere to live
 * that is not attached to a particular button.
 */
export function ReadOnlyNotice(): React.JSX.Element {
  return (
    <p className="read-only-notice" role="note">
      <strong>Read only.</strong> Every figure here is read live and nothing is
      written back. The MCP server this reaches the database through is permitted
      four tools, all of which read; the write and delete tools are named in a
      list the build fails on if anything references them; and the integration
      suite hashes every ERP file before and after exercising every screen.
    </p>
  );
}
