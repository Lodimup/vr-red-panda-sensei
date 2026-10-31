export const didntCatch = "I didn't catch that — hold A and speak again";
export function releaseMessage(holdMs: number): string {
  return holdMs < 300 ? didntCatch : 'Heard you — transcribing…';
}
export function transcriptMessage(text: string): string { return `You: ${text}`; }
