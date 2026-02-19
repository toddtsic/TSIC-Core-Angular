/** Shared Rich Text Editor toolbar config for Job Config HTML fields. */
export const JOB_CONFIG_RTE_TOOLS = {
  items: [
    'Bold', 'Italic', 'Underline', '|',
    'FontColor', 'BackgroundColor', '|',
    'OrderedList', 'UnorderedList', '|',
    'CreateLink', '|', 'Undo', 'Redo',
  ],
};

export const JOB_CONFIG_RTE_HEIGHT = 200;

/** Strip ISO datetime to yyyy-MM-dd for HTML date inputs. */
export function toDateOnly(value: string | null | undefined): string | null {
  return value ? value.substring(0, 10) : null;
}
