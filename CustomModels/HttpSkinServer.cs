using System;
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
            private string publicIp = null;

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

            public void Start(string publicIp = null, int port = 0) {
                if (publicIp == null) {
                    FetchPublicIP();
                } else {
                    this.publicIp = publicIp;
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
                                    Debug("{0}", e.Message);
                                    Debug("{0}", e.StackTrace);
                                }
                            }
                        } // while
                    },
                    cancelToken
                ).Start();
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

                Debug(client.Client.RemoteEndPoint + " - {0} {1}", method, path);

                string status;
                var contentType = "text/plain; charset=utf-8";
                byte[] bodyBytes = new byte[] { };

                if (method == "GET" && path.StartsWith("/?")) {
                    string query = path.Substring(2);
                    Debug("{0}", query);

                    try {
                        byte[] mergedImageBytes = null;
                        using (Bitmap bmp = FetchBitmap(GetSkinUrl(query))) {
                            using (Image overlay = Image.FromFile("overlay.png")) {
                                if (overlay.Size.Width > bmp.Size.Width || overlay.Size.Height > bmp.Size.Height) {
                                    throw new Exception("overlay image is bigger than skin image");
                                }

                                using (Graphics graphics = Graphics.FromImage(bmp)) {
                                    graphics.DrawImage(overlay, new Rectangle(new Point(), overlay.Size),
                                        new Rectangle(new Point(), overlay.Size), GraphicsUnit.Pixel);
                                }

                                using (MemoryStream memoryStream = new MemoryStream()) {
                                    bmp.Save(memoryStream, ImageFormat.Png);
                                    mergedImageBytes = memoryStream.ToArray();
                                }
                            }
                        }

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

            public string GetURL(string skin, string model) {
                if (publicIp == null) {
                    Debug("!!! publicIp == null");
                    return null;
                }
                if (tcpListener == null) {
                    Debug("!!! tcpListener == null");
                    return null;
                }

                var ip = publicIp;
                var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                var path = "/path";
                return string.Format(
                    "http://{0}:{1}{2}",
                    ip,
                    port,
                    path
                );
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
        }
    }
}
