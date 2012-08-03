
using Apache.Qpid.Buffer;
using System.Text;

namespace Apache.Qpid.Framing
{
  ///
  /// This class is autogenerated
  /// Do not modify.
  ///
  /// @author Code Generator Script by robert.j.greig@jpmorgan.com
  public class StreamCancelBody : AMQMethodBody , IEncodableAMQDataBlock
  {
    public const int CLASS_ID = 80; 	
    public const int METHOD_ID = 30; 	

    public string ConsumerTag;    
    public bool Nowait;    
     

    protected override ushort Clazz
    {
        get
        {
            return 80;
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
        (uint)EncodingUtils.EncodedShortStringLength(ConsumerTag)+
            1 /*Nowait*/		 
        ;
         
    }
    }

    protected override void WriteMethodPayload(ByteBuffer buffer)
    {
        EncodingUtils.WriteShortStringBytes(buffer, ConsumerTag);
            EncodingUtils.WriteBooleans(buffer, new bool[]{Nowait});
            		 
    }

    protected override void PopulateMethodBodyFromBuffer(ByteBuffer buffer)
    {
        ConsumerTag = EncodingUtils.ReadShortString(buffer);
        bool[] bools = EncodingUtils.ReadBooleans(buffer);Nowait = bools[0];
        		 
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(base.ToString());
        buf.Append(" ConsumerTag: ").Append(ConsumerTag);
        buf.Append(" Nowait: ").Append(Nowait);
         
        return buf.ToString();
    }

    public static AMQFrame CreateAMQFrame(ushort channelId, string ConsumerTag, bool Nowait)
    {
        StreamCancelBody body = new StreamCancelBody();
        body.ConsumerTag = ConsumerTag;
        body.Nowait = Nowait;
        		 
        AMQFrame frame = new AMQFrame();
        frame.Channel = channelId;
        frame.BodyFrame = body;
        return frame;
    }
} 
}
