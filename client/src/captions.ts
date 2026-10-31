import type {Line} from './types';

export interface CaptionRow {text: string; large: boolean}

export function captionRows(line: Line): CaptionRow[] {
  return line.segments.flatMap(segment => {
    if (segment.lang === 'en') return [{text: segment.text, large: false}];
    const values: [boolean, string | undefined, boolean][] = [
      [line.captions.show_japanese, segment.text, true],
      [line.captions.show_reading, segment.reading, false],
      [line.captions.show_romaji, segment.romaji, false],
      [line.captions.show_translation, segment.translation, false],
    ];
    return values.filter(([, text]) => !!text).filter(([visible]) => visible)
      .map(([, text, large]) => ({text: text!, large}));
  });
}

export class LinePresenter {
  private generation: number | null = null;
  private interrupted = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  constructor(private present: (line: Line | null) => void) {}
  private cancelClear() {if (this.timer !== null) clearTimeout(this.timer); this.timer = null;}
  start(generation: number) {this.generation = generation; this.interrupted = false;}
  show(line: Line) {
    if (this.interrupted || line.generation_id !== this.generation) return;
    this.cancelClear();
    this.present(line);
  }
  end(generation: number) {
    if (this.interrupted || generation !== this.generation) return;
    this.cancelClear();
    this.timer = setTimeout(() => {this.timer = null; this.present(null);}, 4000);
  }
  interrupt() {this.cancelClear(); this.interrupted = true; this.present(null);}
  allowRepeat() {this.interrupted = false;}
  reset() {this.interrupt(); this.generation = null;}
}
