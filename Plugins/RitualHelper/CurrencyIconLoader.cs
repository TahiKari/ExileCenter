using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ExileCenter;

namespace RitualHelper
{
    internal sealed class CurrencyIconLoader
    {
        private static readonly (string FileName, string Url)[] DefaultIcons =
        {
            ("divine.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lNb2RWYWx1ZXMiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/2986e220b3/CurrencyModValues.png"),
            ("exalted.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/ad7c366789/CurrencyAddModToRare.png"),
            ("chaos.png", "https://web.poecdn.com//gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lEdWxhdGUiLCJzY2FsZSI6MSwicmVhbG0iOiJwb2UyIn1d/8b3e0a3f2c/ChaosOrb.png"),
        };

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "RitualHelper-GameHelper-Plugin");
            return client;
        }

        private readonly Dictionary<string, (IntPtr Ptr, int W, int H)> textures = new(StringComparer.OrdinalIgnoreCase);
        private string texturesPath = string.Empty;
        private bool downloadStarted;

        public void Initialize(string pluginDirectory)
        {
            this.texturesPath = Path.Combine(pluginDirectory, "Textures");
            Directory.CreateDirectory(this.texturesPath);

            if (!this.downloadStarted)
            {
                this.downloadStarted = true;
                Task.Run(this.EnsureIconFilesAsync);
            }

            this.Reload();
        }

        public void Reload()
        {
            this.textures.Clear();
            if (!Directory.Exists(this.texturesPath)) return;

            foreach (var pathname in Directory.EnumerateFiles(this.texturesPath, "*.png"))
            {
                var fileName = Path.GetFileName(pathname);
                Core.Overlay.AddOrGetImagePointer(pathname, false, out var handle, out var w, out var h);
                if (handle != IntPtr.Zero)
                    this.textures[fileName] = (handle, (int)w, (int)h);
            }
        }

        public bool TryGet(string fileName, out IntPtr ptr, out int w, out int h)
        {
            if (this.textures.TryGetValue(fileName, out var tex))
            {
                ptr = tex.Ptr;
                w = tex.W;
                h = tex.H;
                return true;
            }

            ptr = IntPtr.Zero;
            w = 0;
            h = 0;
            return false;
        }

        private async Task EnsureIconFilesAsync()
        {
            foreach (var (fileName, url) in DefaultIcons)
            {
                var path = Path.Combine(this.texturesPath, fileName);
                if (File.Exists(path)) continue;

                try
                {
                    using var response = await Http.GetAsync(url).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
