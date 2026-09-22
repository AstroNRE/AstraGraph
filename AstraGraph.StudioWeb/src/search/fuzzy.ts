export interface RankedText {
  id: string;
  text: string;
  score: number;
}

export function fuzzyScore(query: string, text: string): number {
  const needle = query.trim().toLowerCase();
  const haystack = text.toLowerCase();
  if (!needle) return 1;
  if (haystack === needle) return 100;
  if (haystack.startsWith(needle)) return 80;
  let score = 0;
  let index = 0;
  for (const character of needle) {
    const found = haystack.indexOf(character, index);
    if (found < 0) return 0;
    score += 10 - Math.min(found - index, 8);
    index = found + 1;
  }
  return score;
}

export function rankTexts(query: string, items: { id: string; text: string }[]): RankedText[] {
  return items
    .map((item) => ({ ...item, score: fuzzyScore(query, item.text) }))
    .filter((item) => item.score > 0)
    .sort((left, right) => right.score - left.score || left.text.localeCompare(right.text));
}
