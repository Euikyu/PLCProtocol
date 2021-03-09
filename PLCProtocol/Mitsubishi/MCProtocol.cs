using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PLCProtocol.Mitsubishi
{
    /// <summary>
    /// Class related to Mitsubishi PLC Protocol.
    /// </summary>
    public class MCProtocol : IDisposable
    {
        #region Fields
        private readonly object m_CommunicateLock = new object();
        private readonly object m_QueueLock = new object();
        private readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MCProtocol));

        private Queue<byte[]> m_ReceivedMessageQueue = new Queue<byte[]>();

        private TcpClient m_Client;
        private NetworkStream m_Stream;
        private IPAddress m_IPAddress;
        private ushort m_Timeout;


        private Thread m_ConnectionCheckThread;
        private Thread m_ReadingThread;
        #endregion

        #region Properties
        /// <summary>
        /// Destination IP of PLC to connect.
        /// </summary>
        public string IP
        {
            get { return m_IPAddress == null ? string.Empty : m_IPAddress.ToString(); }
            private set
            {
                var strBuf = value.Split('.');
                if (strBuf.Length == 4)
                {
                    byte[] tmpIP = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        byte tmpAddress = 0;
                        if (!byte.TryParse(strBuf[i], out tmpAddress))
                        {
                            return;
                        }
                        tmpIP[i] = tmpAddress;
                    }
                    m_IPAddress = new IPAddress(tmpIP);
                }
            }
        }
        /// <summary>
        /// Destination port number of PLC to connect.
        /// </summary>
        public int PortNumber { get; private set; }
        /// <summary>
        /// Initialized PC code in PLC LAN port. (Default value is 0xFF)
        /// </summary>
        public byte PCNo { get; set; }
        /// <summary>
        /// Initialized Network code in PLC LAN port. (Default value is 0x00)
        /// </summary>
        public byte NetworkNo { get; set; }
        /// <summary>
        /// Initialized protocol format in PLC.
        /// </summary>
        public EMCProtocolFormat ProtocolFormat { get; set; }
        /// <summary>
        /// Waiting time for reply.
        /// </summary>
        public uint Timeout
        {
            get
            {
                return (uint)m_Timeout * 250;
            }
            set
            {
                m_Timeout = (ushort)(value / 250);
            }
        }
        /// <summary>
        /// Define that PLC is successfully communicating PC.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return m_IPAddress != null && m_Client != null && m_Client.Connected;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Create Mitsubishi PLC class on default value.
        /// </summary>
        public MCProtocol()
        {
            this.IP = "192.168.10.100";
            this.PortNumber = 6000;
            this.ProtocolFormat = EMCProtocolFormat.Binary;
            this.NetworkNo = 0x00;
            this.PCNo = 0xFF;
            this.Timeout = 1000;
        }
        /// <summary>
        /// Create Mitsubishi PLC class on specific values.
        /// </summary>
        /// <param name="ipAddress">IP to connect.</param>
        /// <param name="portNum">Port number to connect.</param>
        /// <param name="protocolFormat">Protocol format in PLC to connect.</param>
        /// <param name="networkNo">Network code in PLC LAN port to connect.</param>
        /// <param name="pcNo">PC code in PLC LAN port to connect.</param>
        public MCProtocol(string ipAddress, int portNum, EMCProtocolFormat protocolFormat = EMCProtocolFormat.Binary, byte networkNo = 0x00, byte pcNo = 0xFF)
        {
            this.IP = ipAddress;
            this.PortNumber = portNum;
            this.ProtocolFormat = protocolFormat;
            this.NetworkNo = networkNo;
            this.PCNo = pcNo;
            this.Timeout = 4000;
        }

        /// <summary>
        /// Dispose this class.
        /// </summary>
        public void Dispose()
        {
            this.Disconnect();
        }

        #endregion

        #region Methods
        /// <summary>
        /// Connect PLC to current IP & port number.
        /// </summary>
        /// <returns></returns>
        public void Connect()
        {
            if (IsConnected) throw new Exception("PLC_ALREADY_OPEN_ERR");
            if (m_IPAddress == null) throw new Exception("PLC_INVALID_IP_ERR");

            try
            {
                m_Client = new TcpClient();
                m_Client.Connect(m_IPAddress, PortNumber);
                m_Stream = m_Client.GetStream();
            }
            catch
            {
                throw;
            }
            finally
            {
                if (m_ConnectionCheckThread == null || !m_ConnectionCheckThread.IsAlive)
                {
                    m_ConnectionCheckThread = new Thread(new ThreadStart(this.OnCheckProcess))
                    {
                        Name = "PLC Check"
                    };
                    m_ConnectionCheckThread.Start();
                }
            }

            if (m_ReadingThread == null || !m_ReadingThread.IsAlive)
            {
                m_ReadingThread = new Thread(new ThreadStart(this.WaitReadProcess))
                {
                    Name = "PLC Data Reading"
                };
                m_ReadingThread.Start();
            }
        }

        /// <summary>
        /// Connect PLC to specific IP & port number.
        /// </summary>
        /// <param name="ip">IP to connect newly.</param>
        /// <param name="portNum">Port number to connect newly.</param>
        /// <returns></returns>
        public void Connect(string ip, int portNum)
        {
            try
            {
                this.IP = ip;
                this.PortNumber = portNum;

                this.Connect();
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Disconnect this communication.
        /// </summary>
        /// <returns></returns>
        public void Disconnect()
        {

            if (m_ConnectionCheckThread != null && m_ConnectionCheckThread.IsAlive)
            {
                m_ConnectionCheckThread.Abort();
                m_ConnectionCheckThread.Join();
            }

            if (m_Stream != null)
            {
                m_Stream.Flush();
                m_Stream.Close(500);
                m_Stream = null;
            }

            if (m_ReadingThread != null && m_ReadingThread.IsAlive)
            {
                m_ReadingThread.Abort();
                m_ReadingThread.Join();
            }

            if (m_Client != null)
            {
                m_Client.Close();
                m_Client = null;
            }
        }

        /// <summary>
        /// Refresh this communication.
        /// </summary>
        /// <returns></returns>
        public void Refresh()
        {
            try
            {
                if (m_Stream != null)
                {
                    m_Stream.Flush();
                    m_Stream.Close(500);
                    m_Stream = null;
                }

                if (m_ReadingThread != null && m_ReadingThread.IsAlive)
                {
                    m_ReadingThread.Abort();
                    m_ReadingThread.Join();
                }

                if (m_Client != null)
                {
                    m_Client.Close();
                    m_Client = null;
                }

                this.Connect();
            }
            catch(Exception err)
            {
                _log.Fatal("PLC - " + err.Message);
            }
        }

        /// <summary>
        /// Send message that try to write data having one address on PLC.
        /// </summary>
        /// <param name="data">A address data to write.</param>
        /// <returns></returns>
        public void Write(MCPSendData data)
        {
            try
            {
                if (!IsConnected) throw new Exception("PLC_NOT_OPEN_ERR");
                if (data.IsRead) throw new Exception("PLC_WRONG_MESSAGE_FORMAT_ERR");

                this.SendMsg(data);
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Send message that try to write data having several addresses on PLC.
        /// </summary>
        /// <param name="dataArr">Address data list to write.</param>
        /// <returns></returns>
        public void Write(IEnumerable<MCPSendData> dataArr)
        {
            try
            {
                if (!IsConnected) throw new Exception("PLC_NOT_OPEN_ERR");
                if (dataArr.Any(data => data.IsRead)) throw new Exception("PLC_WRONG_MESSAGE_FORMAT_ERR");

                this.SendMsg(dataArr);                
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Send message that try to bring data having one addresses on PLC.
        /// </summary>
        /// <param name="data">A address data to bring.</param>
        /// <param name="receiveValue">Received value in MCPReceiveData instance.</param>
        /// <returns></returns>
        public void Read(MCPSendData data, ref MCPReceiveData receiveValue)
        {
            try
            {
                if (!IsConnected) throw new Exception("PLC_NOT_OPEN_ERR");
                if (!data.IsRead) throw new Exception("PLC_WRONG_MESSAGE_FORMAT_ERR");

                this.SendMsg(data, ref receiveValue);
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Send message that try to bring data having several addresses on PLC.
        /// </summary>
        /// <param name="dataArr">Address data list to bring.</param>
        /// <param name="receiveValueList">Received data list in byte array.</param>
        /// <returns></returns>
        public void Read(IEnumerable<MCPSendData> dataArr, ref List<MCPReceiveData> receiveValueList)
        {
            try
            {
                if (!IsConnected) throw new Exception("PLC_NOT_OPEN_ERR");
                if (dataArr.Any(data => !data.IsRead)) throw new Exception("PLC_WRONG_MESSAGE_FORMAT_ERR");

                this.SendMsg(dataArr, ref receiveValueList);
                
            }
            catch
            {
                throw;
            }
        }


        private void OnCheckProcess()
        {
            try
            {
                while (true)
                {
                    //it change false only stream.Read() or stream.Write() is failed.
                    if (!IsConnected)
                    {
                        //retry communicating
                        this.Refresh();
                    }

                    //Duration 1 sec
                    Thread.Sleep(1000);
                }
            }
            catch (ThreadAbortException)
            {

            }
            catch (Exception err)
            {
                _log.Fatal("PLC Thread - " + err.Message + Environment.NewLine + "Please check LAN cable or PLC power.");
            }
        }

        private void WaitReadProcess()
        {
            try
            {
                byte[] buffer = new byte[256];

                int len = 0;
                while ((len = m_Stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (len == buffer.Length)
                    {
                        List<byte> combinedBufferList = new List<byte>(buffer);
                        do
                        {
                            len = m_Stream.Read(buffer, 0, buffer.Length);
                            combinedBufferList.AddRange(buffer.Take(len));
                        } while (len == buffer.Length);
                        lock (m_QueueLock)
                        {
                            m_ReceivedMessageQueue.Enqueue(combinedBufferList.ToArray());
                        }
                    }
                    else
                    {
                        lock (m_QueueLock)
                        {
                            m_ReceivedMessageQueue.Enqueue(buffer.Take(len).ToArray());
                        }
                    }
                }
                if (m_Stream != null)
                {
                    m_Stream.Flush();
                    m_Stream.Close(500);
                    m_Stream = null;
                }
            }
            catch (SocketException) { }
            catch (IOException) { }
            catch (ThreadAbortException) { }
            catch (Exception err)
            {
                _log.Fatal("PLC Reading - " + err.Message + Environment.NewLine + err.StackTrace);
            }
        }
        #region Communicate Methods
        private void SendMsg(MCPSendData data)
        {
            byte[] sendMsg = null;
            ushort msgDataLength = 0;
            ushort devCount = 0;
            Type dataType = data.Value.GetType();

            switch (ProtocolFormat)
            {
                case EMCProtocolFormat.ASCII:
                    string strHeader = "5000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";
                    string strData = m_Timeout.ToString("X4") + "1401";
                    if (data.Value is IEnumerable<bool> || dataType == typeof(bool))
                    {
                        strData += "0001" + this.ConvertStringFromAddress(data);
                        if (data.Value.GetType() == typeof(bool))
                        {
                            strData += "0001" + this.Convert1BitStringFromBooleanData((bool)data.Value);
                        }
                        else
                        {
                            var sendData = this.ConvertNBitStringFromBooleanArrayData(data.Value as IEnumerable<bool>);
                            devCount = (ushort)sendData.Length;
                            strData += devCount.ToString("X4") + sendData;
                        }

                        msgDataLength = (ushort)strData.Length;
                    }
                    else
                    {
                        strData += "0000" + this.ConvertStringFromAddress(data);
                        string sendData = string.Empty;
                        if (data.Value is IEnumerable itemList && !(dataType == typeof(string) || data.Value is IEnumerable<char> || data.Value is IEnumerable<byte>)) sendData = this.ConvertMultiWordsStringFromDataList(itemList);
                        else sendData = this.ConvertMultiWordsStringFromData(data.Value);
                        devCount = (ushort)(sendData.Length / 4);
                        strData += devCount.ToString("X4") + sendData;

                        msgDataLength = (ushort)strData.Length;
                    }

                    sendMsg = Encoding.ASCII.GetBytes(strHeader + msgDataLength.ToString("X4") + strData);

                    break;

                case EMCProtocolFormat.Binary:
                    List<byte> binHeader = new List<byte>() { 0x50, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };
                    List<byte> binData = new List<byte>(BitConverter.GetBytes(m_Timeout));
                    binData.Add(0x01);
                    binData.Add(0x14);

                    if (data.Value is IEnumerable<bool> || dataType == typeof(bool))
                    {
                        binData.Add(0x01);
                        binData.Add(0x00);
                        binData.AddRange(this.ConvertByteArrayFromAddress(data));
                        if (data.Value.GetType() == typeof(bool))
                        {
                            binData.Add(0x01);
                            binData.Add(0x00);
                            binData.Add(this.Convert1BitByteFromBooleanData((bool)data.Value));
                        }
                        else
                        {
                            var sendData = this.ConvertNBitByteArrayFromBooleanArrayData(data.Value as IEnumerable<bool>);
                            devCount = (ushort)(data.Value as IEnumerable<bool>).Count();
                            binData.AddRange(BitConverter.GetBytes(devCount));
                            binData.AddRange(sendData);
                        }

                        msgDataLength = (ushort)binData.Count;
                    }
                    else
                    {
                        binData.Add(0x00);
                        binData.Add(0x00);
                        binData.AddRange(this.ConvertByteArrayFromAddress(data));

                        byte[] sendData = null;
                        if (data.Value is IEnumerable itemList && !(dataType == typeof(string) || data.Value is IEnumerable<char> || data.Value is IEnumerable<byte>)) sendData = this.ConvertMultiWordsByteArrayFromDataList(itemList);
                        else sendData = this.ConvertMultiWordsByteArrayFromData(data.Value);
                        devCount = (ushort)(sendData.Length / 2);
                        binData.AddRange(BitConverter.GetBytes(devCount));
                        binData.AddRange(sendData);

                        msgDataLength = (ushort)binData.Count;
                    }
                    binHeader.AddRange(BitConverter.GetBytes(msgDataLength));
                    binHeader.AddRange(binData);
                    sendMsg = binHeader.ToArray();

                    binHeader.Clear();
                    binHeader = null;
                    binData.Clear();
                    binData = null;

                    break;
            }

            lock (m_CommunicateLock)
            {
                m_Stream.Write(sendMsg, 0, sendMsg.Length);

                this.ReceiveMsg();
            }
        }
        private void SendMsg(MCPSendData data, ref MCPReceiveData receiveData)
        {
            byte[] sendMsg = null;
            byte[] readVal = null;
            ushort msgDataLength = 0;

            switch (ProtocolFormat)
            {
                case EMCProtocolFormat.ASCII:
                    string strHeader = "5000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";
                    string strData = m_Timeout.ToString("X4") + "0401" + "0000" + this.ConvertStringFromAddress(data) + data.WordCount.ToString("X4");
                    msgDataLength = (ushort)strData.Length;
                    sendMsg = Encoding.ASCII.GetBytes(strHeader + msgDataLength.ToString("X4") + strData);
                    break;

                case EMCProtocolFormat.Binary:
                    List<byte> binHeader = new List<byte>() { 0x50, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };
                    List<byte> binData = new List<byte>(BitConverter.GetBytes(m_Timeout)) { 0x01, 0x04, 0x00, 0x00 };
                    binData.AddRange(this.ConvertByteArrayFromAddress(data));
                    binData.AddRange(BitConverter.GetBytes(data.WordCount));

                    msgDataLength = (ushort)binData.Count;

                    binHeader.AddRange(BitConverter.GetBytes(msgDataLength));
                    binHeader.AddRange(binData);
                    sendMsg = binHeader.ToArray();

                    binHeader.Clear();
                    binHeader = null;
                    binData.Clear();
                    binData = null;

                    break;
            }

            lock (m_CommunicateLock)
            {
                m_Stream.Write(sendMsg, 0, sendMsg.Length);

                var tmpArr = this.ReceiveMsg(data.WordCount);

                switch (ProtocolFormat)
                {
                    case EMCProtocolFormat.Binary:
                        readVal = tmpArr;
                        break;
                    case EMCProtocolFormat.ASCII:
                        readVal = new byte[tmpArr.Length];
                        for (int i = 0; i < tmpArr.Length; i += 2)
                        {
                            if (i + 1 < tmpArr.Length)
                            {
                                readVal[i + 1] = tmpArr[i];
                                readVal[i] = tmpArr[i + 1];
                            }
                            else if (i + 1 == tmpArr.Length)
                            {
                                readVal[i] = tmpArr[i];
                            }
                        }
                        break;
                }
                receiveData = new MCPReceiveData(readVal, data.DeviceCode, data.Address);
            }
        }
        private void SendMsg(IEnumerable<MCPSendData> dataList)
        {
            byte[] sendMsg = null;
            ushort msgDataLength = 0;

            string strHeader = string.Empty;
            string strData = string.Empty;
            List<byte> binHeader = null;
            List<byte> binData = null;

            var boolDataList = dataList.Where(item => item.Value is IEnumerable<bool> || item.Value is bool);
            var objDataList = dataList.Where(item => !(item.Value is IEnumerable<bool> || item.Value is bool));

            if (boolDataList.Count() > 0)
            {
                byte bitCount = 0;
                switch (ProtocolFormat)
                {
                    case EMCProtocolFormat.ASCII:
                        strHeader = "5000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";
                        strData = m_Timeout.ToString("X4") + "1402" + "0001";
                        string strAddress = string.Empty;

                        foreach (var boolData in boolDataList)
                        {
                            if (boolData.Value is IEnumerable<bool> bListVal)
                            {
                                int len = bListVal.Count();
                                for (int i = 0; i < len; i++)
                                {
                                    strAddress += this.ConvertStringFromAddress(boolData, i) + (bListVal.ElementAt(i) ? "01" : "00");
                                    bitCount++;
                                }
                            }
                            else if (boolData.Value is bool bVal)
                            {
                                strAddress += this.ConvertStringFromAddress(boolData) + (bVal ? "01" : "00");
                                bitCount++;
                            }
                        }

                        strData += bitCount.ToString("X2") + strAddress;
                        msgDataLength = (ushort)strData.Length;

                        sendMsg = Encoding.ASCII.GetBytes(strHeader + msgDataLength.ToString("X4") + strData);

                        break;
                    case EMCProtocolFormat.Binary:
                        binHeader = new List<byte>() { 0x50, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };
                        binData = new List<byte>(BitConverter.GetBytes(m_Timeout)) { 0x02, 0x14, 0x01, 0x00 };
                        List<byte> binAddress = new List<byte>();
                        foreach (var boolData in boolDataList)
                        {
                            if (boolData.Value is IEnumerable<bool> bListVal)
                            {
                                int len = bListVal.Count();
                                for (int i = 0; i < len; i++)
                                {
                                    binAddress.AddRange(this.ConvertByteArrayFromAddress(boolData, i));
                                    binAddress.Add((byte)boolData.DeviceCode);
                                    binAddress.Add(bListVal.ElementAt(i) ? (byte)0x01 : (byte)0x00);
                                    bitCount++;
                                }
                            }
                            else if (boolData.Value is bool bVal)
                            {
                                binAddress.AddRange(this.ConvertByteArrayFromAddress(boolData));
                                binAddress.Add((byte)boolData.DeviceCode);
                                binAddress.Add(bVal ? (byte)0x01 : (byte)0x00);
                                bitCount++;
                            }
                        }

                        binData.AddRange(binAddress);

                        msgDataLength = binData.Count % 2 == 0 ? (ushort)(binData.Count / 2) : (ushort)(binData.Count / 2 + 1);
                        binHeader.AddRange(BitConverter.GetBytes(msgDataLength));
                        binHeader.AddRange(binData);

                        sendMsg = binHeader.ToArray();

                        binHeader.Clear();
                        binData.Clear();
                        binAddress.Clear();
                        binHeader = null;
                        binData = null;
                        binAddress = null;

                        break;
                }

                lock (m_CommunicateLock)
                {
                    m_Stream.Write(sendMsg, 0, sendMsg.Length);

                    this.ReceiveMsg();
                }
            }

            if (objDataList.Count() > 0)
            {
                byte wordCount = 0;
                byte dwordCount = 0;

                switch (ProtocolFormat)
                {
                    case EMCProtocolFormat.ASCII:
                        strHeader = "5000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";
                        strData = m_Timeout.ToString("X4") + "1402" + "0000";
                        string strWordAddress = string.Empty;
                        string strDWordAddress = string.Empty;

                        foreach (var data in objDataList)
                        {
                            string tmpData = string.Empty;

                            if (data.Value is IEnumerable vals)
                            {
                                int tmpCount = 0;
                                if (vals is IEnumerable<char> || vals is string)
                                {
                                    tmpData = this.ConvertMultiWordsStringFromData(vals);
                                    while (tmpData.Length >= 8)
                                    {
                                        strDWordAddress += this.ConvertStringFromAddress(data, tmpCount * 2) + tmpData.Substring(4, 4) + tmpData.Substring(0, 4);
                                        tmpData = tmpData.Substring(8);
                                        dwordCount++;
                                        tmpCount++;
                                    }
                                    if (tmpData.Length > 0)
                                    {
                                        strWordAddress += this.ConvertStringFromAddress(data, tmpCount * 2) + tmpData;
                                        wordCount++;
                                    }
                                }
                                else
                                {
                                    foreach (var val in vals)
                                    {
                                        if (val.GetType() == typeof(long) || val.GetType() == typeof(ulong) || val.GetType() == typeof(double))
                                        {
                                            var tmpArr = this.Convert2WordsStringFrom4WordsData(val);
                                            strDWordAddress += this.ConvertStringFromAddress(data, tmpCount);
                                            strDWordAddress += tmpArr[0];
                                            tmpCount += 2;
                                            strDWordAddress += this.ConvertStringFromAddress(data, tmpCount);
                                            strDWordAddress += tmpArr[1];
                                            tmpCount += 2;
                                            dwordCount += 2;
                                            continue;
                                        }
                                        try
                                        {
                                            tmpData = this.Convert2WordsStringFromData(val);
                                            strDWordAddress += this.ConvertStringFromAddress(data, tmpCount) + tmpData;
                                            tmpCount += 2;
                                            dwordCount++;
                                        }
                                        catch
                                        {
                                            tmpData = this.Convert1WordStringFromData(val);
                                            strWordAddress += this.ConvertStringFromAddress(data, tmpCount) + tmpData;
                                            tmpCount++;
                                            wordCount++;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (data.Value.GetType() == typeof(long) || data.Value.GetType() == typeof(ulong) || data.Value.GetType() == typeof(double))
                                {
                                    var tmpArr = this.Convert2WordsStringFrom4WordsData(data.Value);
                                    strDWordAddress += this.ConvertStringFromAddress(data) + tmpArr[0];
                                    strDWordAddress += this.ConvertStringFromAddress(data, 2);
                                    strDWordAddress += tmpArr[1];
                                    dwordCount += 2;
                                    continue;
                                }
                                try
                                {
                                    tmpData = this.Convert2WordsStringFromData(data.Value);
                                    strDWordAddress += this.ConvertStringFromAddress(data) + tmpData;
                                    dwordCount++;
                                }
                                catch
                                {
                                    tmpData = this.Convert1WordStringFromData(data.Value);
                                    strWordAddress += this.ConvertStringFromAddress(data) + tmpData;
                                    wordCount++;
                                }
                            }
                        }

                        strData += wordCount.ToString("X2") + dwordCount.ToString("X2") + strWordAddress + strDWordAddress;
                        msgDataLength = (ushort)strData.Length;

                        sendMsg = Encoding.ASCII.GetBytes(strHeader + msgDataLength.ToString("X4") + strData);

                        break;

                    case EMCProtocolFormat.Binary:
                        binHeader = new List<byte>() { 0x50, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };
                        binData = new List<byte>(BitConverter.GetBytes(m_Timeout)) { 0x02, 0x14, 0x00, 0x00 };
                        List<byte> binDWordAddress = new List<byte>();
                        List<byte> binWordAddress = new List<byte>();

                        foreach (var data in objDataList)
                        {
                            IEnumerable<byte> tmpData = null;

                            if (data.Value is IEnumerable vals)
                            {
                                int tmpCount = 0;
                                if (vals is IEnumerable<char> || vals is string)
                                {
                                    tmpData = this.ConvertMultiWordsByteArrayFromData(vals);
                                    while (tmpData.Count() >= 4)
                                    {
                                        binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount * 2));
                                        binDWordAddress.AddRange(tmpData.Take(4));
                                        tmpData = tmpData.Skip(4);
                                        dwordCount++;
                                        tmpCount++;
                                    }
                                    if (tmpData.Count() > 0)
                                    {
                                        binWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount * 2));
                                        binWordAddress.AddRange(tmpData);
                                        wordCount++;
                                    }
                                }
                                else
                                {
                                    foreach (var val in vals)
                                    {
                                        if (val.GetType() == typeof(long) || val.GetType() == typeof(ulong) || val.GetType() == typeof(double))
                                        {
                                            var tmpArr = this.Convert2WordsByteArrayFrom4WordsData(val);
                                            binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount));
                                            binDWordAddress.AddRange(tmpArr[0]);
                                            tmpCount += 2;
                                            binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount));
                                            binDWordAddress.AddRange(tmpArr[1]);
                                            tmpCount += 2;
                                            dwordCount += 2;
                                            continue;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                tmpData = this.Convert2WordsByteArrayFromData(val);
                                                binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount));
                                                binDWordAddress.AddRange(tmpData);
                                                tmpCount += 2;
                                                dwordCount++;
                                            }
                                            catch
                                            {
                                                tmpData = this.Convert1WordByteArrayFromData(val);
                                                binWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, tmpCount));
                                                binWordAddress.AddRange(tmpData);
                                                tmpCount++;
                                                wordCount++;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (data.Value.GetType() == typeof(long) || data.Value.GetType() == typeof(ulong) || data.Value.GetType() == typeof(double))
                                {
                                    var tmpArr = this.Convert2WordsByteArrayFrom4WordsData(data.Value);
                                    binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data));
                                    binDWordAddress.AddRange(tmpArr[0]);
                                    binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, 2));
                                    binDWordAddress.AddRange(tmpArr[1]);
                                    dwordCount += 2;
                                    continue;
                                }
                                try
                                {
                                    tmpData = this.Convert2WordsByteArrayFromData(data.Value);
                                    binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data));
                                    binDWordAddress.AddRange(tmpData);
                                    dwordCount++;
                                }
                                catch
                                {
                                    tmpData = this.Convert1WordByteArrayFromData(data.Value);
                                    binWordAddress.AddRange(this.ConvertByteArrayFromAddress(data));
                                    binWordAddress.AddRange(tmpData);
                                    wordCount++;
                                }
                            }
                        }
                        binData.Add(wordCount);
                        binData.Add(dwordCount);
                        binData.AddRange(binWordAddress);
                        binData.AddRange(binDWordAddress);

                        msgDataLength = (ushort)binData.Count;


                        binHeader.AddRange(BitConverter.GetBytes(msgDataLength));
                        binHeader.AddRange(binData);

                        sendMsg = binHeader.ToArray();

                        binHeader.Clear();
                        binData.Clear();
                        binDWordAddress.Clear();
                        binWordAddress.Clear();
                        binHeader = null;
                        binData = null;
                        binDWordAddress = null;
                        binWordAddress = null;

                        break;
                }

                lock (m_CommunicateLock)
                {
                    m_Stream.Write(sendMsg, 0, sendMsg.Length);

                    this.ReceiveMsg();
                }
            }
        }
        private void SendMsg(IEnumerable<MCPSendData> dataList, ref List<MCPReceiveData> readValArr)
        {
            byte[] sendMsg = null;
            ushort msgDataLength = 0;

            string strHeader = string.Empty;
            string strData = string.Empty;
            List<byte> binHeader = null;
            List<byte> binData = null;
            byte wordCount = 0;
            byte dwordCount = 0;

            switch (ProtocolFormat)
            {
                case EMCProtocolFormat.ASCII:
                    strHeader = "5000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";
                    strData = m_Timeout.ToString("X4") + "0403" + "0000";
                    string strWordAddress = string.Empty;
                    string strDWordAddress = string.Empty;

                    foreach (var data in dataList)
                    {
                        var tmpDwordCount = data.WordCount / 2 + dwordCount > byte.MaxValue ? throw new Exception("PLC_MESSAGE_SIZE_OVERFLOW_ERR") : (byte)(data.WordCount / 2);
                        var tmpWordCount = data.WordCount % 2 + wordCount > byte.MaxValue ? throw new Exception("PLC_MESSAGE_SIZE_OVERFLOW_ERR") : (byte)(data.WordCount % 2);

                        for (int i = 0; i < tmpDwordCount; i++)
                        {
                            strDWordAddress += this.ConvertStringFromAddress(data, 2 * i);
                        }
                        if (tmpWordCount > 0)
                        {
                            strWordAddress += this.ConvertStringFromAddress(data, 2 * tmpDwordCount);
                        }
                        dwordCount += tmpDwordCount;
                        wordCount += tmpWordCount;
                    }

                    strData += wordCount.ToString("X2") + dwordCount.ToString("X2") + strWordAddress + strDWordAddress;
                    msgDataLength = (ushort)strData.Length;

                    sendMsg = Encoding.ASCII.GetBytes(strHeader + msgDataLength.ToString("X4") + strData);

                    break;

                case EMCProtocolFormat.Binary:
                    binHeader = new List<byte>() { 0x50, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };
                    binData = new List<byte>(BitConverter.GetBytes(m_Timeout)) { 0x03, 0x04, 0x00, 0x00 };
                    List<byte> binDWordAddress = new List<byte>();
                    List<byte> binWordAddress = new List<byte>();

                    foreach (var data in dataList)
                    {
                        var tmpDwordCount = data.WordCount / 2 + dwordCount > byte.MaxValue ? throw new Exception("PLC_MESSAGE_SIZE_OVERFLOW_ERR") : (byte)(data.WordCount / 2);
                        var tmpWordCount = data.WordCount % 2 + wordCount > byte.MaxValue ? throw new Exception("PLC_MESSAGE_SIZE_OVERFLOW_ERR") : (byte)(data.WordCount % 2);

                        for (int i = 0; i < tmpDwordCount; i++)
                        {
                            binDWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, 2 * i));
                        }
                        if (tmpWordCount > 0)
                        {
                            binWordAddress.AddRange(this.ConvertByteArrayFromAddress(data, 2 * tmpDwordCount));
                        }
                        dwordCount += tmpDwordCount;
                        wordCount += tmpWordCount;
                    }

                    binData.Add(wordCount);
                    binData.Add(dwordCount);
                    binData.AddRange(binWordAddress);
                    binData.AddRange(binDWordAddress);

                    msgDataLength = (ushort)binData.Count;

                    binHeader.AddRange(BitConverter.GetBytes(msgDataLength));
                    binHeader.AddRange(binData);
                    sendMsg = binHeader.ToArray();

                    binHeader.Clear();
                    binData.Clear();
                    binDWordAddress.Clear();
                    binWordAddress.Clear();
                    binHeader = null;
                    binData = null;
                    binDWordAddress = null;
                    binWordAddress = null;
                    break;
            }

            lock (m_CommunicateLock)
            {
                m_Stream.Write(sendMsg, 0, sendMsg.Length);

                var tmpArr = this.ReceiveMsg((ushort)(dwordCount * 2 + wordCount));

                var wordArr = tmpArr.Take(wordCount * 2);
                var dwordArr = tmpArr.Skip(wordCount * 2);

                List<MCPReceiveData> tmpResultList = new List<MCPReceiveData>();

                foreach (var data in dataList)
                {
                    List<byte> tmpList = new List<byte>();

                    var tmpDwordCount = data.WordCount / 2;
                    var tmpWordCount = data.WordCount % 2;

                    switch (ProtocolFormat)
                    {
                        case EMCProtocolFormat.Binary:
                            if (tmpDwordCount > 0)
                            {
                                tmpList.AddRange(dwordArr.Take(tmpDwordCount * 4));
                                dwordArr = dwordArr.Skip(tmpDwordCount * 4);
                            }
                            if (tmpWordCount > 0)
                            {
                                tmpList.AddRange(wordArr.Take(tmpWordCount * 2));
                                wordArr = wordArr.Skip(tmpWordCount * 2);
                            }
                            if (tmpList.Count > 0)
                            {
                                tmpResultList.Add(new MCPReceiveData(tmpList.ToArray(), data.DeviceCode, data.Address));
                                tmpList.Clear();
                            }
                            tmpList = null;
                            break;
                        case EMCProtocolFormat.ASCII:
                            if (tmpDwordCount > 0)
                            {
                                for (int i = 0; i < tmpDwordCount; i++)
                                {
                                    var tmpDwordArrForAscii = dwordArr.Skip(i * 4).Take(4);
                                    tmpList.AddRange(tmpDwordArrForAscii.Reverse());
                                }
                                dwordArr = dwordArr.Skip(tmpDwordCount * 4);
                            }
                            if (tmpWordCount > 0)
                            {
                                for (int i = 0; i < tmpWordCount; i++)
                                {
                                    var tmpWordArrForAscii = wordArr.Skip(i * 2).Take(2);
                                    tmpList.AddRange(tmpWordArrForAscii.Reverse());
                                }
                                wordArr = wordArr.Skip(tmpWordCount * 2);
                            }
                            if (tmpList.Count > 0)
                            {
                                tmpResultList.Add(new MCPReceiveData(tmpList.ToArray(), data.DeviceCode, data.Address));
                                tmpList.Clear();
                            }
                            tmpList = null;
                            break;

                    }
                }
                readValArr = tmpResultList;
            }
        }

        private byte[] ReceiveMsg(ushort wordCount = 0)
        {
            if (!IsConnected) throw new Exception("PLC_NOT_OPEN_ERR");
            byte[] tmpBuffer = new byte[256];
            string receiveData = string.Empty;
            string tmpStr = string.Empty;
            while (IsConnected)
            {
                lock (m_QueueLock)
                {
                    if (m_ReceivedMessageQueue.Count > 0)
                    {
                        tmpBuffer = m_ReceivedMessageQueue.Dequeue();
                        break;
                    }
                    Thread.Sleep(10);
                }
            }

            switch (ProtocolFormat)
            {
                case EMCProtocolFormat.ASCII:
                    receiveData = Encoding.ASCII.GetString(tmpBuffer);
                    tmpStr = "D000" + NetworkNo.ToString("X2") + PCNo.ToString("X2") + "03FF" + "00";

                    if (!receiveData.Contains(tmpStr)) throw new Exception("Receive wrong data." + Environment.NewLine + "Message : " + receiveData);
                    int strCount = int.Parse(receiveData.Substring(tmpStr.Length, 4), System.Globalization.NumberStyles.HexNumber);
                    if (strCount != receiveData.Length - tmpStr.Length - 4 || strCount != wordCount * 4 + 4) throw new Exception("Wrong data count." + Environment.NewLine + "Message : " + receiveData);

                    string errCode = receiveData.Substring(tmpStr.Length + 4, 4);
                    if (errCode != "0000") throw new Exception("Receive error occur." + Environment.NewLine + "Message : " + receiveData.Substring(tmpStr.Length + 8));
                    receiveData = receiveData.Substring(tmpStr.Length + 8);

                    return this.ConvertHexStringToByteArray(receiveData);

                case EMCProtocolFormat.Binary:
                    var tmpByteArray = new byte[] { 0xD0, 0x00, NetworkNo, PCNo, 0xFF, 0x03, 0x00 };

                    for (int i = 0; i < tmpByteArray.Length; i++) if (tmpByteArray[i] != tmpBuffer[i]) throw new Exception("Receive wrong data.");
                    ushort byteCount = BitConverter.ToUInt16(tmpBuffer.Skip(tmpByteArray.Length).Take(2).ToArray(), 0);
                    if (byteCount != tmpBuffer.Length - tmpByteArray.Length - 2 || byteCount != wordCount * 2 + 2) throw new Exception("Wrong data count.");
                    int errInt = BitConverter.ToUInt16(tmpBuffer.Skip(tmpByteArray.Length + 2).Take(2).ToArray(), 0);
                    if (errInt != 0) throw new Exception("Receive error occur." + Environment.NewLine + "Message : " + this.ConvertValueToString(tmpBuffer.Take(tmpByteArray.Length + 4).ToArray()));

                    return tmpBuffer.Skip(tmpByteArray.Length + 4).ToArray();
            }
            //can't arrive here
            throw new Exception();
        }
        #endregion

        #region Convert Data Methods

        #region Bit Units
        private string Convert1BitStringFromBooleanData(bool boolData)
        {
            return boolData ? "1" : "0";
        }
        private byte Convert1BitByteFromBooleanData(bool boolData)
        {
            return (byte)(boolData ? 0x10 : 0x00);
        }
        private string ConvertNBitStringFromBooleanArrayData(IEnumerable<bool> boolArr)
        {
            string tmpStr = string.Empty;
            foreach (var b in boolArr)
            {
                tmpStr += b ? "1" : "0";
            }
            return tmpStr;
        }
        private byte[] ConvertNBitByteArrayFromBooleanArrayData(IEnumerable<bool> boolArr)
        {
            int count = boolArr.Count();
            byte[] tmpByteArr = count % 2 == 0 ? new byte[count / 2] : new byte[count / 2 + 1];
            for (int i = 0; i < count; i += 2)
            {
                tmpByteArr[i / 2] = (byte)((boolArr.ElementAt(i) ? 0x10 : 0x00) + (i + 1 < count && boolArr.ElementAt(i + 1) ? 0x01 : 0x00));
            }
            return tmpByteArr;
        }
        #endregion

        #region Byte Units
        private byte Convert1ByteFromData(object val)
        {
            Type type = val.GetType();

            if (type == typeof(byte))
            {
                return (byte)val;
            }
            else if (type == typeof(char))
            {
                char charData = (char)val;
                return (byte)charData;
            }
            else
            {
                throw new Exception("PLC_INVALID_PLC_DATA_FORMAT_ERR");
            }
        }
        private string Convert1ByteStringFromData(object val)
        {
            return (this.Convert1ByteFromData(val)).ToString("X2");
        }
        #endregion

        #region Word Units
        private byte[] Convert1WordByteArrayFromData(object val)
        {
            Type type = val.GetType();

            if (type == typeof(byte))
            {
                byte byteData = (byte)val;
                return new byte[] { byteData, 0x00 };
            }
            else if (type == typeof(char))
            {
                char charData = (char)val;
                byte byteData = (byte)charData;
                return new byte[] { byteData, 0x00 };
            }
            else if (type == typeof(short))
            {
                short shortData = (short)val;
                return BitConverter.GetBytes(shortData);
            }
            else if (type == typeof(ushort))
            {
                ushort shortData = (ushort)val;
                return BitConverter.GetBytes(shortData);
            }
            else if (type == typeof(string) && (val as string).Length <= 2)
            {
                char[] charData = ((string)val).ToArray();
                if (charData.Length % 2 == 0) return Encoding.ASCII.GetBytes(charData);
                else
                {
                    byte[] tmpArr = new byte[charData.Length + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData);
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else if (val is IEnumerable<char> charArr && charArr.Count() <= 2)
            {
                char[] charData = charArr.ToArray();
                if (charData.Length % 2 == 0) return Encoding.ASCII.GetBytes(charData);
                else
                {
                    byte[] tmpArr = new byte[charData.Length + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData);
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else
            {
                throw new Exception("PLC_INVALID_PLC_DATA_FORMAT_ERR");
            }
        }
        private string Convert1WordStringFromData(object val)
        {
            return this.ConvertValueToString(this.Convert1WordByteArrayFromData(val));
        }

        #endregion

        #region Double Word Units
        private byte[] Convert2WordsByteArrayFromData(object val)
        {
            Type type = val.GetType();

            if (type == typeof(int))
            {
                int intData = (int)val;
                return BitConverter.GetBytes(intData);
            }
            else if (type == typeof(uint))
            {
                uint intData = (uint)val;
                return BitConverter.GetBytes(intData);
            }
            else if (type == typeof(float))
            {
                float.TryParse(val.ToString(), out float floatData);
                return BitConverter.GetBytes(floatData);
            }
            else if (type == typeof(string) && (val as string).Length > 2 && (val as string).Length <= 4)
            {
                char[] charData = ((string)val).ToArray();
                if (charData.Length % 2 == 0) return Encoding.ASCII.GetBytes(charData);
                else
                {
                    byte[] tmpArr = new byte[charData.Length + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData);
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else if (val is IEnumerable<char> charArr && charArr.Count() > 2 && charArr.Count() <= 4)
            {
                char[] charData = charArr.ToArray();
                if (charData.Length % 2 == 0) return Encoding.ASCII.GetBytes(charData);
                else
                {
                    byte[] tmpArr = new byte[charData.Length + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData);
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else
            {
                throw new Exception("PLC_INVALID_PLC_DATA_FORMAT_ERR");
            }
        }
        private string Convert2WordsStringFromData(object val)
        {
            return this.ConvertValueToString(this.Convert2WordsByteArrayFromData(val), true);
        }

        private byte[][] Convert2WordsByteArrayFrom4WordsData(object val)
        {
            Type type = val.GetType();
            byte[] tmpByteArray = null;
            if (type == typeof(long))
            {
                long longData = (long)val;
                tmpByteArray = BitConverter.GetBytes(longData);
            }
            else if (type == typeof(ulong))
            {
                ulong longData = (ulong)val;
                tmpByteArray = BitConverter.GetBytes(longData);
            }
            else if (type == typeof(double))
            {
                double doubleData = (double)val;
                tmpByteArray = BitConverter.GetBytes(doubleData);
            }
            else
            {
                throw new Exception("PLC_INVALID_PLC_DATA_FORMAT_ERR");
            }
            return new byte[][] { tmpByteArray.Take(4).ToArray(), tmpByteArray.Skip(4).Take(4).ToArray() };
        }

        private string[] Convert2WordsStringFrom4WordsData(object val)
        {
            var tmpArr = this.Convert2WordsByteArrayFrom4WordsData(val);
            return new string[] { this.ConvertValueToString(tmpArr[0], true), this.ConvertValueToString(tmpArr[1], true) };
        }
        #endregion

        #region Multiple Units
        private byte[] ConvertMultiWordsByteArrayFromData(object val)
        {
            Type type = val.GetType();

            if (type == typeof(byte))
            {
                byte byteData = (byte)val;
                return new byte[] { byteData, 0x00 };
            }
            else if (type == typeof(char))
            {
                char charData = (char)val;
                byte byteData = (byte)charData;
                return new byte[] { byteData, 0x00 };
            }
            else if (type == typeof(short))
            {
                short shortData = (short)val;
                return BitConverter.GetBytes(shortData);
            }
            else if (type == typeof(ushort))
            {
                ushort shortData = (ushort)val;
                return BitConverter.GetBytes(shortData);
            }
            else if (type == typeof(int))
            {
                int intData = (int)val;
                return BitConverter.GetBytes(intData);
            }
            else if (type == typeof(uint))
            {
                uint intData = (uint)val;
                return BitConverter.GetBytes(intData);
            }
            else if (type == typeof(long))
            {
                long longData = (long)val;
                return BitConverter.GetBytes(longData);
            }
            else if (type == typeof(ulong))
            {
                ulong longData = (ulong)val;
                return BitConverter.GetBytes(longData);
            }
            else if (type == typeof(float))
            {
                float floatData = (float)val;
                return BitConverter.GetBytes(floatData);
            }
            else if (type == typeof(double))
            {
                double doubleData = (double)val;
                return BitConverter.GetBytes(doubleData);
            }
            else if (type == typeof(string))
            {
                char[] charData = ((string)val).ToArray();
                if (charData.Length % 2 == 0) return Encoding.ASCII.GetBytes(charData);
                else
                {
                    byte[] tmpArr = new byte[charData.Length + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData);
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else if (val is IEnumerable<char> charData)
            {
                if (charData.Count() % 2 == 0) return Encoding.ASCII.GetBytes(charData.ToArray());
                else
                {
                    byte[] tmpArr = new byte[charData.Count() + 1];
                    var asciiData = Encoding.ASCII.GetBytes(charData.ToArray());
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else if (val is IEnumerable<byte> byteArrData)
            {
                if (byteArrData.Count() % 2 == 0) return byteArrData.ToArray();
                else
                {
                    byte[] tmpArr = new byte[byteArrData.Count() + 1];
                    var asciiData = byteArrData.ToArray();
                    for (int i = 0; i < asciiData.Length; i++) tmpArr[i] = asciiData[i];
                    return tmpArr;
                }
            }
            else
            {
                throw new Exception("PLC_INVALID_PLC_DATA_FORMAT_ERR");
            }
        }
        private byte[] ConvertMultiWordsByteArrayFromDataList(IEnumerable vals)
        {
            List<byte> tmpByteList = new List<byte>();
            foreach (var val in vals)
            {
                tmpByteList.AddRange(this.ConvertMultiWordsByteArrayFromData(val));
            }
            return tmpByteList.ToArray();
        }

        private string ConvertMultiWordsStringFromData(object val)
        {
            return this.ConvertValueToString(this.ConvertMultiWordsByteArrayFromData(val));
        }
        private string ConvertMultiWordsStringFromDataList(IEnumerable vals)
        {
            var tmpStr = string.Empty;
            foreach (var val in vals)
            {
                tmpStr += this.ConvertValueToString(this.ConvertMultiWordsByteArrayFromData(val));
            }
            return tmpStr;
        }
        #endregion

        private string ConvertValueToString(byte[] byteArray, bool isDWord = false)
        {
            string tmpStr = string.Empty;
            if (isDWord)
            {
                for (int i = 0; i < byteArray.Length; i += 4)
                {
                    tmpStr += (i + 3 < byteArray.Length ? byteArray[i + 3].ToString("X2") : "00")
                        + (i + 2 < byteArray.Length ? byteArray[i + 2].ToString("X2") : "00")
                        + (i + 1 < byteArray.Length ? byteArray[i + 1].ToString("X2") : "00")
                        + byteArray[i].ToString("X2");
                }
            }
            else
            {
                for (int i = 0; i < byteArray.Length; i += 2)
                {
                    tmpStr += (i + 1 < byteArray.Length ? byteArray[i + 1].ToString("X2") : "00") + byteArray[i].ToString("X2");
                }
            }
            return tmpStr;
        }

        private byte[] ConvertHexStringToByteArray(string hexString)
        {
            return Enumerable.Range(0, hexString.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hexString.Substring(x, 2), 16)).ToArray();
        }
        #endregion

        #region Convert Address Methods
        private string ConvertStringFromAddress(MCPSendData data)
        {
            var strCode = data.DeviceCode.ToString();
            string strAddr = string.Empty;
            if ((int)data.DeviceCode <= 0xA3 && (int)data.DeviceCode >= 0x9C) strAddr = data.Address.ToString("X6");
            else strAddr = data.Address.ToString("D6");

            return (strCode.Length == 1 ? strCode + "*" : strCode) + (strAddr.Length > 6 ? strAddr.Substring(strAddr.Length - 6, 6) : strAddr);
        }
        private string ConvertStringFromAddress(MCPSendData data, int offset)
        {
            var strCode = data.DeviceCode.ToString();
            string strAddr = string.Empty;
            if ((int)data.DeviceCode <= 0xA3 && (int)data.DeviceCode >= 0x9C) strAddr = (data.Address + offset).ToString("X6");
            else strAddr = (data.Address + offset).ToString("D6");

            return (strCode.Length == 1 ? strCode + "*" : strCode) + (strAddr.Length > 6 ? strAddr.Substring(strAddr.Length - 6, 6) : strAddr);
        }

        private byte[] ConvertByteArrayFromAddress(MCPSendData data)
        {
            byte[] tmpAddr = BitConverter.GetBytes(data.Address);
            tmpAddr[tmpAddr.Length - 1] = (byte)data.DeviceCode;
            return tmpAddr;
        }
        private byte[] ConvertByteArrayFromAddress(MCPSendData data, int offset)
        {
            byte[] tmpAddr = BitConverter.GetBytes(data.Address + offset);
            tmpAddr[tmpAddr.Length - 1] = (byte)data.DeviceCode;
            return tmpAddr;
        }
        #endregion
        
        #endregion

    }
}
