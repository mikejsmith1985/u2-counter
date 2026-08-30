/**
 * The parsed record laid out so its parallel fields line up.
 *
 * The old view listed each field on its own line, values separated by dots, and
 * left the reader to take "position three of every field describes the same
 * branch" on trust. Read against a real record it looks false: single-valued
 * fields have no third position at all, and nothing on screen puts the third
 * value of one field anywhere near the third value of the next.
 *
 * So the fields that run in parallel become columns and each position becomes a
 * row. The claim then does not need making — position three is a row, and what
 * belongs together is beside itself.
 *
 * The fields that hold one value are shown apart, above, because they describe
 * the record rather than any one position. Mixing them into the grid is what
 * made the original claim look wrong: a description has no third branch, and
 * showing it as though it might have implies a structure that is not there.
 *
 * A record whose multi-valued fields disagree on length is shown as it is, with
 * the disagreement pointed at. That is the failure the write path exists to
 * prevent, and hiding it here would be hiding the thing worth seeing.
 */

interface Props {
  /** Each field's values, keyed by position, exactly as the API parsed them. */
  parsed: Record<string, string[]>;
}

export function ParallelFields({ parsed }: Props): React.JSX.Element {
  const fields = Object.entries(parsed)
    .map(([position, values]) => ({ position: Number(position), values }))
    .sort((left, right) => left.position - right.position);

  const single = fields.filter((field) => field.values.length <= 1);
  const parallel = fields.filter((field) => field.values.length > 1);

  const lengths = new Set(parallel.map((field) => field.values.length));
  const rows = Math.max(0, ...parallel.map((field) => field.values.length));

  return (
    <div className="parallel">
      {single.length > 0 && (
        <div className="parallel__single">
          <p className="parallel__caption">
            One value each — these describe the record, not a position in it.
          </p>
          {single.map((field) => (
            <div key={field.position} className="parallel__line">
              <span className="parallel__position">field {field.position}</span>
              <span className="parallel__value">{field.values[0] ?? ""}</span>
            </div>
          ))}
        </div>
      )}

      {parallel.length > 0 && (
        <>
          <p className="parallel__caption">
            {lengths.size === 1 ? (
              <>
                Many values each, and every one holds{" "}
                <strong>{[...lengths][0]}</strong>. Read across a row: that is what
                &ldquo;position three describes the same branch&rdquo; means.
              </>
            ) : (
              <>
                Many values each — and they <strong>do not agree on how many</strong>.
                A row is no longer one thing, which is exactly the damage a careless
                write does to this kind of record.
              </>
            )}
          </p>

          <div className="scroll">
            <table className="parallel__table">
              <thead>
                <tr>
                  <th scope="col">Position</th>
                  {parallel.map((field) => (
                    <th key={field.position} scope="col">
                      field {field.position}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {Array.from({ length: rows }, (_unused, index) => (
                  <tr key={index}>
                    <th scope="row">{index + 1}</th>
                    {parallel.map((field) => (
                      <td
                        key={field.position}
                        className={
                          index >= field.values.length ? "parallel__missing" : undefined
                        }
                      >
                        {index < field.values.length ? (
                          field.values[index]
                        ) : (
                          <span aria-label="no value at this position">—</span>
                        )}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
