using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace XinTiao;

/// <summary>
/// TCP 客户端帮助类 - 可指定本地端点，独立线程读取，避免大数据量丢包
/// （与 HelpSerialPort 保持相同的使用方式：Open / Send / TakeAllBytes / Close）
/// </summary>
public class HelpTcp : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _readThread;
    private volatile bool _isRunning;
    private readonly Lock _lockObj = new();

    // 接收数据缓冲区（线程安全队列）
    private readonly ConcurrentQueue<byte> _receiveBuffer = new();

    /// <summary>发生错误时触发</summary>
    public event Action<string, Exception>? ErrorOccurred;

    /// <summary>对端断开 / 连接异常时触发（文本说明）</summary>
    public event Action<string>? Disconnected;

    /// <summary>是否已连接</summary>
    public bool IsOpen => _client is { Connected: true };

    /// <summary>本地端点（如 192.168.1.10:50234）</summary>
    public string LocalEndPoint => _client?.Client.LocalEndPoint?.ToString() ?? "";

    /// <summary>对端端点</summary>
    public string RemoteEndPoint => _client?.Client.RemoteEndPoint?.ToString() ?? "";

    /// <summary>
    /// 连接对端
    /// </summary>
    /// <param name="localIp">本地IP，空或 0.0.0.0 表示任意网卡</param>
    /// <param name="localPort">本地端口，0 表示由系统自动分配</param>
    /// <param name="remoteIp">对端IP</param>
    /// <param name="remotePort">对端端口</param>
    /// <param name="timeoutMs">连接超时（毫秒）</param>
    public bool Open(string localIp, int localPort, string remoteIp, int remotePort, int timeoutMs = 3000)
    {
        lock (_lockObj)
        {
            if (IsOpen) return true;

            if (!IPAddress.TryParse(remoteIp, out var remoteAddr))
            {
                ErrorOccurred?.Invoke("对端IP地址不正确",
                    new FormatException($"无法识别的对端IP：{remoteIp}"));
                return false;
            }

            var localAddr = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(localIp) && localIp != "0.0.0.0" &&
                !IPAddress.TryParse(localIp, out localAddr))
            {
                ErrorOccurred?.Invoke("本地IP地址不正确",
                    new FormatException($"无法识别的本地IP：{localIp}"));
                return false;
            }

            try
            {
                _client = new TcpClient { NoDelay = true };

                // 本地端口 > 0 时才绑定，避免与其他程序冲突
                if (localPort is > 0 and <= 65535)
                    _client.Client.Bind(new IPEndPoint(localAddr, localPort));

                var connectTask = _client.ConnectAsync(remoteAddr, remotePort);
                if (!connectTask.Wait(timeoutMs))
                {
                    CleanupClient();
                    ErrorOccurred?.Invoke("连接超时",
                        new TimeoutException($"连接 {remoteAddr}:{remotePort} 超时（{timeoutMs} 毫秒）"));
                    return false;
                }

                _stream = _client.GetStream();
                _stream.ReadTimeout = 500;    // 读超时用于及时响应关闭
                _stream.WriteTimeout = 3000;

                _isRunning = true;
                _readThread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "TcpReadThread"
                };
                _readThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                CleanupClient();
                ErrorOccurred?.Invoke($"连接 {remoteIp}:{remotePort} 失败", ex.InnerException ?? ex);
                return false;
            }
        }
    }

    /// <summary>关闭连接</summary>
    public void Close()
    {
        lock (_lockObj)
        {
            if (_client == null) return;

            _isRunning = false;

            try
            {
                if (_readThread != null && _readThread.IsAlive &&
                    _readThread != Thread.CurrentThread)
                {
                    _readThread.Join(500);
                }
            }
            catch { }

            CleanupClient();
        }
    }

    /// <summary>发送数据</summary>
    public bool Send(byte[] data)
    {
        if (!IsOpen || data == null || data.Length == 0) return false;
        try
        {
            lock (_lockObj)
            {
                _stream!.Write(data, 0, data.Length);
                _stream.Flush();
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("TCP 发送数据失败", ex);
            return false;
        }
    }

    /// <summary>发送字符串</summary>
    public bool Send(string text, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return Send(encoding.GetBytes(text));
    }

    /// <summary>从缓冲区取走所有已接收的字节（消费式读取）</summary>
    public byte[] TakeAllBytes()
    {
        var list = new System.Collections.Generic.List<byte>();
        while (_receiveBuffer.TryDequeue(out byte b))
            list.Add(b);
        return [.. list];
    }

    /// <summary>清空接收缓冲区</summary>
    public void ClearBuffer()
    {
        while (_receiveBuffer.TryDequeue(out _)) { }
    }

    /// <summary>独立读取线程循环</summary>
    private void ReadLoop()
    {
        var tempBuf = new byte[8192];

        while (_isRunning)
        {
            try
            {
                var stream = _stream;
                if (stream == null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                int n = stream.Read(tempBuf, 0, tempBuf.Length);
                if (n <= 0)
                {
                    // 读到 0 表示对端已关闭连接
                    if (_isRunning)
                    {
                        _isRunning = false;
                        Disconnected?.Invoke("对端已关闭连接");
                    }
                    break;
                }

                for (int i = 0; i < n; i++)
                    _receiveBuffer.Enqueue(tempBuf[i]);
            }
            catch (IOException ex) when (ex.InnerException is SocketException se &&
                                         se.SocketErrorCode == SocketError.TimedOut)
            {
                // 读超时属正常，继续循环
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    _isRunning = false;
                    Disconnected?.Invoke("连接已断开：" + ex.Message);
                }
                break;
            }
        }
    }

    private void CleanupClient()
    {
        try
        {
            _stream?.Dispose();
            if (_client != null)
            {
                _client.Close();
                _client.Dispose();
            }
        }
        catch { }
        finally
        {
            _stream = null;
            _client = null;
            _readThread = null;
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// UDP 帮助类 - 绑定本地端点接收，向对端端点发送，独立线程读取
/// </summary>
public class HelpUdp : IDisposable
{
    private UdpClient? _udp;
    private Thread? _readThread;
    private volatile bool _isRunning;
    private readonly Lock _lockObj = new();

    private readonly ConcurrentQueue<byte> _receiveBuffer = new();

    // 当前对端端点（发送目标）
    private IPEndPoint? _remote;

    /// <summary>发生错误时触发</summary>
    public event Action<string, Exception>? ErrorOccurred;

    /// <summary>是否已打开</summary>
    public bool IsOpen => _udp != null;

    /// <summary>本地端点</summary>
    public string LocalEndPoint => _udp?.Client.LocalEndPoint?.ToString() ?? "";

    /// <summary>对端端点</summary>
    public string RemoteEndPoint => _remote?.ToString() ?? "";

    /// <summary>
    /// 打开本地端口
    /// </summary>
    /// <param name="localIp">本地IP，空或 0.0.0.0 表示监听所有网卡</param>
    /// <param name="localPort">本地端口，0 表示由系统自动分配</param>
    /// <param name="remoteIp">对端IP（发送目标）</param>
    /// <param name="remotePort">对端端口（发送目标）</param>
    public bool Open(string localIp, int localPort, string remoteIp, int remotePort)
    {
        lock (_lockObj)
        {
            if (_udp != null) return true;

            if (!TryParseRemote(remoteIp, remotePort, out var remote, out string error))
            {
                ErrorOccurred?.Invoke("对端参数不正确", new FormatException(error));
                return false;
            }

            var localAddr = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(localIp) && localIp != "0.0.0.0" &&
                !IPAddress.TryParse(localIp, out localAddr))
            {
                ErrorOccurred?.Invoke("本地IP地址不正确",
                    new FormatException($"无法识别的本地IP：{localIp}"));
                return false;
            }

            try
            {
                _udp = new UdpClient(new IPEndPoint(localAddr, localPort));
                _udp.Client.ReceiveTimeout = 500; // 用于及时响应关闭
                _remote = remote;

                _isRunning = true;
                _readThread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "UdpReadThread"
                };
                _readThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                CleanupUdp();
                ErrorOccurred?.Invoke($"打开本地端口 {localPort} 失败", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// 运行中修改发送目标（对端IP/端口）
    /// </summary>
    public bool UpdateRemote(string remoteIp, int remotePort, out string error)
    {
        if (!TryParseRemote(remoteIp, remotePort, out var remote, out error))
            return false;

        lock (_lockObj)
        {
            _remote = remote;
        }
        return true;
    }

    /// <summary>关闭</summary>
    public void Close()
    {
        lock (_lockObj)
        {
            if (_udp == null) return;

            _isRunning = false;

            try
            {
                if (_readThread != null && _readThread.IsAlive &&
                    _readThread != Thread.CurrentThread)
                {
                    _readThread.Join(500);
                }
            }
            catch { }

            CleanupUdp();
        }
    }

    /// <summary>发送数据到对端端点</summary>
    public bool Send(byte[] data)
    {
        if (_udp == null || _remote == null || data == null || data.Length == 0) return false;
        try
        {
            lock (_lockObj)
            {
                _udp.Send(data, data.Length, _remote);
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("UDP 发送数据失败", ex);
            return false;
        }
    }

    /// <summary>发送字符串</summary>
    public bool Send(string text, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return Send(encoding.GetBytes(text));
    }

    /// <summary>从缓冲区取走所有已接收的字节（消费式读取）</summary>
    public byte[] TakeAllBytes()
    {
        var list = new System.Collections.Generic.List<byte>();
        while (_receiveBuffer.TryDequeue(out byte b))
            list.Add(b);
        return [.. list];
    }

    /// <summary>清空接收缓冲区</summary>
    public void ClearBuffer()
    {
        while (_receiveBuffer.TryDequeue(out _)) { }
    }

    /// <summary>独立读取线程循环</summary>
    private void ReadLoop()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);

        while (_isRunning)
        {
            try
            {
                var udp = _udp;
                if (udp == null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var data = udp.Receive(ref from);
                for (int i = 0; i < data.Length; i++)
                    _receiveBuffer.Enqueue(data[i]);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut
                                                 or SocketError.ConnectionReset
                                                 or SocketError.ConnectionRefused
                                                 or SocketError.MessageSize)
            {
                // 超时 / ICMP 端口不可达 均属正常，继续循环
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                    ErrorOccurred?.Invoke("UDP 接收数据出错", ex);
                Thread.Sleep(100);
            }
        }
    }

    private static bool TryParseRemote(string remoteIp, int remotePort, out IPEndPoint remote, out string error)
    {
        remote = new IPEndPoint(IPAddress.Any, 0);
        error = "";

        if (!IPAddress.TryParse(remoteIp, out var addr))
        {
            error = $"无法识别的对端IP：{remoteIp}";
            return false;
        }

        if (remotePort is < 1 or > 65535)
        {
            error = "对端端口必须在 1 - 65535 之间";
            return false;
        }

        remote = new IPEndPoint(addr, remotePort);
        return true;
    }

    private void CleanupUdp()
    {
        try
        {
            _udp?.Dispose();
        }
        catch { }
        finally
        {
            _udp = null;
            _readThread = null;
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// TCP 服务端帮助类 - 监听本地端口，允许接入多个客户端，接收数据汇总到同一缓冲区，
/// 发送时广播给所有已连接客户端（与 HelpTcp / HelpUdp 保持相同的使用方式）
/// </summary>
public class HelpTcpServer : IDisposable
{
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _isRunning;
    private readonly Lock _lockObj = new();
    private int _clientSeq;

    // 接收数据缓冲区（所有客户端共用，线程安全）
    private readonly ConcurrentQueue<byte> _receiveBuffer = new();

    // 已连接客户端：key 为“IP:端口”
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new(StringComparer.Ordinal);

    /// <summary>发生错误时触发</summary>
    public event Action<string, Exception>? ErrorOccurred;

    /// <summary>客户端接入 / 断开时触发（后台线程，界面需切回 UI 线程）</summary>
    public event Action? ClientsChanged;

    /// <summary>是否正在监听</summary>
    public bool IsOpen => _listener != null;

    /// <summary>当前已连接客户端数量</summary>
    public int ClientCount => _clients.Count;

    /// <summary>已连接客户端端点列表（如 192.168.1.20:50234, 192.168.1.21:50235）</summary>
    public string ClientEndPoints => _clients.IsEmpty ? string.Empty : string.Join(", ", _clients.Keys);

    /// <summary>本地监听端点</summary>
    public string LocalEndPoint => _listener?.LocalEndpoint?.ToString() ?? "";

    /// <summary>
    /// 开始监听
    /// </summary>
    /// <param name="localIp">本地IP，空或 0.0.0.0 表示监听所有网卡</param>
    /// <param name="localPort">本地端口，服务端必须 1 - 65535</param>
    public bool Open(string localIp, int localPort)
    {
        lock (_lockObj)
        {
            if (_listener != null) return true;

            var localAddr = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(localIp) && localIp != "0.0.0.0" &&
                !IPAddress.TryParse(localIp, out localAddr))
            {
                ErrorOccurred?.Invoke("本地IP地址不正确",
                    new FormatException($"无法识别的本地IP：{localIp}"));
                return false;
            }

            if (localPort is < 1 or > 65535)
            {
                ErrorOccurred?.Invoke("本地端口不正确",
                    new FormatException("服务端监听端口必须在 1 - 65535 之间"));
                return false;
            }

            try
            {
                _listener = new TcpListener(localAddr, localPort);
                _listener.Start();

                _isRunning = true;
                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "TcpAcceptThread"
                };
                _acceptThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                CleanupListener();
                ErrorOccurred?.Invoke($"监听 {localIp}:{localPort} 失败（端口可能已被占用）", ex);
                return false;
            }
        }
    }

    /// <summary>发送数据：广播给所有已连接客户端</summary>
    public bool Send(byte[] data)
    {
        if (_listener == null || data == null || data.Length == 0) return false;

        bool any = false;
        var failed = new System.Collections.Generic.List<string>();

        lock (_lockObj)
        {
            foreach (var pair in _clients)
            {
                try
                {
                    pair.Value.GetStream().Write(data, 0, data.Length);
                    any = true;
                }
                catch
                {
                    failed.Add(pair.Key);
                }
            }
        }

        foreach (string key in failed)
            RemoveClient(key);

        return any;
    }

    /// <summary>发送字符串</summary>
    public bool Send(string text, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return Send(encoding.GetBytes(text));
    }

    /// <summary>从缓冲区取走所有已接收的字节（消费式读取）</summary>
    public byte[] TakeAllBytes()
    {
        var list = new System.Collections.Generic.List<byte>();
        while (_receiveBuffer.TryDequeue(out byte b))
            list.Add(b);
        return [.. list];
    }

    /// <summary>清空接收缓冲区</summary>
    public void ClearBuffer()
    {
        while (_receiveBuffer.TryDequeue(out _)) { }
    }

    /// <summary>停止监听并断开所有客户端</summary>
    public void Close()
    {
        lock (_lockObj)
        {
            if (_listener == null) return;

            _isRunning = false;

            try
            {
                // 停止监听会让阻塞中的 AcceptTcpClient 抛出异常，从而退出接受线程
                _listener.Stop();
            }
            catch { }

            foreach (string key in _clients.Keys)
            {
                try
                {
                    _clients[key].Close();
                }
                catch { }
            }

            _clients.Clear();

            try
            {
                if (_acceptThread != null && _acceptThread.IsAlive &&
                    _acceptThread != Thread.CurrentThread)
                {
                    _acceptThread.Join(500);
                }
            }
            catch { }

            CleanupListener();
        }
    }

    /// <summary>接受客户端连接的循环</summary>
    private void AcceptLoop()
    {
        while (_isRunning)
        {
            try
            {
                var listener = _listener;
                if (listener == null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                TcpClient client = listener.AcceptTcpClient();
                client.NoDelay = true;

                string key = client.Client.RemoteEndPoint?.ToString()
                             ?? "client-" + Interlocked.Increment(ref _clientSeq);

                _clients[key] = client;

                var readThread = new Thread(() => ClientReadLoop(key, client))
                {
                    IsBackground = true,
                    Name = "TcpServerReadThread"
                };
                readThread.Start();

                ClientsChanged?.Invoke();
            }
            catch (SocketException)
            {
                // Stop() 之后 AcceptTcpClient 会抛异常，属正常退出
                if (!_isRunning) break;
                Thread.Sleep(50);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_isRunning) break;
                ErrorOccurred?.Invoke("等待客户端连接失败", ex);
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>单个客户端的读取循环</summary>
    private void ClientReadLoop(string key, TcpClient client)
    {
        var tempBuf = new byte[8192];

        try
        {
            var stream = client.GetStream();
            stream.ReadTimeout = 500; // 读超时用于及时响应关闭

            while (_isRunning)
            {
                try
                {
                    int n = stream.Read(tempBuf, 0, tempBuf.Length);
                    if (n <= 0) break; // 对端关闭

                    for (int i = 0; i < n; i++)
                        _receiveBuffer.Enqueue(tempBuf[i]);
                }
                catch (IOException ex) when (ex.InnerException is SocketException se &&
                                             se.SocketErrorCode == SocketError.TimedOut)
                {
                    // 读超时属正常，继续循环
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        System.Diagnostics.Debug.WriteLine("TCP 服务端读取客户端数据出错：" + ex.Message);
                    break;
                }
            }
        }
        catch
        {
            // 取流失败，按断开处理
        }
        finally
        {
            RemoveClient(key);
        }
    }

    /// <summary>移除并关闭一个客户端</summary>
    private void RemoveClient(string key)
    {
        if (!_clients.TryRemove(key, out var client)) return;

        try
        {
            client.Close();
        }
        catch { }

        ClientsChanged?.Invoke();
    }

    private void CleanupListener()
    {
        try
        {
            _listener?.Stop();
        }
        catch { }
        finally
        {
            _listener = null;
            _acceptThread = null;
        }
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}
