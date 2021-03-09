using PLCProtocol.Mitsubishi;
using PLCProtocol_TestApp.Xml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PLCProtocol_TestApp
{
    public class MainWindow_ViewModel : ViewModelBase
    {
        #region Fields
        private XmlParser m_XmlParser;
        private MCProtocol m_PLC;

        private Thread m_ConnectionCheckThread;

        private char m_SeparateChar;
        private int m_SelectedSeparateCharIndex;
        #endregion

        #region Properties
        //Connection Properties
        public string IP
        {
            get
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data) return data.IP;
                else return string.Empty;
            }
            set
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data)
                {
                    data.IP = value;
                }
            }
        }
        public ushort Port
        {
            get
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data) return data.PortNumber;
                else return 0;
            }
            set
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data)
                {
                    data.PortNumber = value;
                }
            }
        }
        public bool IsBinary
        {
            get
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data) return data.ProtocolFormat == EMCProtocolFormat.Binary;
                else return true;
            }
            set
            {
                if (m_PLC != null && m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data)
                {
                    if (value)
                    {
                        m_PLC.ProtocolFormat = EMCProtocolFormat.Binary;
                        data.ProtocolFormat = EMCProtocolFormat.Binary;
                    }
                    else
                    {
                        m_PLC.ProtocolFormat = EMCProtocolFormat.ASCII;
                        data.ProtocolFormat = EMCProtocolFormat.ASCII;

                    }
                }
            }
        }
        public byte NetworkNo
        {
            get
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data) return data.NetworkNo;
                else return 0;
            }
            set
            {
                if (m_PLC != null && m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data)
                {
                    m_PLC.NetworkNo = value;
                    data.NetworkNo = value;
                }
            }
        }
        public byte PCNo
        {
            get
            {
                if (m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data) return data.PCNo;
                else return 255;
            }
            set
            {
                if (m_PLC != null && m_XmlParser != null && m_XmlParser.ParsedData is MitsubishiPLCData data)
                {
                    m_PLC.PCNo = value;
                    data.PCNo = value;
                }
            }
        }
        public bool IsConnected
        {
            get
            {
                if (m_PLC != null) return m_PLC.IsConnected;
                else return false;
            }
        }
        public int SelectedSeparateCharIndex
        {
            get
            {
                return m_SelectedSeparateCharIndex;
            }
            set
            {
                if (m_SelectedSeparateCharIndex != value)
                {
                    m_SelectedSeparateCharIndex = value;
                    switch (value)
                    {
                        case 0:
                            m_SeparateChar = ' ';
                            break;
                        case 1:
                            m_SeparateChar = '-';
                            break;
                        case 2:
                            m_SeparateChar = ',';
                            break;
                        case 3:
                            m_SeparateChar = '_';
                            break;
                    }
                    foreach (var res in ResultList)
                    {
                        res.SeparateChar = m_SeparateChar;
                    }
                }
            }
        }

        //Collections
        public ObservableCollection<SendCommand> WriteCommandList { get; set; }
        public ObservableCollection<SendCommand> ReadCommandList { get; set; }
        public ObservableCollection<ResultData> ResultList { get; set; }
        #endregion

        public MainWindow_ViewModel()
        {
            m_XmlParser = new XmlParser(EXmlType.MitsubishiPLCData);
            m_XmlParser.LoadXml();
            
            m_PLC = new MCProtocol(IP, Port, IsBinary ? EMCProtocolFormat.Binary : EMCProtocolFormat.ASCII, NetworkNo, PCNo);
            WriteCommandList = new ObservableCollection<SendCommand>();
            ReadCommandList = new ObservableCollection<SendCommand>();
            ResultList = new ObservableCollection<ResultData>();
            m_SeparateChar = ' ';
        }

        #region Methods
        internal void OnLoad()
        {
            m_ConnectionCheckThread = new Thread(new ThreadStart(() =>
            {
                while (true)
                {
                    this.RaisePropertyChanged(nameof(IsConnected));
                    Thread.Sleep(100);
                }
            }));
            m_ConnectionCheckThread.Start();
        }
        internal void Connect_button_Click()
        {
            try
            {
                if (IsConnected)
                {
                    m_PLC.Disconnect();
                }
                else
                {
                    m_PLC.Connect(IP, Port);
                }
            }
            catch(Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }

        internal void Write_button_Click()
        {
            try
            {
                if (WriteCommandList.Count == 1)
                {
                    var data = this.ConvertPLCData(WriteCommandList, 0);
                    m_PLC.Write(data);
                }
                else if (WriteCommandList.Count > 0)
                {
                    List<MCPSendData> dataList = new List<MCPSendData>();
                    for (int i = 0; i < WriteCommandList.Count; i++)
                    {
                        dataList.Add(this.ConvertPLCData(WriteCommandList, i));
                    }
                    m_PLC.Write(dataList);
                }

                MessageBox.Show("Write Successfully.");
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }
        internal void Read_button_Click()
        {
            try
            {
                if (ReadCommandList.Count == 1)
                {
                    ResultList.Clear();
                    var data = this.ConvertPLCData(ReadCommandList, 0, true);
                    MCPReceiveData res = null;
                    m_PLC.Read(data, ref res);
                    ResultList.Add(new ResultData(res, m_SeparateChar));
                }
                else if (ReadCommandList.Count > 0)
                {
                    ResultList.Clear();
                    List<MCPSendData> dataList = new List<MCPSendData>();
                    List<MCPReceiveData> resList = null;
                    for (int i = 0; i < ReadCommandList.Count; i++)
                    {
                        dataList.Add(this.ConvertPLCData(ReadCommandList, i, true));
                    }
                    m_PLC.Read(dataList, ref resList);
                    for (int i = 0; i < resList.Count; i++)
                    {
                        ResultList.Add(new ResultData(resList[i], m_SeparateChar));
                    }
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }
        private MCPSendData ConvertPLCData(ObservableCollection<SendCommand> sendList, int index, bool isRead = false)
        {
            if (!isRead)
            {
                object val = null;
                string[] tmpBuf = sendList[index].Value.Split(m_SeparateChar);
                switch (sendList[index].DataType)
                {
                    case EParseDataType.Boolean:
                        List<bool> boolList = new List<bool>();
                        foreach (var str in tmpBuf)
                        {
                            if (bool.TryParse(str, out bool res)) boolList.Add(res);
                        }
                        val = boolList;
                        break;
                    case EParseDataType.Byte:
                        List<byte> byteList = new List<byte>();
                        foreach (var str in tmpBuf)
                        {
                            if (byte.TryParse(str, out byte res)) byteList.Add(res);
                        }
                        val = byteList;
                        break;
                    case EParseDataType.Short:
                        List<short> shortList = new List<short>();
                        foreach (var str in tmpBuf)
                        {
                            if (short.TryParse(str, out short res)) shortList.Add(res);
                        }
                        val = shortList;
                        break;
                    case EParseDataType.Int:
                        List<int> intList = new List<int>();
                        foreach (var str in tmpBuf)
                        {
                            if (int.TryParse(str, out int res)) intList.Add(res);
                        }
                        val = intList;
                        break;
                    case EParseDataType.Long:
                        List<long> longList = new List<long>();
                        foreach (var str in tmpBuf)
                        {
                            if (long.TryParse(str, out long res)) longList.Add(res);
                        }
                        val = longList;
                        break;
                    case EParseDataType.Float:
                        List<float> floatList = new List<float>();
                        foreach (var str in tmpBuf)
                        {
                            if (float.TryParse(str, out float res)) floatList.Add(res);
                        }
                        val = floatList;
                        break;
                    case EParseDataType.Double:
                        List<double> doubleList = new List<double>();
                        foreach (var str in tmpBuf)
                        {
                            if (double.TryParse(str, out double res)) doubleList.Add(res);
                        }
                        val = doubleList;
                        break;
                    case EParseDataType.String:
                        val = sendList[index].Value;
                        break;
                }
                return new MCPSendData(sendList[index].DeviceCode, sendList[index].DeviceNumber, val);
            }
            else
            {
                return new MCPSendData(sendList[index].DeviceCode, sendList[index].DeviceNumber, isRead, sendList[index].WordCount);
            }
        }

        internal void NewWriteCommand_button_Click()
        {
            try
            {
                WriteCommandList.Add(new SendCommand());
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }
        internal void DeleteWriteCommand_button_Click(int selectedIndex)
        {
            try
            {
                if (selectedIndex != -1 && WriteCommandList.Count > selectedIndex)
                {
                    WriteCommandList.RemoveAt(selectedIndex);
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }

        internal void NewReadCommand_button_Click()
        {
            try
            {
                ReadCommandList.Add(new SendCommand());
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }
        internal void DeleteReadCommand_button_Click(int selectedIndex)
        {
            try
            {
                if (selectedIndex != -1 && ReadCommandList.Count > selectedIndex)
                {
                    ReadCommandList.RemoveAt(selectedIndex);
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }

        internal void Window_Closing()
        {
            try
            {
                if (m_ConnectionCheckThread != null && m_ConnectionCheckThread.IsAlive)
                {
                    m_ConnectionCheckThread.Abort();
                    m_ConnectionCheckThread.Join(1000);
                }
                if (m_XmlParser != null) m_XmlParser.SaveXml();
                if (m_PLC != null) m_PLC.Dispose();
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message);
            }
        }
        #endregion
    }

    public enum EParseDataType
    {
        Boolean,
        Byte,
        Short,
        Int,
        Long,
        Float,
        Double,
        String
    }
    public class SendCommand
    {
        private int m_DeviceNumber;
        private string m_DeviceHexNumber;

        public EMCPDeviceCode DeviceCode { get; set; }
        public int DeviceNumber
        {
            get
            {
                if ((int)DeviceCode >= (int)EMCPDeviceCode.X && (int)DeviceCode <= (int)EMCPDeviceCode.DY)
                {
                    if (m_DeviceHexNumber != null && int.TryParse(m_DeviceHexNumber, System.Globalization.NumberStyles.HexNumber, null, out int num))
                    {
                        return num;
                    }
                    else return 0;
                }
                else return m_DeviceNumber;
            }
            set
            {
                m_DeviceHexNumber = value.ToString("X");
                m_DeviceNumber = value;
            }
        }
        public string DeviceHexNumber
        {
            get
            {
                if ((int)DeviceCode >= (int)EMCPDeviceCode.X && (int)DeviceCode <= (int)EMCPDeviceCode.DY) return m_DeviceHexNumber;
                else return m_DeviceNumber.ToString("X");
            }
            set
            {
                m_DeviceHexNumber = value;
                if (int.TryParse(m_DeviceHexNumber, System.Globalization.NumberStyles.HexNumber, null, out int num)) m_DeviceNumber = num;
                else m_DeviceNumber = 0;
            }
        }
        public ushort WordCount { get; set; }
        public EParseDataType DataType { get; set; }
        public string Value { get; set; }

        public SendCommand()
        {
            DeviceCode = EMCPDeviceCode.M;
            DataType = EParseDataType.Short;
            Value = string.Empty;
        }
    }
    public class ResultData : INotifyPropertyChanged
    {
        #region Fields
        private MCPReceiveData m_ReceiveData;
        private EParseDataType m_DataType;
        private char m_SeparateChar;

        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region Properties
        public EMCPDeviceCode DeviceCode
        {
            get
            {
                if (m_ReceiveData != null) return m_ReceiveData.DeviceCode;
                else return EMCPDeviceCode.M;
            }
        }
        public int DeviceNumber
        {
            get
            {
                if (m_ReceiveData != null) return m_ReceiveData.Address;
                else return -1;
            }
        }
        public string DeviceHexNumber
        {
            get
            {
                if (m_ReceiveData != null) return m_ReceiveData.Address.ToString("X");
                else return "-1";
            }
        }

        public char SeparateChar
        {
            get { return m_SeparateChar; }
            set
            {
                if (m_SeparateChar != value)
                {
                    m_SeparateChar = value;
                    if (m_ReceiveData != null) this.ChangeResultText();
                }
            }
        }

        public EParseDataType DataType
        {
            get
            {
                return m_DataType;
            }
            set
            {
                m_DataType = value;
                if (m_ReceiveData != null)
                {
                    this.ChangeResultText();
                }
            }
        }
        public string ResultText { get; private set; }
        #endregion

        public ResultData(MCPReceiveData receiveData, char separateChar)
        {
            this.m_ReceiveData = receiveData;
            this.SeparateChar = separateChar;
            DataType = EParseDataType.Byte;
        }

        #region Methods
        private void ChangeResultText()
        {
            ResultText = string.Empty;

            switch (m_DataType)
            {
                case EParseDataType.Boolean:
                    var boolArr = m_ReceiveData.GetBooleanArray();
                    foreach (var item in boolArr) ResultText += (item ? "True" : "False") + m_SeparateChar;
                    break;

                case EParseDataType.Short:
                    var shortArr = m_ReceiveData.GetInt16Array();
                    foreach (var item in shortArr) ResultText += item.ToString() + m_SeparateChar;
                    break;

                case EParseDataType.Int:
                    var intArr = m_ReceiveData.GetInt32Array();
                    foreach (var item in intArr) ResultText += item.ToString() + m_SeparateChar;
                    break;

                case EParseDataType.Float:
                    var floatArr = m_ReceiveData.GetSingleArray();
                    foreach (var item in floatArr) ResultText += item.ToString() + m_SeparateChar;
                    break;

                case EParseDataType.Long:
                    var longArr = m_ReceiveData.GetInt64Array();
                    foreach (var item in longArr) ResultText += item.ToString() + m_SeparateChar;
                    break;

                case EParseDataType.Double:
                    var doubleArr = m_ReceiveData.GetDoubleArray();
                    foreach (var item in doubleArr) ResultText += item.ToString() + m_SeparateChar;
                    break;

                case EParseDataType.String:
                    ResultText = m_ReceiveData.GetString();
                    break;

                case EParseDataType.Byte:
                default:
                    var byteArr = m_ReceiveData.GetByteArray();
                    foreach (var item in byteArr) ResultText += item.ToString("X2") + m_SeparateChar;
                    break;
            }

            if (ResultText.Length > 0) ResultText = ResultText.Remove(ResultText.Length - 1, 1);

            this.RaisePropertyChanged(nameof(ResultText));
        }

        protected void RaisePropertyChanged(string pName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(pName));
        }
        #endregion
    }
}
