using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MCGalaxy.Network;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {
        public class HttpSkinServer {
            private TcpListener tcpListener = null;
            private CancellationTokenSource httpListenerWorkerToken = null;
            private string publicIp;
            private readonly int port;

            public HttpSkinServer(string publicIp = null, int port = 0) {
                this.publicIp = publicIp;
                this.port = port;
            }

            private void FetchPublicIP() {
                using (WebClient client = HttpUtil.CreateWebClient()) {
                    var data = client.DownloadData("https://1.1.1.1/cdn-cgi/trace");
                    var text = System.Text.Encoding.ASCII.GetString(data, 0, data.Length);

                    var marker = "ip=";
                    var markerIndex = text.IndexOf(marker);
                    if (markerIndex == -1) throw new Exception("couldn't find 'ip=' marker");

                    var part = text.Substring(markerIndex + marker.Length);
                    var ip = part.Substring(0, Math.Max(part.IndexOf('\n'), 0));

                    publicIp = ip;
                    Debug("CustomModels Skin http server got public ip {0}", ip);
                }
            }

            public static HttpSkinServer Start(string publicIp = null, int port = 0) {
                var httpSkinServer = new HttpSkinServer(publicIp, port);
                httpSkinServer.Start();
                return httpSkinServer;
            }

            public void Start() {
                if (publicIp == null) {
                    FetchPublicIP();
                }

                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Server.ReceiveTimeout = 2000;
                tcpListener.Server.SendTimeout = 2000;
                tcpListener.Start();
                Debug("CustomModels Skin http server listening on {0}", tcpListener.LocalEndpoint);

                httpListenerWorkerToken = new CancellationTokenSource();
                var cancelToken = httpListenerWorkerToken.Token;
                new Task(
                    () => {
                        while (!cancelToken.IsCancellationRequested) {
                            cancelToken.ThrowIfCancellationRequested();

                            // blocks until a connection happens
                            using (TcpClient client = tcpListener.AcceptTcpClient()) {
                                cancelToken.ThrowIfCancellationRequested();

                                // can't access this after Close
                                var endpoint = client.Client.RemoteEndPoint;
                                Stopwatch sw = new Stopwatch();
                                sw.Start();
                                try {
                                    // TODO new thread?
                                    using (NetworkStream stream = client.GetStream()) {
                                        HandleStream(stream, client);
                                        stream.Flush();
                                        stream.Close();
                                    }
                                    client.Close();
                                } catch (Exception e) {
                                    client.Close();
                                    if (!(e is IOException)) {
                                        Debug("{0}", e.Message);
                                        Debug("{0}", e.StackTrace);
                                    } else {
                                        Debug("" + e.Message);
                                    }
                                }
                                sw.Stop();
                                Debug("{0}: {1}ms", endpoint, sw.Elapsed.TotalMilliseconds);
                            }
                        } // while
                    },
                    cancelToken
                ).Start();
            }

            public void Stop() {
                if (tcpListener != null) {
                    tcpListener.Stop();
                    tcpListener = null;
                }
                if (httpListenerWorkerToken != null) {
                    httpListenerWorkerToken.Cancel();
                    httpListenerWorkerToken = null;
                }
            }

            private void HandleStream(NetworkStream networkStream, TcpClient client) {
                byte[] requestBytes = new byte[4096];
                var bytesRead = networkStream.Read(requestBytes, 0, requestBytes.Length);
                var request = System.Text.Encoding.ASCII.GetString(requestBytes, 0, bytesRead);

                // GET /url+path?query HTTP/1.1
                var methodIndex = request.IndexOf(' ');
                if (methodIndex <= 0) throw new Exception("couldn't find method");
                var method = request.Substring(0, methodIndex);

                request = request.Substring(methodIndex + 1);
                var pathIndex = request.IndexOf(' ');
                if (pathIndex <= 0) throw new Exception("couldn't find path");
                var path = request.Substring(0, pathIndex);
                path = WebUtility.UrlDecode(path);

                Debug("{0} - {1} {2}", client.Client.RemoteEndPoint, method, path);

                string status;
                var contentType = "text/plain; charset=utf-8";
                byte[] bodyBytes = new byte[] { };

                if (method == "GET" && path.StartsWith("/")) {
                    try {
                        string withoutSlash = path.Substring(1);
                        var entityNameIndex = withoutSlash.IndexOf('/');
                        if (entityNameIndex <= 0) throw new Exception("invalid entityNameIndex");
                        var playerName = withoutSlash.Substring(0, entityNameIndex);
                        var entityName = withoutSlash.Substring(entityNameIndex + 1);

                        Debug("'{0}' '{1}'", playerName, entityName);

                        var p = FindPlayer(playerName);
                        if (p == null) throw new Exception("couldn't find player");

                        var e = FindEntity(p, entityName);
                        if (e == null) throw new Exception("couldn't find entity");

                        Debug("'{0}' '{1}'", p.truename, e.Model);
                        var mergedImageBytes = GenerateSkinForEntity(e);

                        status = "200 OK";
                        contentType = "image/png";
                        bodyBytes = mergedImageBytes;
                    } catch (Exception e) {
                        // TODO higher than Debug
                        Debug("{0}", e.Message);
                        Debug("{0}", e.StackTrace);
                        status = "500 Internal Server Error";
                        // bodyBytes = System.Text.Encoding.ASCII.GetBytes(e.Message);
                    }
                } else {
                    status = "404 Not Found";
                }

                string[][] headers = new[] {
                    new[] {"Content-Type", contentType},
                    new[] {"Content-Length", "" + bodyBytes.Length},
                    new[] {"Connection", "close"},
                    new[] {"Access-Control-Allow-Origin", "*"},
                    new[] {"Access-Control-Allow-Methods", "GET"},
                };

                var headersText = "";
                foreach (var pair in headers) {
                    headersText += pair[0] + ": " + pair[1] + "\r\n";
                }

                var header = "HTTP/1.1 " + status + "\r\n" + headersText + "\r\n";

                byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                networkStream.Write(headerBytes, 0, headerBytes.Length);
                networkStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            private Player FindPlayer(string truename) {
                foreach (Player p in PlayerInfo.Online.Items) {
                    if (p.truename == truename) return p;
                }
                return null;
            }

            private Entity FindEntity(Player p, string name) {
                var ag = FindPlayer(name);
                if (ag != null) {
                    return ag;
                }

                foreach (var bot in p.level.Bots.Items) {
                    if (bot.name == name) return bot;
                }
                return null;
            }

            public byte[] GenerateSkinForEntity(Entity e) {
                var model = ModelInfo.GetRawModel(e.Model);
                var storedModel = new StoredCustomModel(model);
                if (!storedModel.Exists()) throw new Exception("no model " + model);
                storedModel.LoadFromFile();
                var blockBench = storedModel.ParseBlockBench();

                var textureSheet = TextureSheet.FromTextures(
                    blockBench.textures,
                    storedModel.usesHumanSkin,
                    e.SkinName
                );

                byte[] mergedImageBytes = null;
                using (var mergedImage = new Bitmap(textureSheet.width, textureSheet.height)) {
                    using (Graphics graphics = Graphics.FromImage(mergedImage)) {
                        var x = 0;
                        foreach (var textureImage in textureSheet.textureImages) {
                            var image = textureImage.image;

                            graphics.DrawImage(
                                image,
                                x,
                                0,
                                image.Width,
                                image.Height
                            );

                            x += image.Width;
                        }
                    }

                    using (MemoryStream memoryStream = new MemoryStream()) {
                        mergedImage.Save(memoryStream, ImageFormat.Png);
                        mergedImageBytes = memoryStream.ToArray();
                    }
                }

                textureSheet.Dispose();

                return mergedImageBytes;
            }

            public string GetURL(Entity e, string name, string skin, string model, Player dst) {
                string entityName;
                if (e is Player player) {
                    entityName = player.truename;
                } else if (e is PlayerBot bot) {
                    entityName = bot.name;
                } else {
                    Debug("!!! not a Player or PlayerBot???");
                    return null;
                }

                if (publicIp == null) {
                    Debug("!!! publicIp == null");
                    return null;
                }
                if (tcpListener == null) {
                    Debug("!!! tcpListener == null");
                    return null;
                }

                if (!ShouldUseCustomURL(model)) {
                    return null;
                }

                // TODO a more generic way for sharing same images
                return string.Format(
                    "http://{0}:{1}/{2}/{3}",
                    publicIp,
                    ((IPEndPoint)tcpListener.LocalEndpoint).Port,
                    dst.truename,
                    entityName
                );
            }


            public static bool ShouldUseCustomURL(string model) {
                var storedModel = new StoredCustomModel(model);
                if (!storedModel.Exists()) {
                    Debug("!!! !storedModel.Exists()");
                    return false;
                }

                // TODO heavy, maybe cache per model name?
                var blockBench = storedModel.ParseBlockBench();
                if (blockBench.textures.Length == 0) {
                    Debug("!!! blockBench.textures.Length == 0");
                    return false;
                }

                return ShouldUseCustomURL(blockBench);
            }

            public static bool ShouldUseCustomURL(BlockBench.JsonRoot blockBench) {
                return blockBench.textures.Length >= 2;
            }


        } // HttpSkinServer

        public class TextureImage {
            public readonly uint id;

            // dun forget to Dispose these boys!
            public readonly MemoryStream memoryStream;
            public readonly Image image;

            public TextureImage(uint id, byte[] bytes) {
                this.id = id;
                this.memoryStream = new MemoryStream(bytes);
                this.image = Image.FromStream(memoryStream);
            }

            public static TextureImage FromBase64Url(uint id, string base64Url) {
                var marker = "data:image/png;base64,";
                var base64Index = base64Url.IndexOf(marker);
                if (base64Index == -1) throw new Exception("couldn't find base64 marker in url");
                var base64 = base64Url.Substring(base64Index + marker.Length);
                var bytes = Convert.FromBase64String(base64);
                return new TextureImage(id, bytes);
            }

            public static TextureImage FromTexture(BlockBench.JsonRoot.Texture texture) {
                var id = uint.Parse(texture.id);
                return TextureImage.FromBase64Url(id, texture.source);
            }

            public static TextureImage FromData(string id, byte[] bytes) {
                return new TextureImage(uint.Parse(id), bytes);
            }


            public void Dispose() {
                image.Dispose();
                memoryStream.Dispose();
            }

        }

        public class TextureSheet {
            public TextureImage[] textureImages;
            public int width;
            public int height;

            TextureSheet(TextureImage[] textureImages) {
                this.textureImages = textureImages;
                this.width = 0;
                this.height = 0;
                foreach (var textureImage in textureImages) {
                    this.width += textureImage.image.Width;
                    if (textureImage.image.Height > this.height) {
                        this.height = textureImage.image.Height;
                    }
                }
            }

            public static TextureSheet FromTextures(BlockBench.JsonRoot.Texture[] textures, bool usesHumanSkin = false, string skin = null) {
                var textureImages = new List<TextureImage>();

                var first = true;
                foreach (var texture in textures) {
                    var replaceHumanSkin = skin != null && (
                        usesHumanSkin || (
                            skin.StartsWith("http://") || skin.StartsWith("https://")
                        )
                    );
                    TextureImage textureImage = TextureImage.FromTexture(texture);
                    if (first && replaceHumanSkin) {
                        byte[] bytes = null;
                        try {
                            bytes = FetchData(GetSkinUrl(skin));
                        } catch { }
                        if (bytes != null) {



                            var textureImageOverride = TextureImage.FromData(texture.id, bytes);
                            using (var newImage = new Bitmap(textureImage.image.Width, textureImage.image.Height)) {
                                using (Graphics graphics = Graphics.FromImage(newImage)) {
                                    graphics.DrawImage(
                                        textureImageOverride.image,
                                        0,
                                        0,
                                        textureImage.image.Width,
                                        textureImage.image.Height
                                    );
                                }
                                textureImageOverride.Dispose();

                                using (MemoryStream memoryStream = new MemoryStream()) {
                                    newImage.Save(memoryStream, ImageFormat.Png);
                                    var newImageBytes = memoryStream.ToArray();
                                    textureImage.Dispose();
                                    textureImage = TextureImage.FromData(texture.id, newImageBytes);
                                }
                            }
                        }
                    }
                    first = false;
                    textureImages.Add(textureImage);
                }

                return new TextureSheet(textureImages.ToArray());
            }

            public void Dispose() {
                for (int i = textureImages.Length - 1; i >= 0; i--) {
                    var textureImage = textureImages[i];
                    textureImage.Dispose();
                }
            }
        }
    }
}
