using PLCProtocol.Mitsubishi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace PLCProtocol_TestApp.Xml
{
    /// <summary>
    /// 여러 설정 값을 XML 형식으로 저장/불러오기 하는 클래스입니다.
    /// </summary>
    public class XmlParser : IDisposable
    {
        #region Fields
        private readonly string m_DefaultDirPath = AppDomain.CurrentDomain.BaseDirectory + @"\Config\";
        private XmlSerializer m_XmlSerializer;
        #endregion

        #region Properties
        /// <summary>
        /// 현재 다루는 데이터의 XML 타입을 가져옵니다. 
        /// </summary>
        public EXmlType XmlType { get; }

        /// <summary>
        /// XML에서 불러온 데이터를 가져옵니다.
        /// </summary>
        public ParsedData ParsedData { get; private set; }
        #endregion

        /// <summary>
        /// 여러 설정 값을 XML 형식으로 저장/불러오기 하는 클래스를 생성합니다.
        /// </summary>
        /// <param name="xmlType">다룰 데이터의 XML 타입.</param>
        public XmlParser(EXmlType xmlType)
        {
            this.XmlType = xmlType;
            switch (xmlType)
            {
                case EXmlType.MitsubishiPLCData:
                    m_XmlSerializer = new XmlSerializer(typeof(MitsubishiPLCData));
                    ParsedData = new MitsubishiPLCData();
                    break;

            }
        }

        public void Dispose()
        {
            ParsedData = null;
            m_XmlSerializer = null;
        }

        #region Methods
        /// <summary>
        /// XML로부터 데이터를 불러옵니다.
        /// </summary>
        /// <returns></returns>
        public void LoadXml()
        {
            if (!File.Exists(m_DefaultDirPath + XmlType.ToString() + ".xml"))
            {
                this.SaveXml();
            }
            using (var sr = new StreamReader(m_DefaultDirPath + XmlType.ToString() + ".xml"))
            {
                ParsedData = m_XmlSerializer.Deserialize(sr) as ParsedData;
            }
        }

        /// <summary>
        /// 현재 데이터를 XML로 저장합니다.
        /// </summary>
        /// <returns></returns>
        public void SaveXml()
        {
            Directory.CreateDirectory(m_DefaultDirPath);
            using (var sw = new StreamWriter(m_DefaultDirPath + XmlType.ToString() + ".xml"))
            {
                m_XmlSerializer.Serialize(sw, ParsedData);
            }
        }
        #endregion
    }

    #region Parse Data Classes
    /// <summary>
    /// 데이터의 XML 타입
    /// </summary>
    public enum EXmlType
    {
        MitsubishiPLCData
    }

    /// <summary>
    /// XML로 저장/불러오기 할 데이터 클래스입니다.
    /// </summary>
    public class ParsedData
    {
    }
    
    #region PLC Setting
    public class MitsubishiPLCData : ParsedData
    {
        public string IP { get; set; }
        public ushort PortNumber { get; set; }
        public byte NetworkNo { get; set; }
        public byte PCNo { get; set; }
        public EMCProtocolFormat ProtocolFormat { get; set; }

        public MitsubishiPLCData()
        {
            IP = string.Empty;
            PCNo = 0xFF;
            ProtocolFormat = EMCProtocolFormat.Binary;
        }
    }
    #endregion

    #endregion
}
