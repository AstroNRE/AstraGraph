import { rankTexts } from "./fuzzy";

self.onmessage = (event: MessageEvent<{ query: string; items: { id: string; text: string }[] }>) => {
  const ranked = rankTexts(event.data.query, event.data.items).slice(0, 30);
  self.postMessage(ranked);
};
