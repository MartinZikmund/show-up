// Marker scratch for "draw it yourself": scratchStart/scratchSpeed/scratchStop from
// docs/prototypes/marker-cross.html. Volume 0..1 maps to the prototype's 0..0.32 gain,
// rate to its 1800-4000 Hz band (rate 1 = 2400 Hz).
globalThis.ShowUpScratch = (() => {
    let audio = null;
    let scratch = null;

    function start() {
        stop();
        try {
            audio ??= new AudioContext();
        } catch {
            return false;
        }
        if (audio.state === "suspended") {
            audio.resume();
        }
        const len = audio.sampleRate, buf = audio.createBuffer(1, len, audio.sampleRate), d = buf.getChannelData(0);
        for (let k = 0; k < len; k++) {
            d[k] = Math.random() * 2 - 1;
        }
        const src = audio.createBufferSource();
        src.buffer = buf;
        src.loop = true;
        const bp = audio.createBiquadFilter();
        bp.type = "bandpass";
        bp.frequency.value = 2400;
        bp.Q.value = 1.2;
        const g = audio.createGain();
        g.gain.value = 0;
        src.connect(bp).connect(g).connect(audio.destination);
        src.start();
        scratch = { src, bp, g };
        return true;
    }

    function speed(volume, rate) {
        if (!scratch) {
            return;
        }
        const t = audio.currentTime;
        scratch.g.gain.setTargetAtTime(0.32 * volume, t, 0.03);
        scratch.bp.frequency.setTargetAtTime(2400 * rate, t, 0.05);
    }

    function stop() {
        if (!scratch) {
            return;
        }
        const s0 = scratch;
        scratch = null;
        s0.g.gain.setTargetAtTime(0, audio.currentTime, 0.04);
        setTimeout(() => s0.src.stop(), 200);
    }

    return { start, speed, stop };
})();
