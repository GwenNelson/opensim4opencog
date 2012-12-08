
using Apache.Qpid.Buffer;
using System.Text;

namespace Apache.Qpid.Framing
{
  ///
  /// This class is autogenerated
  /// Do not modify.
  ///
  /// @author Code Generator Script by robert.j.greig@jpmorgan.com
  public class QueueDeclareBody : AMQMethodBody , IEncodableAMQDataBlock
  {
    public const int CLASS_ID = 50; 	
    public const int METHOD_ID = 10; 	

    public ushort Ticket;    
    public string Queue;    
    public bool Passive;    
    public bool Durable;    
    public bool Exclusive;    
    public bool AutoDelete;    
    public bool Nowait;    
    public FieldTable Arguments;    
     

    protected override ushort Clazz
    {
        get
        {
            return 50;
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
        2 /*Ticket*/+
            (uint)EncodingUtils.EncodedShortStringLength(Queue)+
            1 /*Passive*/+
            0 /*Durable*/+
            0 /*Exclusive*/+
            0 /*AutoDelete*/+
            0 /*Nowait*/+
            (uint)EncodingUtils.EncodedFieldTableLength(Arguments)		 
        ;
         
    }
    }

    protected override void WriteMethodPayload(ByteBuffer buffer)
    {
        buffer.Put(Ticket);
            EncodingUtils.WriteShortStringBytes(buffer, Queue);
            EncodingUtils.WriteBooleans(buffer, new bool[]{Passive, Durable, Exclusive, AutoDelete, Nowait});
            EncodingUtils.WriteFieldTableBytes(buffer, Arguments);
            		 
    }

    protected override void PopulateMethodBodyFromBuffer(ByteBuffer buffer)
    {
        Ticket = buffer.GetUInt16();
        Queue = EncodingUtils.ReadShortString(buffer);
        bool[] bools = EncodingUtils.ReadBooleans(buffer);Passive = bools[0];
        Durable = bools[1];
        Exclusive = bools[2];
        AutoDelete = bools[3];
        Nowait = bools[4];
        Arguments = EncodingUtils.ReadFieldTable(buffer);
        		 
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(base.ToString());
        buf.Append(" Ticket: ").Append(Ticket);
        buf.Append(" Queue: ").Append(Queue);
        buf.Append(" Passive: ").Append(Passive);
        buf.Append(" Durable: ").Append(Durable);
        buf.Append(" Exclusive: ").Append(Exclusive);
        buf.Append(" AutoDelete: ").Append(AutoDelete);
        buf.Append(" Nowait: ").Append(Nowait);
        buf.Append(" Arguments: ").Append(Arguments);
         
        return buf.ToString();
    }

    public static AMQFrame CreateAMQFrame(ushort channelId, ushort Ticket, string Queue, bool Passive, bool Durable, bool Exclusive, bool AutoDelete, bool Nowait, FieldTable Arguments)
    {
        QueueDeclareBody body = new QueueDeclareBody();
        body.Ticket = Ticket;
        body.Queue = Queue;
        body.Passive = Passive;
        body.Durable = Durable;
        body.Exclusive = Exclusive;
        body.AutoDelete = AutoDelete;
        body.Nowait = Nowait;
        body.Arguments = Arguments;
        		 
        AMQFrame frame = new AMQFrame();
        frame.Channel = channelId;
        frame.BodyFrame = body;
        return frame;
    }
} 
}