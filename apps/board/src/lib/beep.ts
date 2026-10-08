let audio: AudioContext | null = null;

/** Browsers only allow sound after a user gesture, so the KDS asks for one tap ("Enable sound"). */
export function unlockSound(): boolean {
  try {
    audio ??= new AudioContext();
    void audio.resume();
    return true;
  } catch {
    return false;
  }
}

export const soundEnabled = () => audio?.state === "running";

/** Short two-tone chime; silent until unlockSound() has run. */
export function beep(tones: number[] = [880, 1320]) {
  if (!audio || audio.state !== "running") return;
  tones.forEach((freq, i) => {
    const osc = audio!.createOscillator();
    const gain = audio!.createGain();
    const start = audio!.currentTime + i * 0.18;
    osc.frequency.value = freq;
    gain.gain.setValueAtTime(0.25, start);
    gain.gain.exponentialRampToValueAtTime(0.001, start + 0.16);
    osc.connect(gain).connect(audio!.destination);
    osc.start(start);
    osc.stop(start + 0.17);
  });
}
