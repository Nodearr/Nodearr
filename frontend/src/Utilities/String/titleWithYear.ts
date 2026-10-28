// A title with its year after it, as the Arr apps show one: "Pingu (1990)". Not twice when the
// title already ends with it, as TVDB names some series: "Bluey (2018)".
export default function titleWithYear(
  title: string | null | undefined,
  year?: number | null
) {
  const name = title ?? '';

  if (!year || name.endsWith(`(${year})`)) {
    return name;
  }

  return `${name} (${year})`;
}

// The year to show on a line of its own under the title, where a poster tile has no room for both:
// none when there is no year, or the title already says it.
export function yearOnItsOwn(
  title: string | null | undefined,
  year?: number | null
) {
  return year && !(title ?? '').endsWith(`(${year})`) ? `${year}` : null;
}
