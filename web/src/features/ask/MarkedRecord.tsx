/**
 * A stored record with its separators made visible, and their code points named.
 *
 * This is the evidence, so it shows the thing rather than describing it. The
 * separators are real characters in the bytes that came back — 254 between
 * fields, 253 between values, 252 between subvalues — and naming the code point
 * beside each one is what turns "those look like separators" into something a
 * reader can check for themselves.
 *
 * It matters because the obvious suspicion is the right one to answer: a screen
 * showing a MultiValue record could be showing a relational row with separators
 * pasted in. It could not be showing these bytes.
 */

/** Attribute mark: separates fields. */
const AM = "þ";

/** Value mark: separates values within a field. */
const VM = "ý";

/** Subvalue mark: separates sub-items within a value. */
const SM = "ü";

/** How each separator is shown and explained. */
const MARKS: Record<string, { label: string; code: number; className: string }> = {
  [AM]: { label: "AM", code: 254, className: "mark mark--am" },
  [VM]: { label: "VM", code: 253, className: "mark mark--vm" },
  [SM]: { label: "SM", code: 252, className: "mark mark--sm" },
};

interface Props {
  /** The record exactly as the database returned it. */
  raw: string;
}

export function MarkedRecord({ raw }: Props): React.JSX.Element {
  const pieces = splitKeepingMarks(raw);

  const counts = {
    fields: raw.split(AM).length,
    values: raw.split(AM)[0]?.split(VM).length ?? 0,
  };

  return (
    <div className="marked">
      <pre className="marked__record">
        {pieces.map((piece, index) => {
          const mark = MARKS[piece];

          return mark ? (
            <span
              key={index}
              className={mark.className}
              title={`${mark.label} — character ${mark.code}`}
            >
              {mark.label}
            </span>
          ) : (
            <span key={index}>{piece}</span>
          );
        })}
      </pre>

      <p className="marked__legend">
        {counts.fields} field{counts.fields === 1 ? "" : "s"}, {counts.values} value
        {counts.values === 1 ? "" : "s"} in the first — separated by characters 254, 253
        and 252. Position <em>n</em> of every field belongs to the same branch.
      </p>
    </div>
  );
}

/**
 * Split a record into text and separators, keeping the separators.
 *
 * @param raw The record.
 * @returns Alternating text and single-separator pieces.
 *
 * @remarks
 * A plain split throws the separators away, and the separators are the whole
 * point of this component. Kept as their own pieces so each can be rendered as
 * the labelled thing it is rather than as an unprintable character the browser
 * draws as a box.
 */
function splitKeepingMarks(raw: string): string[] {
  const pieces: string[] = [];
  let current = "";

  for (const character of raw) {
    if (MARKS[character]) {
      if (current) {
        pieces.push(current);
        current = "";
      }
      pieces.push(character);
    } else {
      current += character;
    }
  }

  if (current) {
    pieces.push(current);
  }

  return pieces;
}
