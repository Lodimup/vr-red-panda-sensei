import {describe, expect, it} from 'vitest';
import {didntCatch, releaseMessage, transcriptMessage} from './voice-feedback';
describe('voice feedback', () => {
  it('distinguishes short holds from transcription and shows the server transcript', () => {
    expect(releaseMessage(299)).toBe(didntCatch);
    expect(releaseMessage(300)).toBe('Heard you — transcribing…');
    expect(transcriptMessage('りんご')).toBe('You: りんご');
  });
});
