using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace XinTiao;

public class HelpSerialPort_
{
    /// <summary>
    /// 串口帮助类 - 使用独立读取线程避免大数据量丢包
    /// </summary>
    public class HelpSerialPort : IDisposable
    {
        private SerialPort _serialPort;
        private Thread _readThread;
        private volatile bool _isRunning;
        private readonly Lock _lockObj = new();

        // 接收数据缓冲区（线程安全队列）
        private readonly ConcurrentQueue<byte> _receiveBuffer = new ConcurrentQueue<byte>();

        //这里是两个触发事件
        // /// <summary>收到数据时触发（已在后台线程，注意 UI 调度）</summary>
        // public event Action<byte[]> DataReceived;
        //
        // /// <summary>收到文本数据时触发</summary>
        // public event Action<string> TextReceived;

        /// <summary>发生错误时触发</summary>
        public event Action<string, Exception> ErrorOccurred;

        /// <summary>是否已打开</summary>
        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        /// <summary>串口名称</summary>
        public string PortName => _serialPort?.PortName;

        /// <summary>
        /// 打开串口
        /// </summary>
        public bool Open(string portName, int baudRate = 9600,
                         Parity parity = Parity.None, int dataBits = 8,
                         StopBits stopBits = StopBits.One, int readTimeout = 500)
        {
            lock (_lockObj)
            {
                if (IsOpen) return true;

                try
                {
                    _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
                    {
                        ReadTimeout = readTimeout,
                        WriteTimeout = 1000,
                        ReadBufferSize = 65536,   // 加大底层缓冲
                        WriteBufferSize = 65536,
                        DtrEnable = true,
                        RtsEnable = true
                    };

                    _serialPort.Open();
                    _isRunning = true;

                    // 启动独立读取线程
                    _readThread = new Thread(ReadLoop)
                    {
                        IsBackground = true,
                        Name = "SerialReadThread",
                        Priority = ThreadPriority.AboveNormal
                    };
                    _readThread.Start();

                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"打开串口 {portName} 失败", ex);
                    CleanupPort();
                    return false;
                }
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            lock (_lockObj)
            {
                if (!IsOpen && _serialPort == null) return;

                _isRunning = false;

                try
                {
                    // 等待读取线程退出
                    if (_readThread != null && _readThread.IsAlive)
                    {
                        _readThread.Join(1000);
                    }
                }
                catch { }

                CleanupPort();
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public bool Send(byte[] data)
        {
            if (!IsOpen || data == null || data.Length == 0) return false;
            try
            {
                lock (_lockObj)
                {
                    _serialPort.Write(data, 0, data.Length);
                }
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("发送数据失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 发送字符串
        /// </summary>
        public bool Send(string text, Encoding encoding = null)
        {
            encoding ??= Encoding.UTF8;
            return Send(encoding.GetBytes(text));
        }

        /// <summary>
        /// 从缓冲区取走所有已接收的字节（消费式读取）
        /// </summary>
        public byte[] TakeAllBytes()
        {
            var list = new System.Collections.Generic.List<byte>();
            while (_receiveBuffer.TryDequeue(out byte b))
                list.Add(b);
            return [..list];
        }

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        public void ClearBuffer()
        {
            while (_receiveBuffer.TryDequeue(out _)) { }
        }

        /// <summary>
        /// 独立读取线程循环
        /// </summary>
        private void ReadLoop()
        {
            var tempBuf = new byte[8192];

            while (_isRunning)
            {
                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    int n = _serialPort.Read(tempBuf, 0, tempBuf.Length);
                    if (n <= 0) continue;

                    // 1. 放入缓冲区
                    for (int i = 0; i < n; i++)
                        _receiveBuffer.Enqueue(tempBuf[i]);

                    //这里是两个触发事件
                    // // 2. 复制一份触发事件（不影响缓冲区）
                    // var chunk = new byte[n];
                    // Buffer.BlockCopy(tempBuf, 0, chunk, 0, n);
                    //
                    // DataReceived?.Invoke(chunk);
                    // TextReceived?.Invoke(Encoding.UTF8.GetString(chunk));
                }
                catch (TimeoutException)
                {
                    // 读超时属正常，继续循环
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        ErrorOccurred?.Invoke("读取串口数据出错", ex);
                    Thread.Sleep(100);
                }
            }
        }

        private void CleanupPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();
                    _serialPort.Dispose();
                }
            }
            catch { }
            finally
            {
                _serialPort = null;
                _readThread = null;
            }
        }

        /// <summary>
        /// 获取可用串口列表
        /// </summary>
        public static string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}