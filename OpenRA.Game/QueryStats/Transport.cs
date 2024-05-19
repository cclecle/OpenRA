#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRA.QueryStats
{
	public abstract class ExceptionFrame : Exception { }
	public class ExceptionFrameAlreadyCompleted : ExceptionFrame { }
	public class ExceptionFrameWrongPacket : ExceptionFrame { }
	public class ExceptionFrameWrongPacketType : ExceptionFrameWrongPacket { }
	public class ExceptionFrameWrongPacketID : ExceptionFrameWrongPacket { }
	public class Frame : IEnumerable<Packet>
	{
		static readonly MessageFactory MessageFactory = MessageFactory.GetFactory();
		static readonly Random Rnd = new((int)DateTime.Now.Ticks & 0x0000FFFF);
		public short MaxPacketSize { get; protected set; } = 1248;
		readonly IList<Packet> pkts = new List<Packet>();
		public Message Msg { get; protected set; } = null;
		public uint ID { get; protected set; } = 0;
		public byte Total { get; protected set; } = 1;
		public bool Completed { get; protected set; } = false;

		/// <summary>
		/// Create a new Frame from a message.
		/// Frame created from a message are automaticaly completed, [Completed] flag is set.
		/// </summary>
		/// <param name="msg">The message to use to create the Frame.</param>
		public Frame(Message msg)
		{
			Msg = msg;
			var raw_msg = msg.Serialize();

			if (raw_msg.Length <= MaxPacketSize)
			{
				Total = 1;
				pkts.Add(new PacketShort() { Payload = raw_msg });
				return;
			}

			ID = (uint)Rnd.Next(0x7FFFFFFF);
			byte number = 0;
			while (true)
			{
				var buffer = new byte[MaxPacketSize];
				var byte_read = raw_msg.Read(buffer, 0, MaxPacketSize);
				if (byte_read <= 0)
					break;
				Array.Resize(ref buffer, byte_read);
				var pkt = new PacketLong() { ID = (int)ID, Payload = new MemoryStream(buffer, false), Number = number };
				pkts.Add(pkt);
				number++;
			}

			foreach (var pkt in pkts)
				((PacketLong)pkt).Total = number;

			Completed = true;
		}

		/// <summary>
		/// Create a new Frame from a packet.
		/// if the Frame is incomplete (missing packets), [Completed] flag will not be setted to true.
		/// </summary>
		/// <param name="pkt">The packet to use to create the Frame.</param>
		public Frame(Packet pkt)
		{
			if (pkt.GetType() == typeof(PacketShort))
			{
				pkt.Payload.Seek(0, SeekOrigin.Begin);
				Msg = (Message)MessageFactory.GetMessage(pkt.Payload);
				Completed = true;
			}
			else if (pkt.GetType() == typeof(PacketLong))
			{
				var pktLong = (PacketLong)pkt;
				ID = (uint)pktLong.ID;
				MaxPacketSize = pktLong.Size;
				Total = pktLong.Total;
				pkts.Add(pkt);
			}
			else
			{
				throw new ExceptionFrameWrongPacketType();
			}
		}

		/// <summary>
		/// Add a packet to an existing (incomplete) Frame.
		/// if the Frame is incomplete (missing packets), [Completed] flag will not be setted to true.
		/// </summary>
		/// <param name="pkt">The packet to add to the Frame.</param>
		public void AddPacket(PacketLong pkt)
		{
			if (Completed)
				throw new ExceptionFrameAlreadyCompleted();

			if (pkt.ID != ID)
				throw new ExceptionFrameWrongPacketID();

			if (pkt.Size != MaxPacketSize)
			{
				// throw new ExceptionFrameWrongPacket();
				System.Diagnostics.Debug.Write("pkt field [Size] differs from the initial request (keeping original).");
			}

			if (pkt.Total != Total)
			{
				// throw new ExceptionFrameWrongPacket();
				System.Diagnostics.Debug.Write("pkt field [Total] differs from the initial request (keeping original).");
			}

			if (pkts.Any(p => ((PacketLong)p).Number == pkt.Number))
				throw new ExceptionFrameWrongPacket();

			pkts.Add(pkt);

			if (pkts.Count == Total)
			{
				var raw_message_stream = new MemoryStream();
				foreach (var p in pkts.OrderBy(p => ((PacketLong)p).Number))
					p.Payload.WriteTo(raw_message_stream);
				raw_message_stream.Seek(0, SeekOrigin.Begin);
				Msg = (Message)MessageFactory.GetMessage(raw_message_stream);
				Completed = true;
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public IEnumerator<Packet> GetEnumerator()
		{
			foreach (var pkt in pkts)
				yield return pkt;
		}
	}
}
