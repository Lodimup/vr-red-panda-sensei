import { XRDevice, metaQuest3 } from 'iwer';

const device = new XRDevice(metaQuest3);
device.installRuntime({ forceInstall: true });
Object.assign(window, { __senseiXR: device });

// Insert an analyser in each destination connection and preserve connect/disconnect behavior.
const connect = AudioNode.prototype.connect as (...args: any[]) => any;
const disconnect = AudioNode.prototype.disconnect as (...args: any[]) => any;
const taps = new Map<AudioNode, { destination: AudioDestinationNode; analyser: AnalyserNode; output: number; input: number }[]>();
const samples: { time: number; peak: number }[] = [];
AudioNode.prototype.connect = function(this: AudioNode, destination: AudioNode | AudioParam, ...args: number[]) {
  if (!(destination instanceof AudioDestinationNode)) return connect.call(this, destination, ...args);
  const output = args[0] ?? 0, input = args[1] ?? 0;
  let list = taps.get(this); if (!list) { list = []; taps.set(this, list); }
  if (!list.some(t => t.destination === destination && t.output === output && t.input === input)) {
    const analyser = this.context.createAnalyser(); analyser.fftSize = 2048;
    connect.call(analyser, destination, 0, input);
    connect.call(this, analyser, output, 0);
    list.push({destination, analyser, output, input});
  }
  return destination;
} as typeof AudioNode.prototype.connect;
AudioNode.prototype.disconnect = function(this: AudioNode, ...args: any[]) {
  const list = taps.get(this) ?? [];
  const selected = list.filter(t => args.length === 0 || (typeof args[0] === 'number' ? t.output === args[0] : args[0] === t.destination && (args.length < 2 || t.output === args[1]) && (args.length < 3 || t.input === args[2])));
  for (const t of selected) { disconnect.call(this,t.analyser,t.output,0); disconnect.call(t.analyser); list.splice(list.indexOf(t),1); }
  if (list.length === 0) taps.delete(this);
  // A destination overload must not be forwarded: the destination was replaced by the tap.
  if (args[0] instanceof AudioDestinationNode) { if (!selected.length) return disconnect.call(this,...args); return; }
  return disconnect.call(this,...args);
} as typeof AudioNode.prototype.disconnect;
setInterval(() => {
  let peak = 0;
  for (const [source, list] of taps) {
    if (source.context.state === 'closed') { taps.delete(source); continue; }
    for (const {analyser} of list) {
      const buffer = new Float32Array(analyser.fftSize);
      analyser.getFloatTimeDomainData(buffer);
      for (const value of buffer) peak = Math.max(peak, Math.abs(value));
    }
  }
  samples.push({ time: performance.now(), peak });
  while (samples.length && samples[0].time < performance.now() - 60000) samples.shift();
}, 10);
Object.assign(window, { __senseiAudio: { samples } });
