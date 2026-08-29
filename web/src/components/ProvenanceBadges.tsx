/**
 * Whose figures these are, said wherever figures appear.
 *
 * The badges belong on the governance strip, and they belong again on anything
 * that covers it. The record drawer is the most screenshot-worthy screen in this
 * application — it is the one that shows a real MultiValue record — and a
 * screenshot of it with no demonstration badge is an image someone can present,
 * entirely honestly, as production data.
 *
 * Repeating three short labels is a small cost against that.
 */

interface Props {
  /** Whether the session may change anything. False throughout this release. */
  isReadOnly: boolean;
  /** Whether the database login serves more than one person. */
  isSharedLogin: boolean;
  /** Whether these figures are demonstration data. */
  isDemonstrationData: boolean;
}

export function ProvenanceBadges({
  isReadOnly,
  isSharedLogin,
  isDemonstrationData,
}: Props): React.JSX.Element {
  return (
    <>
      {isSharedLogin && (
        <span
          className="governance__badge governance__badge--shared"
          title="Several people use this database account, so the database cannot tell them apart"
        >
          SHARED LOGIN
        </span>
      )}

      {isReadOnly && (
        <span className="governance__badge governance__badge--readonly">READ ONLY</span>
      )}

      {isDemonstrationData && (
        <span className="governance__badge governance__badge--demo">DEMONSTRATION DATA</span>
      )}
    </>
  );
}
