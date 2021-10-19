using System;
using System.Drawing;
using System.IO;
using System.Net;
using MCGalaxy.Network;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {

        // 32x64 (Steve), 64x64 (SteveLayers), 64x64 slim-arm (Alex)
        public enum SkinType { Steve, SteveLayers, Alex };
        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static bool IsAllColor(Color solid, Bitmap bmp, int x1, int y1, int width, int height) {
            int x, y;
            for (y = y1; y < y1 + height; y++) {
                for (x = x1; x < x1 + width; x++) {
                    //e.Message("x is %b{0}%S, y is %b{1}%S.", x, y);
                    Color col = bmp.GetPixel(x, y);
                    if (!col.Equals(solid)) {
                        //e.Message("It's not {0}, it's {1}!", solid, col);
                        return false;
                    }
                }
            }
            return true;
        }

        // ruthlessly copy-paste-edited from ClassiCube Utils.c (thanks UnknownShadow200)
        static SkinType GetSkinType(Bitmap bmp) {
            Color col;
            int scale;
            if (bmp.Width == bmp.Height * 2) return SkinType.Steve;
            if (bmp.Width != bmp.Height) return SkinType.SteveLayers;

            scale = bmp.Width / 64;
            // Minecraft alex skins have this particular pixel with alpha of 0
            col = bmp.GetPixel(54 * scale, 20 * scale);
            if (col.A < 128) { return SkinType.Alex; }
            Color black = Color.FromArgb(0, 0, 0);
            return IsAllColor(black, bmp, 54 * scale, 20 * scale, 2 * scale, 12 * scale)
                && IsAllColor(black, bmp, 50 * scale, 16 * scale, 2 * scale, 4 * scale) ? SkinType.Alex : SkinType.SteveLayers;
        }

        static byte[] FetchData(Uri uri) {
            byte[] data = null;
            // TODO set timeout!
            Debug("DownloadData {0}", uri);
            using (WebClient client = HttpUtil.CreateWebClient()) {
                data = client.DownloadData(uri);
            }
            return data;
        }

        static Uri GetSkinUrl(string skinName) {
            if (Uri.TryCreate(skinName, UriKind.Absolute, out Uri uri)) {
                return uri;
            }

            if (Uri.TryCreate("http://www.classicube.net/static/skins/" + skinName + ".png", UriKind.Absolute, out uri)) {
                return uri;
            }

            throw new Exception("couldn't convert " + skinName + " to a Uri");
        }

        static SkinType GetSkinType(string skinName) {
            var uri = GetSkinUrl(skinName);

            using (var memoryStream = new MemoryStream(FetchData(uri))) {
                using (var bitmap = new Bitmap(memoryStream)) {
                    return GetSkinType(bitmap);
                }
            }
        }

    } // class CustomModelsPlugin
} // namespace MCGalaxy
