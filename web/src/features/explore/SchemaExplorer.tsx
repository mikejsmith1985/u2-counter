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
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { DictionaryField } from "../../api/types";

/** How many characters of a value to show before trimming it. */
const VALUE_WIDTH = 28;

export function SchemaExplorer(): React.JSX.Element {
  const [file, setFile] = useState<string>("");
  const [position, setPosition] = useState<number>(0);
  const [value, setValue] = useState<string>("");
  const [applied, setApplied] = useState<{ position: number; value: string }>({
    position: 0,
    value: "",
  });

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
    queryKey: ["schema", "records", chosen, applied.position, applied.value],
    queryFn: ({ signal }) =>
      api.schemaRecords(chosen, applied.position, applied.value, signal),
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
              setApplied({ position: 0, value: "" });
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
            setApplied({ position, value });
          }}
        >
          <span>Equals</span>
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

      {isFetching && <p className="explore__working">Reading the account…</p>}

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
                        {trim(row.fields[field.position - 1] ?? "")}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

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
