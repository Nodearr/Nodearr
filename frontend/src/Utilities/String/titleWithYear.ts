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
