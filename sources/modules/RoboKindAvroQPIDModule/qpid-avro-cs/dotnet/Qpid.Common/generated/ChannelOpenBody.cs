
using Apache.Qpid.Buffer;
using System.Text;

namespace Apache.Qpid.Framing
{
  ///
  /// This class is autogenerated
  /// Do not modify.
  ///
  /// @author Code Generator Script by robert.j.greig@jpmorgan.com
  public class ChannelOpenBody : AMQMethodBody , IEncodableAMQDataBlock
  {
    public const int CLASS_ID = 20; 	
    public const int METHOD_ID = 10; 	

    public string OutOfBand;    
     

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
            return 10;
        }
    }

    protected override uint BodySize
    {
    get
    {
        
        return (uint)
        (uint)EncodingUtils.EncodedShortStringLength(OutOfBand)		 
        ;
         
    }
    }

    protected override void WriteMethodPayload(ByteBuffer buffer)
    {
        EncodingUtils.WriteShortStringBytes(buffer, OutOfBand);
            		 
    }

    protected override void PopulateMethodBodyFromBuffer(ByteBuffer buffer)
    {
        OutOfBand = EncodingUtils.ReadShortString(buffer);
        		 
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(base.ToString());
        buf.Append(" OutOfBand: ").Append(OutOfBand);
         
        return buf.ToString();
    }

    public static AMQFrame CreateAMQFrame(ushort channelId, string OutOfBand)
    {
        ChannelOpenBody body = new ChannelOpenBody();
        body.OutOfBand = OutOfBand;
        		 
        AMQFrame frame = new AMQFrame();
        frame.Channel = channelId;
        frame.BodyFrame = body;
        return frame;
    }
} 
}
