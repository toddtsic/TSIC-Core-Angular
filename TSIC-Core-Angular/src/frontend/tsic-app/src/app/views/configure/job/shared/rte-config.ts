// The toolbar config that used to live here is now the app-wide canonical one:
// `TSIC_RTE_TOOLS` in @shared-ui/rte-config. It moved because shared-ui and the
// scheduling rescheduler both mount an RTE too, and a shared component reaching
// into views/configure/job for its toolbar is the wrong direction of dependency.

export const JOB_CONFIG_RTE_HEIGHT = 200;

/** Strip ISO datetime to yyyy-MM-dd for HTML date inputs. */
export function toDateOnly(value: string | null | undefined): string | null {
  return value ? value.substring(0, 10) : null;
}

/**
 * Normalize what an `<input type="date">` emits when it is CLEARED.
 *
 * A cleared date input emits the EMPTY STRING, not null. Posting that straight into a
 * `DateTime?` property fails deserialization on the server before any handler runs, so
 * the save is rejected and ASP.NET answers with a raw serializer diagnostic naming the
 * type, the JSON path and a byte offset (AR-088). Every `(ngModelChange)` on a date
 * input that feeds a save payload must go through this.
 */
export function fromDateInput(value: string | null | undefined): string | null {
  return value ? value : null;
}
