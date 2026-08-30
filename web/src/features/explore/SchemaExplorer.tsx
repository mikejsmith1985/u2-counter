/**
 * A screen with no field names in it, built from the database's own dictionary.
 *
 * Every other screen here is compiled to one layout, which is right for a
 * counter — the people using it want the same four numbers in the same place
 * every time. It is wrong for anybody evaluating this against their own system:
 * their files are not called PRODUCT and INVENTORY, and their fields are not in
 * these positions, so a fixed layout tells them nothing about their own data.
 *
 * So this reads the account. The file list comes from the account, the field
 * list from each file's dictionary, and the column headings from what the
 * dictionary calls them. Point it at a different database and it shows that
 * database — there is no mapping to write, because a MultiValue file already
 * carries one.
 *
 * The selection that ran is shown at the bottom. A screen that says what it
 * asked is one whose answer can be checked.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { DictionaryField } from "../../api/types";
import { UpdateValue } from "./UpdateValue";

/** How many characters of a value to show before trimming it. */
const VALUE_WIDTH = 28;

export function SchemaExplorer(): React.JSX.Element {
  const [file, setFile] = useState<string>("");
  const [position, setPosition] = useState<number>(0);
  const [value, setValue] = useState<string>("");
  // Contains by default. Exact match is what a MultiValue SELECT does, and as
  // the only option on a screen for exploring an unfamiliar account it reads as
  // broken: "aurora" against a branch called "Aurora" returned nothing, with no
  // hint that case was the reason.
  const [isExact, setIsExact] = useState(false);

  const [applied, setApplied] = useState<{ position: number; value: string; isExact: boolean }>({
    position: 0,
    value: "",
    isExact: false,
  });

  // Which cell is being edited, if any. Held here rather than in the row so that
  // only one can be open at a time -- two half-finished edits on the same record
  // is a way to write the wrong one.
  const [editing, setEditing] = useState<
    { recordId: string; field: DictionaryField; index: number; current: string } | null
  >(null);

  const queryClient = useQueryClient();

  // Whether this deployment permits the editor at all. Asked rather than
  // assumed: the cells were buttons on every deployment, including the ones
  // with no write path, so clicking one opened an editor that could only fail.
  const { data: writeStatus } = useQuery({
    queryKey: ["records-status"],
    queryFn: ({ signal }) => api.updateStatus(signal),
    staleTime: Infinity,
    retry: false,
  });

  const canWrite = writeStatus?.canWrite === true;

  const { data: files } = useQuery({
    queryKey: ["schema", "files"],
    queryFn: ({ signal }) => api.schemaFiles(signal),
    staleTime: 5 * 60_000,
  });

  // The first file, chosen once the account has answered. Picked here rather
  // than defaulted to a name, because a default name is a guess about somebody
  // else's database.
  const chosen = file || files?.files?.[0] || "";

  const { data: records, isFetching } = useQuery({
    queryKey: ["schema", "records", chosen, applied.position, applied.value, applied.isExact],
    queryFn: ({ signal }) =>
      api.schemaRecords(chosen, applied.position, applied.value, applied.isExact, signal),
    enabled: chosen.length > 0,
  });

  const dictionary: DictionaryField[] = records?.dictionary ?? [];

  // The key is described in the dictionary as field zero, and it is already the
  // row's own column. Shown once, not twice.
  const columns = dictionary.filter((field) => field.position > 0);

  return (
    <section className="explore" data-tour="explore" aria-label="Explore the database">
      <header className="explore__head">
        <h2 className="explore__title">What is in this account</h2>
        <p className="explore__intro">
          Nothing below is written into this application. The files come from the
          account, the fields from each file&rsquo;s own dictionary, and the column
          headings from what that dictionary calls them.
        </p>
      </header>

      <div className="explore__controls">
        <label className="explore__control">
          <span>File</span>
          <select
            value={chosen}
            onChange={(event) => {
              setFile(event.target.value);
              setPosition(0);
              setValue("");
              setApplied({ position: 0, value: "", isExact: false });
            }}
          >
            {(files?.files ?? []).map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        </label>

        <label className="explore__control">
          <span>Field</span>
          <select
            value={position}
            onChange={(event) => setPosition(Number(event.target.value))}
          >
            <option value={0}>any field</option>
            {columns.map((field) => (
              <option key={field.name} value={field.position}>
                {field.heading} ({field.name}, field {field.position}
                {field.isMultiValued ? ", multi-valued" : ""})
              </option>
            ))}
          </select>
        </label>

        <form
          className="explore__control explore__control--value"
          onSubmit={(event) => {
            event.preventDefault();
            setApplied({ position, value, isExact });
          }}
        >
          <select
            aria-label="How to match"
            value={isExact ? "exact" : "contains"}
            onChange={(event) => setIsExact(event.target.value === "exact")}
            disabled={position === 0}
          >
            <option value="contains">Contains</option>
            <option value="exact">Equals</option>
          </select>
          <input
            type="text"
            value={value}
            placeholder="a value to match…"
            onChange={(event) => setValue(event.target.value)}
            disabled={position === 0}
          />
          <button type="submit" className="button" disabled={position === 0}>
            Find
          </button>
        </form>
      </div>

      {/* Said where it bites. Case folding would make this screen behave unlike
          the database it is demonstrating, so the honest answer is to warn
          rather than to hide it. */}
      <p className="explore__note">
        Matching is case-sensitive, as the database does it. <code>Contains</code>{" "}
        asks <code>LIKE &quot;...value...&quot;</code>; <code>Equals</code> asks{" "}
        <code>= &quot;value&quot;</code>. The statement actually run is printed
        under the results.
      </p>

      {/* Said out loud, because it was not discoverable.
          
          Every value in the table below is a button and always was, but it
          looked exactly like text until somebody happened to hover over it. A
          feature nobody can find is worse than one that does not exist: it costs
          the same to build and earns nothing. */}
      {canWrite && (
        <p className="explore__editable">
          <strong>Click any value to change it.</strong> One value of one field,
          in place — it names the record and the position first, reads the record
          back afterwards, and refuses to pad a field to reach a position it does
          not have. This is the only write in the application.
        </p>
      )}

      {isFetching && <p className="explore__working">Reading the account…</p>}

      {/* Nothing matched, and the reason is nearly always capitalisation.
          Saying so where the zero appears beats a note further up the page that
          somebody has already scrolled past. */}
      {records && !isFetching && records.matchCount === 0 && applied.value && (
        <p className="explore__nothing">
          Nothing matched <code>{applied.value}</code>. Matching is case-sensitive,
          so <code>{applied.value}</code> is not{" "}
          <code>
            {applied.value.charAt(0).toUpperCase() + applied.value.slice(1)}
          </code>
          . Clear the box to see every record and read the capitalisation off the
          values themselves.
        </p>
      )}

      {records && !isFetching && (
        <>
          <div className="scroll">
            <table className="explore__table">
              <caption className="explore__caption">
                {records.matchCount} record{records.matchCount === 1 ? "" : "s"} matched
                {records.records.length < records.matchCount &&
                  `, showing the first ${records.records.length}`}
              </caption>
              <thead>
                <tr>
                  <th scope="col">Key</th>
                  {columns.map((field) => (
                    <th key={field.name} scope="col" title={describe(field)}>
                      {field.heading}
                      {field.isMultiValued && <span className="explore__multi">multi</span>}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {records.records.map((row) => (
                  <tr key={row.key}>
                    <th scope="row" className="explore__key">
                      {row.key}
                    </th>
                    {columns.map((field) => (
                      <td key={field.name} className="explore__value">
                        {canWrite ? (
                          <button
                            type="button"
                            className="explore__cell"
                            title={`Change ${field.heading} for ${row.key}`}
                            onClick={() =>
                              setEditing({
                                recordId: row.key,
                                field,
                                // The first value of the field. A multi-valued
                                // field holds one per branch, and choosing which
                                // is a decision the editor makes plain rather
                                // than one this table guesses.
                                index: 0,
                                current: firstValue(row.fields[field.position - 1] ?? ""),
                              })
                            }
                          >
                            {trim(row.fields[field.position - 1] ?? "")}
                          </button>
                        ) : (
                          trim(row.fields[field.position - 1] ?? "")
                        )}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {editing && (
            <UpdateValue
              file={chosen}
              recordId={editing.recordId}
              field={editing.field}
              index={editing.index}
              current={editing.current}
              onClose={() => setEditing(null)}
              onChanged={() => {
                // Thrown away rather than re-set.
                //
                // This used to spread `applied` into a new object, which changes
                // the object's identity and nothing else: the query key is built
                // from its primitive fields, so the key was identical and React
                // Query served the cached rows. A value could be changed
                // successfully and the table would keep showing the old one --
                // the worst possible outcome for a screen whose subject is
                // whether a write actually happened.
                void queryClient.invalidateQueries({ queryKey: ["schema", "records"] });
              }}
            />
          )}

          <p className="explore__selection">
            {/* Said out loud, because a screen that shows what it asked is one
                whose answer somebody can check. */}
            Asked: <code>{records.selection}</code>
          </p>
        </>
      )}
    </section>
  );
}

/**
 * The first value of a field.
 *
 * @param raw The field, which may hold many values.
 * @returns The value at position one.
 *
 * @remarks
 * A multi-valued field holds one value per branch, so "the field's value" is not
 * a thing that exists. The editor is given a specific position and says which it
 * is; this picks the first because a table cell has to start somewhere.
 */
function firstValue(raw: string): string {
  return raw.split("ý")[0] ?? "";
}

/** What a field's dictionary entry says about it, for a column's tooltip. */
function describe(field: DictionaryField): string {
  const parts = [
    `${field.name} — field ${field.position}`,
    field.isMultiValued ? "multi-valued" : "single-valued",
  ];

  if (field.format) {
    parts.push(`format ${field.format}`);
  }

  if (field.conversion) {
    parts.push(`conversion ${field.conversion}`);
  }

  return parts.join(" · ");
}

/**
 * Shorten a value for a cell, and show the separators it contains.
 *
 * @param raw The field, which may hold many values.
 * @returns Something that fits in a cell.
 *
 * @remarks
 * Value marks are replaced with a visible separator rather than dropped. A
 * parallel field holding twelve branch quantities rendered as one run of digits
 * would be unreadable and, worse, would look like a single number.
 */
function trim(raw: string): string {
  const shown = raw.replaceAll("ý", " · ").replaceAll("ü", " / ");

  return shown.length > VALUE_WIDTH ? `${shown.slice(0, VALUE_WIDTH)}…` : shown;
}
