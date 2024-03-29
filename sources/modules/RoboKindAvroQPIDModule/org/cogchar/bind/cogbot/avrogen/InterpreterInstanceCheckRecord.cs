// ------------------------------------------------------------------------------
// <auto-generated>
//    Generated by RoboKindChat.vshost.exe, version 0.9.0.0
//    Changes to this file may cause incorrect behavior and will be lost if code
//    is regenerated
// </auto-generated>
// ------------------------------------------------------------------------------
namespace org.cogchar.bind.cogbot.avrogen
{
	using System;
	using System.Collections.Generic;
	using System.Text;
	using Avro;
	using Avro.Specific;
	
	public partial class InterpreterInstanceCheckRecord : ISpecificRecord, InterpreterInstanceCheck
	{
		private static Schema _SCHEMA = Avro.Schema.Parse(@"{""type"":""record"",""name"":""InterpreterInstanceCheckRecord"",""namespace"":""org.cogchar.bind.cogbot.avrogen"",""fields"":[{""name"":""sourceId"",""type"":""string""},{""name"":""destinationId"",""type"":""string""},{""name"":""timestampMillisecUTC"",""type"":""long""},{""name"":""requestId"",""type"":""string""},{""name"":""instanceLoadId"",""type"":""string""},{""name"":""instanceName"",""type"":""string""}]}");
		private string _sourceId;
		private string _destinationId;
		private long _timestampMillisecUTC;
		private string _requestId;
		private string _instanceLoadId;
		private string _instanceName;
		public virtual Schema Schema
		{
			get
			{
				return InterpreterInstanceCheckRecord._SCHEMA;
			}
		}
		public string sourceId
		{
			get
			{
				return this._sourceId;
			}
			set
			{
				this._sourceId = value;
			}
		}
		public string destinationId
		{
			get
			{
				return this._destinationId;
			}
			set
			{
				this._destinationId = value;
			}
		}
		public long timestampMillisecUTC
		{
			get
			{
				return this._timestampMillisecUTC;
			}
			set
			{
				this._timestampMillisecUTC = value;
			}
		}
		public string requestId
		{
			get
			{
				return this._requestId;
			}
			set
			{
				this._requestId = value;
			}
		}
		public string instanceLoadId
		{
			get
			{
				return this._instanceLoadId;
			}
			set
			{
				this._instanceLoadId = value;
			}
		}
		public string instanceName
		{
			get
			{
				return this._instanceName;
			}
			set
			{
				this._instanceName = value;
			}
		}
		public virtual object Get(int fieldPos)
		{
			switch (fieldPos)
			{
			case 0: return this.sourceId;
			case 1: return this.destinationId;
			case 2: return this.timestampMillisecUTC;
			case 3: return this.requestId;
			case 4: return this.instanceLoadId;
			case 5: return this.instanceName;
			default: throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()");
			};
		}
		public virtual void Put(int fieldPos, object fieldValue)
		{
			switch (fieldPos)
			{
			case 0: this.sourceId = (System.String)fieldValue; break;
			case 1: this.destinationId = (System.String)fieldValue; break;
			case 2: this.timestampMillisecUTC = (System.Int64)fieldValue; break;
			case 3: this.requestId = (System.String)fieldValue; break;
			case 4: this.instanceLoadId = (System.String)fieldValue; break;
			case 5: this.instanceName = (System.String)fieldValue; break;
			default: throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
			};
		}
	}
}
