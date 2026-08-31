/**
 * Change one value, and see what it did to the record.
 *
 * The last step of find, review, update — and the one that has to be careful,
 * because a MultiValue database will not catch a bad write on your behalf. There
 * are no constraints to break: a record that has just had a quantity moved onto
 * the wrong branch is still a perfectly valid record, and every later read agrees
 * with it.
 *
 * So the flow shows its working. It names what it is about to replace, asks for
 * confirmation, and then shows the record before and after with the number of
 * values each field held both times. If any of those numbers changed, a parallel
 * field has moved and the panel says so in the strongest terms available — which
 * is more than the database itself would do.
 */

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../../api/client";
import type { DictionaryField, RecordChange } from "../../api/types";

interface Props {
  /** The file holding the record. */
  file: string;
  /** The record's key. */
  recordId: string;
  /** The field being changed, as its dictionary describes it. */
  field: DictionaryField;
  /** Which value within that field, counting from zero. */
  index: number;
  /** What is there now. */
  current: string;
  /**
   * Leave the editor, and refresh the rows behind it.
   *
   * There was a second callback for the moment a write succeeded. Nothing
   * could usefully be done with it: refreshing there re-rendered this
   * component while its report was on screen, so the proof that no parallel
   * field had moved appeared and vanished. The report is the whole point of
   * the screen, and the table underneath can wait until it has been read.
   */
  onClose: () => void;
}

export function UpdateValue({
  file,
  recordId,
  field,
  index,
  current,
  onClose,
}: Props): React.JSX.Element | null {
  const [value, setValue] = useState(current);
  const [isConfirming, setIsConfirming] = useState(false);

  // Asked before an edit control is shown at all, so nobody is invited to type a
  // change this deployment cannot make.
  const { data: status } = useQuery({
    queryKey: ["update-status"],
    queryFn: ({ signal }) => api.updateStatus(signal),
    staleTime: Infinity,
    retry: false,
  });

  const change = useMutation<RecordChange, Error, string>({
    mutationFn: (wanted: string) =>
      api.updateValue(file, recordId, field.position, index, wanted),
  });

  if (!status?.canWrite) {
    return (
      <div className="update update--absent">
        <p>
          This deployment reads only. It runs the read-only ERP driver, which has no
          write path at all — not a switched-off one.
        </p>
        <button type="button" className="button button--quiet" onClick={onClose}>
          Close
        </button>
      </div>
    );
  }

  const failure = change.error instanceof ApiFailure ? change.error : null;
  const result = change.data;

  return (
    <div className="update">
      <p className="update__what">
        Changing <strong>{field.heading}</strong> ({field.name}, field {field.position}
        {field.isMultiValued ? `, value ${index + 1}` : ""}) of{" "}
        <code>{recordId}</code>
      </p>

      {!result && (
        <>
          <div className="update__edit">
            <label>
              <span>Now</span>
              <input type="text" value={current} readOnly />
            </label>
            <label>
              <span>Change to</span>
              <input
                type="text"
                value={value}
                autoFocus
                onChange={(event) => setValue(event.target.value)}
              />
            </label>
          </div>

          {!isConfirming && (
            <div className="update__actions">
              <button type="button" className="button button--quiet" onClick={onClose}>
                Cancel
              </button>
              <button
                type="button"
                className="button"
                disabled={value === current}
                onClick={() => setIsConfirming(true)}
              >
                Review the change
              </button>
            </div>
          )}

          {isConfirming && (
            <>
              {/* Named rather than implied. Somebody confirming a write should be
                  told which record and which position, not asked to remember
                  what they clicked two screens ago. */}
              <p className="update__confirm">
                Write <code>{value || "(empty)"}</code> over <code>{current || "(empty)"}</code>{" "}
                at position {index + 1} of field {field.position} in {recordId}?
              </p>
              <div className="update__actions">
                <button
                  type="button"
                  className="button button--quiet"
                  onClick={() => setIsConfirming(false)}
                >
                  Back
                </button>
                <button
                  type="button"
                  className="button"
                  disabled={change.isPending}
                  onClick={() => change.mutate(value)}
                >
                  {change.isPending ? "Writing…" : "Write it"}
                </button>
              </div>
            </>
          )}
        </>
      )}

      {failure && <p className="update__failure">{failure.message}</p>}

      {result && <ChangeReport change={change.data!} onClose={onClose} />}
    </div>
  );
}

/**
 * What the write did, read back from the file.
 *
 * @param change The record before and after.
 * @param onClose Leave the editor.
 */
function ChangeReport({
  change,
  onClose,
}: {
  change: RecordChange;
  onClose: () => void;
}): React.JSX.Element {
  return (
    <div className="update__report">
      <p
        className={
          change.isAlignmentPreserved
            ? "update__verdict update__verdict--good"
            : "update__verdict update__verdict--bad"
        }
      >
        {change.isAlignmentPreserved
          ? "Written, and every field still holds the same number of values."
          : "Written, but a field changed length — a value has moved onto a different position."}
      </p>

      <div className="scroll">
        <table className="update__lengths">
          <caption>
            {/* The evidence, not decoration. A MultiValue database has no
                constraints to break, so this comparison is the only thing that
                can tell a good write from one that quietly moved a quantity. */}
            Values per field, before and after
          </caption>
          <thead>
            <tr>
              <th scope="col">Field</th>
              {change.fieldLengthsBefore.map((_, position) => (
                <th key={position} scope="col">
                  {position + 1}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">Before</th>
              {change.fieldLengthsBefore.map((count, position) => (
                <td key={position}>{count}</td>
              ))}
            </tr>
            <tr>
              <th scope="row">After</th>
              {change.fieldLengthsAfter.map((count, position) => (
                <td
                  key={position}
                  className={
                    count === change.fieldLengthsBefore[position]
                      ? undefined
                      : "update__moved"
                  }
                >
                  {count}
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>

      <button type="button" className="button" onClick={onClose}>
        Done
      </button>
    </div>
  );
}
