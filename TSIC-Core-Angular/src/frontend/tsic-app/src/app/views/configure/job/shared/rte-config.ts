// The toolbar config that used to live here is now the app-wide canonical one:
// `TSIC_RTE_TOOLS` in @shared-ui/rte-config. It moved because shared-ui and the
// scheduling rescheduler both mount an RTE too, and a shared component reaching
// into views/configure/job for its toolbar is the wrong direction of dependency.

export const JOB_CONFIG_RTE_HEIGHT = 200;

/** Strip ISO datetime to yyyy-MM-dd for HTML date inputs. */
export function toDateOnly(value: string | null | undefined): string | null {
  return value ? value.substring(0, 10) : null;
}
