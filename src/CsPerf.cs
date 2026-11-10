using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CsArsenal
{
    // This mod's own performance log (user 11-10; other mods keep their own). Every Harmony hook and per-frame part of the
    // mod is timed: calls, total time, the slowest call and the memory it allocated. Every 30 real seconds in which the
    // mod did anything, one line goes to <persistent data>/CsArsenal/CsArsenal-perf.log (2 MB, then .old.log); the
    // self-test also echoes it to the game's Player.log, tagged "[CsArsenal][性能]".
    // Hooks run on the game's simulation thread and on the main thread, so every counter is added with Interlocked; the
    // main thread reads and resets them in Flush. Timing a hook costs two Stopwatch reads and two allocation reads.
    public static class CsPerf
    {
        public enum H { Hit, Shoot, Aim, Volley, Speed, Live, Spread, Tracer, TracerFrame, Sound, Mesh, Attach, Update, Count }
        static readonly string[] Names = { "受击结算", "开火", "瞄准计时", "霰弹齐射", "移动速度", "冻结检查", "弹丸散布", "曳光创建", "曳光每帧", "枪声", "科技图标网格", "手持挂件", "模组每帧" };
        const int N = (int)H.Count;

        public static long Now => Stopwatch.GetTimestamp();
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        static readonly long[] ticks = new long[N], calls = new long[N], maxTick = new long[N], alloc = new long[N];

        public struct Mark { public long T, A; }
        public static Mark Start() => new Mark { T = Stopwatch.GetTimestamp(), A = Alloc() };
        public static void End(H h, Mark m)
        {
            long d = Stopwatch.GetTimestamp() - m.T; int i = (int)h;
            Interlocked.Add(ref ticks[i], d); Interlocked.Increment(ref calls[i]);
            long cur; while (d > (cur = Interlocked.Read(ref maxTick[i])) && Interlocked.CompareExchange(ref maxTick[i], d, cur) != cur) { }
            if (threadAlloc != null) { long a = threadAlloc() - m.A; if (a > 0) Interlocked.Add(ref alloc[i], a); }
        }

        // bytes allocated by the calling thread (only where the runtime really counts them)
        static readonly Func<long> threadAlloc = MakeThreadAlloc();
        static Func<long> MakeThreadAlloc()
        {
            try
            {
                var m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
                if (m == null) return null;
                var f = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
                long a = f(); var probe = new byte[8192]; long b = f(); GC.KeepAlive(probe);
                return b - a >= 8192 ? f : null;
            }
            catch { return null; }
        }
        static long Alloc() => threadAlloc != null ? threadAlloc() : 0;

        // frames (main thread, ModBehaviour.Update)
        static int frames; static double frameSum, frameMax;
        static float windowStart = -1; static int gc0 = -1;
        public static float Window = 30f;
        public static bool EchoToGameLog;   // set by the self-test
        public static string LastLine = "", FilePath;
        public static bool LastLineFresh;
        // the per-hook numbers of the last Flush, for the self-test: name -> (calls/s, ms/s, max ms, KB/s)
        public static readonly System.Collections.Generic.Dictionary<string, double[]> Last = new System.Collections.Generic.Dictionary<string, double[]>();
        public static double LastFrameAvg, LastFrameMax, LastSeconds;

        public static void EndFrame()
        {
            double dt = Time.unscaledDeltaTime * 1000.0;
            frameSum += dt; if (dt > frameMax) frameMax = dt; frames++;
            if (windowStart < 0) { windowStart = Time.unscaledTime; gc0 = GC.CollectionCount(0); }
            if (Time.unscaledTime - windowStart >= Window) Flush();
        }

        public static void Flush()
        {
            float secs = Mathf.Max(0.001f, Time.unscaledTime - windowStart);
            var t = new long[N]; var c = new long[N]; var mx = new long[N]; var al = new long[N];
            for (int i = 0; i < N; i++)
            {
                t[i] = Interlocked.Exchange(ref ticks[i], 0); c[i] = Interlocked.Exchange(ref calls[i], 0);
                mx[i] = Interlocked.Exchange(ref maxTick[i], 0); al[i] = Interlocked.Exchange(ref alloc[i], 0);
            }
            // "did anything": a hook other than the always-running ones (speed, freeze check, aim timer, per-frame update)
            bool busy = c[(int)H.Hit] + c[(int)H.Shoot] + c[(int)H.Tracer] + c[(int)H.Sound] > 0;
            LastLineFresh = frames > 0;
            Last.Clear(); LastSeconds = secs;
            LastFrameAvg = frames > 0 ? frameSum / frames : 0; LastFrameMax = frameMax;
            if (frames > 0)
            {
                double total = 0, totalAlloc = 0;
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(ModBehaviour.Version).Append("  ").Append(secs.ToString("0")).Append(" 秒 ")
                  .Append(frames).Append(" 帧 平均帧 ").Append(LastFrameAvg.ToString("0.0")).Append("ms（").Append((frames / secs).ToString("0"))
                  .Append(" fps） 最慢帧 ").Append(frameMax.ToString("0")).Append("ms |");
                for (int i = 0; i < N; i++)
                {
                    if (c[i] == 0) continue;
                    double msPerSec = Ms(t[i]) / secs, kbPerSec = al[i] / 1024.0 / secs;
                    total += msPerSec; totalAlloc += kbPerSec;
                    Last[Names[i]] = new[] { c[i] / secs, msPerSec, Ms(mx[i]), kbPerSec };
                    sb.Append(' ').Append(Names[i]).Append(' ').Append((c[i] / secs).ToString("0")).Append("次/秒 ")
                      .Append(msPerSec.ToString("0.00")).Append("ms/秒（平均 ").Append((Ms(t[i]) * 1000 / c[i]).ToString("0.0")).Append("us 最慢 ")
                      .Append(Ms(mx[i]).ToString("0.00")).Append("ms");
                    if (threadAlloc != null) sb.Append(" 分配 ").Append(kbPerSec.ToString("0.0")).Append("KB/秒");
                    sb.Append("）；");
                }
                sb.Append(" | 合计 ").Append(total.ToString("0.00")).Append("ms/秒");
                if (threadAlloc != null) sb.Append(" 分配 ").Append(totalAlloc.ToString("0.0")).Append("KB/秒"); else sb.Append("（这个运行时数不了分配）");
                sb.Append(" | 曳光存活 ").Append(CsTracer.Live).Append(" | 垃圾回收 ").Append(gc0 < 0 ? 0 : GC.CollectionCount(0) - gc0)
                  .Append(" 次 内存 ").Append((GC.GetTotalMemory(false) / 1048576.0).ToString("0")).Append("MB");
                LastLine = sb.ToString();
                if (busy || EchoToGameLog) Write(LastLine);   // quiet windows (no fighting) are not logged in normal play
            }
            frames = 0; frameSum = frameMax = 0;
            windowStart = Time.unscaledTime; gc0 = GC.CollectionCount(0);
        }

        // main thread only (persistentDataPath)
        public static void Write(string line)
        {
            if (EchoToGameLog) UnityEngine.Debug.Log("[CsArsenal][性能] " + line);
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "CsArsenal");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, "CsArsenal-perf.log");
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    var old = Path.Combine(dir, "CsArsenal-perf.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[CsArsenal] perf log: " + e.Message); }
        }
    }
}
