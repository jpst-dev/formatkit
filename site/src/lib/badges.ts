export type BadgeTone = 'green' | 'amber' | 'plain';
export interface Badge {
  text: string;
  tone: BadgeTone;
}

/** Tone of the level badge: safe is green, aggressive is amber, anything else plain. */
export function levelTone(level: string): BadgeTone {
  return level === 'safe' ? 'green' : level === 'aggressive' ? 'amber' : 'plain';
}
