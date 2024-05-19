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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace OpenRA.QueryStats
{
	public abstract class ExceptionPacket : Exception { }
	public class ExceptionPacketWrongHeader : ExceptionPacket { }
	public class ExceptionPacketWrongID : ExceptionPacket { }

	public class PacketFactory : AMessageFactory<PacketFactory>
	{
		readonly ImmutableArray<AMessage> registeredMessages = new List<AMessage>
		{
			new PacketShort(),
			new PacketLong()
		}.ToImmutableArray();
		public override ImmutableArray<AMessage> RegisteredMessages { get => registeredMessages; }
	}

	public abstract class Packet : AMessage
	{
		public abstract int Header { get; }
		public MemoryStream Payload { get; set; } = new MemoryStream();
		protected virtual StringBuilder HeaderToStringBuilder()
		{
			return new StringBuilder(base.ToString())
				.AppendLine()
				.AppendLine($"\tHeader : {Header:X02}");
		}

		public override string ToString()
		{
			return HeaderToStringBuilder()
				.AppendLine($"\tPayload : {BitConverter.ToString(Payload.ToArray())}")
				.ToString();
		}

		public override bool Identify(MemoryStream test_stream)
		{
			using (var reader = new BinaryReader(test_stream, new UTF8Encoding(), true))
			{
				try
				{
					var header = reader.ReadInt32();
					test_stream.Seek(-4, SeekOrigin.Current);
					return header == Header;
				}
				catch (EndOfStreamException)
				{
					return false;
				}
			}
		}

		protected override void SerializeHeader()
		{
			writer.Write(Header);
		}

		protected override void UnSerializeHeader()
		{
			if (reader.ReadInt32() != Header)
				throw new ExceptionPacketWrongHeader();
		}

		protected override void SerializePayload()
		{
			Payload.WriteTo(stream);
		}

		protected override void UnSerializePayload()
		{
			Payload = new MemoryStream();
			stream.CopyTo(Payload);
		}
	}

	public sealed class PacketShort : Packet
	{
		public override int Header { get => -1; }
	}

	public sealed class PacketLong : Packet
	{
		public override int Header { get => -2; }

		uint id;
		public int ID
		{
			get => (int)id;
			set
			{
				if (value >= 0x7FFFFFFF)
					throw new ExceptionPacketWrongID();
				id = (uint)(((uint)value & 0x7FFFFFFF) + (Convert.ToInt32(ID_compressed) << 31));
			}
		}

		public bool ID_compressed
		{
			get => (id & 0x80000000) > 0;
			set => id = (uint)((id & 0x7FFFFFFF) + (Convert.ToInt32(value) << 31));
		}

		public byte Total { get; set; } = 1;
		public byte Number { get; set; } = 0;
		public short Size { get; private set; } = 1248;
		public int? CompressedSize { get; set; } = null;
		public int? CompressedCRC32 { get; set; } = null;
		protected override StringBuilder HeaderToStringBuilder()
		{
			return base.HeaderToStringBuilder()
				.AppendLine($"\tID : {ID:X08}")
				.AppendLine($"\tID_compressed : {ID_compressed}")
				.AppendLine($"\tTotal : {Total}")
				.AppendLine($"\tNumber : {Number}")
				.AppendLine($"\tSize : {Size}")
				.AppendLine($"\tCompressedSize : {CompressedSize}")
				.AppendLine($"\tCompressedCRC32 : {CompressedCRC32}");
		}

		void Check()
		{
			if (Total < 1)
				throw new ExceptionPacketWrongHeader();
			if (Number >= Total)
				throw new ExceptionPacketWrongHeader();
		}

		protected override void SerializeHeader()
		{
			Check();
			base.SerializeHeader();
			writer.Write(id);
			writer.Write(Total);
			writer.Write(Number);
			writer.Write(Size);
			if (ID_compressed && (Number == 0))
			{
				writer.Write((int)CompressedSize);
				writer.Write((int)CompressedCRC32);
			}
		}

		protected override void UnSerializeHeader()
		{
			base.UnSerializeHeader();
			id = reader.ReadUInt32();
			Total = reader.ReadByte();
			Number = reader.ReadByte();
			Size = reader.ReadInt16();
			if (ID_compressed && (Number == 0))
			{
				CompressedSize = reader.ReadInt32();
				CompressedCRC32 = reader.ReadInt32();
			}
			else
			{
				CompressedSize = null;
				CompressedCRC32 = null;
			}

			Check();
		}
	}
}
