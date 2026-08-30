/**
 * Says out loud that this was built for a counter workstation.
 *
 * It works on a phone and it is not good on one: the branch grid wants width,
 * the record view wants more, and neither was designed for a column four inches
 * across. That is a decision rather than an oversight — the screen exists for
 * somebody standing at a trade counter with a customer in front of them and a
 * desktop under the counter, and designing for a thumb would have cost the
 * density that job actually needs.
 *
 * Shown only on a narrow window, and only as a note. Blocking a small screen
 * would be worse than serving it imperfectly: somebody opening a link on a phone
 * to see what this is should see it, and should also know they are not looking
 * at the thing at its best.
 */

export function BuiltForAWorkstation(): React.JSX.Element {
  return (
    <p className="workstation-note" role="note">
      <strong>Built for a counter workstation.</strong> This works on a phone but
      was not designed for one — the branch grid and the stored-record view both
      want the width of a desk monitor. Everything here is usable at this size;
      none of it is at its best.
    </p>
  );
}
