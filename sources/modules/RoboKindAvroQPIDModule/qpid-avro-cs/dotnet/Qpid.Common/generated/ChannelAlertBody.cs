
using Apache.Qpid.Buffer;
using System.Text;

namespace Apache.Qpid.Framing
{
  ///
  /// This class is autogenerated
  /// Do not modify.
  ///
  /// @author Code Generator Script by robert.j.greig@jpmorgan.com
  public class ChannelAlertBody : AMQMethodBody , IEncodableAMQDataBlock
  {
    public const int CLASS_ID = 20; 	
    public const int METHOD_ID = 30; 	

    public ushort ReplyCode;    
    public string ReplyText;    
    public FieldTable Details;    
     

    protected override ushort Clazz
    {
        get
        {
            return 20;
        }
    }
   
    protected override ushort Method
    {
        get
        {
            return 30;
        }
    }

    protected override uint BodySize
    {
    get
    {
        
        return (uint)
        2 /*ReplyCode*/+
            (uint)EncodingUtils.EncodedShortStringLength(ReplyText)+
            (uint)EncodingUtils.EncodedFieldTableLength(Details)		 
        ;
         
    }
    }

    protected override void WriteMethodPayload(ByteBuffer buffer)
    {
        buffer.Put(ReplyCode);
            EncodingUtils.WriteShortStringBytes(buffer, ReplyText);
            EncodingUtils.WriteFieldTableBytes(buffer, Details);
            		 
    }

    protected override void PopulateMethodBodyFromBuffer(ByteBuffer buffer)
    {
        ReplyCode = buffer.GetUInt16();
        ReplyText = EncodingUtils.ReadShortString(buffer);
        Details = EncodingUtils.ReadFieldTable(buffer);
        		 
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(base.ToString());
        buf.Append(" ReplyCode: ").Append(ReplyCode);
        buf.Append(" ReplyText: ").Append(ReplyText);
        buf.Append(" Details: ").Append(Details);
         
        return buf.ToString();
    }

    public static AMQFrame CreateAMQFrame(ushort channelId, ushort ReplyCode, string ReplyText, FieldTable Details)
    {
        ChannelAlertBody body = new ChannelAlertBody();
        body.ReplyCode = ReplyCode;
        body.ReplyText = ReplyText;
        body.Details = Details;
        		 
        AMQFrame frame = new AMQFrame();
        frame.Channel = channelId;
        frame.BodyFrame = body;
        return frame;
    }
} 
}
