#if __WASM__
using System.Runtime.InteropServices.JavaScript;

namespace ShowUp.Services.Marking;

// The prototype's Web Audio scratch (looped noise -> band-pass -> gain), in WasmScripts/MarkerScratch.js:
// HTML audio can't move a filter band, and Web Audio never ducks other tabs' audio.
public sealed partial class MarkingFeedback
{
    static partial void StartScratchCore(string sound, ref string? backend)
    {
        if (Scratch.Start())
        {
            backend = "Web Audio";
        }
    }

    static partial void SetScratchCore(double volume, double rate) => Scratch.Speed(volume, rate);

    static partial void StopScratchCore() => Scratch.Stop();

    private static partial class Scratch
    {
        [JSImport("globalThis.ShowUpScratch.start")]
        public static partial bool Start();

        [JSImport("globalThis.ShowUpScratch.speed")]
        public static partial void Speed(double volume, double rate);

        [JSImport("globalThis.ShowUpScratch.stop")]
        public static partial void Stop();
    }
}
#endif
