using System;
using System.IO;
using System.Threading;
using Steamworks;

namespace WorkshopFetch
{
    // Downloads one Workshop item through the logged-in Steam client WITHOUT subscribing to it, then copies it to --out.
    // The game only loads subscribed items (UGCManager.getInstalledItemFolders walks GetSubscribedItems), so a
    // downloaded-but-unsubscribed item never shows up in the real game.
    //   WorkshopFetch --app <id> --id <published id> --out <dir> [--timeout <seconds>]
    static class Program
    {
        static bool inited;

        static int Main(string[] argv)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try { return Run(argv); }
            catch (Exception e) { Console.WriteLine("ERROR " + e); return 1; }
            finally { if (inited) SteamAPI.Shutdown(); }
        }

        static string Arg(string[] a, string k, string def = null)
        {
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "--" + k) return a[i + 1];
            if (def == null) throw new ArgumentException("missing --" + k);
            return def;
        }

        static int Run(string[] a)
        {
            uint app = uint.Parse(Arg(a, "app"));
            var id = new PublishedFileId_t(ulong.Parse(Arg(a, "id")));
            string outDir = Path.GetFullPath(Arg(a, "out"));
            int timeout = int.Parse(Arg(a, "timeout", "1200"));
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt"), app.ToString());
            Environment.SetEnvironmentVariable("SteamAppId", app.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", app.ToString());
            var r = SteamAPI.InitEx(out string msg);
            if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK) { Console.WriteLine("ERROR steam init " + r + ": " + msg); return 2; }
            inited = true;
            Console.WriteLine("steam user: " + SteamFriends.GetPersonaName());

            EResult done = EResult.k_EResultNone;
            var cb = Callback<DownloadItemResult_t>.Create(d => { if (d.m_nPublishedFileId == id) done = d.m_eResult; });
            uint st = SteamUGC.GetItemState(id);
            Console.WriteLine("state before: " + (EItemState)st);
            bool subscribedBefore = (st & (uint)EItemState.k_EItemStateSubscribed) != 0;
            bool ready = (st & (uint)EItemState.k_EItemStateInstalled) != 0 && (st & (uint)EItemState.k_EItemStateNeedsUpdate) == 0;
            if (!ready)
            {
                if (!SteamUGC.DownloadItem(id, true)) { Console.WriteLine("ERROR DownloadItem returned false"); return 3; }
                var t0 = DateTime.UtcNow; var next = DateTime.MinValue;
                while (done == EResult.k_EResultNone)
                {
                    SteamAPI.RunCallbacks(); Thread.Sleep(200);
                    if (DateTime.UtcNow > next)
                    {
                        SteamUGC.GetItemDownloadInfo(id, out ulong got, out ulong total);
                        Console.WriteLine("progress " + got + "/" + total + " state " + (EItemState)SteamUGC.GetItemState(id));
                        next = DateTime.UtcNow.AddSeconds(10);
                    }
                    if ((DateTime.UtcNow - t0).TotalSeconds > timeout) { Console.WriteLine("ERROR timeout"); return 4; }
                }
                Console.WriteLine("download result: " + done);
                if (done != EResult.k_EResultOK) return 5;
            }
            if (!SteamUGC.GetItemInstallInfo(id, out ulong size, out string folder, 4096, out uint stamp)) { Console.WriteLine("ERROR no install info"); return 6; }
            Console.WriteLine("FOLDER=" + folder + " size " + size + " stamp " + stamp);
            int n = Copy(folder, outDir);
            Console.WriteLine("copied " + n + " files to " + outDir);
            uint after = SteamUGC.GetItemState(id);
            Console.WriteLine("state after: " + (EItemState)after + " (subscribed before: " + subscribedBefore + ")");
            Console.WriteLine("RESULT=OK");
            GC.KeepAlive(cb);
            return 0;
        }

        static int Copy(string src, string dst)
        {
            int n = 0;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
            foreach (var d in Directory.GetDirectories(src)) n += Copy(d, Path.Combine(dst, Path.GetFileName(d)));
            return n;
        }
    }
}
