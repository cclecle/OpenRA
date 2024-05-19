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
using OpenRA.Support;

namespace OpenRA.QueryStats
{
	public abstract class ExceptionMessage : Exception { }
	public class ExceptionWrongMessageHeader : ExceptionMessage { }

	public class MessageFactory : AMessageFactory<MessageFactory>
	{
		readonly ImmutableArray<AMessage> registeredMessages = new List<AMessage>
		{
			new A2S_INFO(),
			new S2A_INFO(),
			new S2A_PLAYER(),
			new A2S_PLAYER(),
			new A2S_RULES(),
			new S2A_RULES(),
			new S2C_CHALLENGE()
		}.ToImmutableArray();
		public override ImmutableArray<AMessage> RegisteredMessages { get => registeredMessages; }
	}

	public abstract class Message : AMessage
	{
		public abstract byte Header { get; }

		protected virtual StringBuilder ToStringBuilder()
		{
			return new StringBuilder(base.ToString())
				.AppendLine()
				.AppendLine($"\tHeader : {Header:X02}");
		}

		public override string ToString()
		{
			return ToStringBuilder()
				.ToString();
		}

		public override bool Identify(MemoryStream test_stream)
		{
			using (var reader = new BinaryReader(test_stream, new UTF8Encoding(), true))
			{
				try
				{
					var header = reader.ReadByte();
					test_stream.Seek(-1, SeekOrigin.Current);
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
			if (reader.ReadByte() != Header)
				throw new ExceptionWrongMessageHeader();
		}
	}

	public abstract class A2S_SimpleCommand : Message
	{
		public int? Challenge { get; set; }
		public override string ToString()
		{
			return ToStringBuilder()
				.AppendLine($"\tChallenge : {Challenge:X08}")
				.ToString();
		}

		protected override void UnSerializePayload()
		{
			try
			{
				Challenge = reader.ReadInt32();
			}
			catch (EndOfStreamException)
			{
				Challenge = null;
			}
		}

		protected override void SerializePayload()
		{
			if (Challenge != null)
				writer.Write((int)Challenge);
		}
	}

	public sealed class A2S_INFO : A2S_SimpleCommand
	{
		public override byte Header { get => 0x54; }

		public const string DefaultPayload = "Source Engine Query";
		public string Payload { get; set; } = DefaultPayload;

		protected override StringBuilder ToStringBuilder()
		{
			return base.ToStringBuilder()
				.AppendLine($"\tPayload : {Payload}");
		}

		protected override void UnSerializePayload()
		{
			Payload = reader.ReadNullTerminated();
			base.UnSerializePayload();
		}

		protected override void SerializePayload()
		{
			writer.WriteNullTerminated(Payload);
			base.SerializePayload();
		}
	}

	public sealed class S2A_INFO : Message
	{
		public override byte Header { get => 0x49; }
		public byte Protocol { get; private set; } = 48;
		public string Name { get; set; } = "";
		public string Map { get; set; } = "";
		public string Folder { get; set; } = "";
		public string Game { get; set; } = "";
		public short ID { get; set; } = 0;
		public byte Players { get; set; } = 0;
		public byte MaxPlayers { get; set; } = 0;
		public byte Bots { get; set; } = 0;
		public byte ServerType { get; set; } = (byte)'d';
		public byte Environment { get; set; } = (byte)'w';
		public byte Visibility { get; set; } = 0;
		public byte VAC { get; set; } = 0;
		public string Version { get; set; } = "";
		public byte ExtraDataFlag { get; private set; }
		public short? Port { get; set; } = null;
		public ulong? SteamID { get; set; } = null;
		public short? SpecPort { get; set; } = null;
		public string SpecName { get; set; } = null;
		public string Keywords { get; set; } = null;
		public ulong? GameID { get; set; } = null;
		protected override StringBuilder ToStringBuilder()
		{
			return base.ToStringBuilder()
				.AppendLine($"\tProtocol : {Protocol}")
				.AppendLine($"\tName : {Name}")
				.AppendLine($"\tMap : {Map}")
				.AppendLine($"\tFolder : {Folder}")
				.AppendLine($"\tGame : {Game}")
				.AppendLine($"\tID : {ID:X04}")
				.AppendLine($"\tPlayers : {Players}")
				.AppendLine($"\tMaxPlayers : {MaxPlayers}")
				.AppendLine($"\tBots : {Bots}")
				.AppendLine($"\tServerType : {Convert.ToChar(ServerType)}")
				.AppendLine($"\tEnvironment : {Convert.ToChar(Environment)}")
				.AppendLine($"\tVisibility : {Visibility}")
				.AppendLine($"\tVAC : {VAC}")
				.AppendLine($"\tVersion : {Version}")
				.AppendLine($"\tExtraDataFlag : {ExtraDataFlag:X02}")
				.AppendLine($"\tPort? : {Port}")
				.AppendLine($"\tSteamID? : {SteamID}")
				.AppendLine($"\tSpecPort? : {SpecPort}")
				.AppendLine($"\tSpecName? : {SpecName}")
				.AppendLine($"\tKeywords? : {Keywords}")
				.AppendLine($"\tGameID? : {GameID}");
		}

		protected override void UnSerializePayload()
		{
			Protocol = reader.ReadByte();
			Name = reader.ReadNullTerminated();
			Map = reader.ReadNullTerminated();
			Folder = reader.ReadNullTerminated();
			Game = reader.ReadNullTerminated();
			ID = reader.ReadInt16();
			Players = reader.ReadByte();
			MaxPlayers = reader.ReadByte();
			Bots = reader.ReadByte();
			ServerType = reader.ReadByte();
			Environment = reader.ReadByte();
			Visibility = reader.ReadByte();
			VAC = reader.ReadByte();
			Version = reader.ReadNullTerminated();
			ExtraDataFlag = reader.ReadByte();

			Port = null;
			if ((ExtraDataFlag & 0x80) != 0)
				Port = reader.ReadInt16();

			SteamID = null;
			if ((ExtraDataFlag & 0x10) != 0)
				SteamID = reader.ReadUInt64();

			SpecPort = null;
			SpecName = null;
			if ((ExtraDataFlag & 0x40) != 0)
			{
				SpecPort = reader.ReadInt16();
				SpecName = reader.ReadNullTerminated();
			}

			Keywords = null;
			if ((ExtraDataFlag & 0x20) != 0)
				Keywords = reader.ReadNullTerminated();

			GameID = null;
			if ((ExtraDataFlag & 0x01) != 0)
				GameID = reader.ReadUInt64();
		}

		protected override void SerializePayload()
		{
			writer.Write(Protocol);
			writer.WriteNullTerminated(Name);
			writer.WriteNullTerminated(Map);
			writer.WriteNullTerminated(Folder);
			writer.WriteNullTerminated(Game);
			writer.Write(ID);
			writer.Write(Players);
			writer.Write(MaxPlayers);
			writer.Write(Bots);
			writer.Write(ServerType);
			writer.Write(Environment);
			writer.Write(Visibility);
			writer.Write(VAC);
			writer.WriteNullTerminated(Version);

			ExtraDataFlag = 0;

			if (Port != null)
				ExtraDataFlag |= 0x80;

			if (SteamID != null)
				ExtraDataFlag |= 0x10;

			if ((SpecPort != null) && (SpecName != null))
				ExtraDataFlag |= 0x40;

			if (Keywords != null)
				ExtraDataFlag |= 0x20;

			if (GameID != null)
				ExtraDataFlag |= 0x01;

			writer.Write(ExtraDataFlag);

			if (Port != null)
				writer.Write((short)Port);

			if (SteamID != null)
				writer.Write((ulong)SteamID);

			if ((SpecPort != null) && (SpecName != null))
			{
				writer.Write((short)SpecPort);
				writer.WriteNullTerminated(SpecName);
			}

			if (Keywords != null)
				writer.WriteNullTerminated(Keywords);

			if (GameID != null)
				writer.Write((ulong)GameID);
		}
	}

	public sealed class A2S_RULES : A2S_SimpleCommand
	{
		public override byte Header { get => 0x56; }
	}

	public sealed class S2A_RULE_DATA : ISerializableMessage
	{
		public string Name;
		public string Value;
		public override string ToString()
		{
			return $"{Name} : {Value}";
		}

		public MemoryStream Serialize()
		{
			var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(), true))
			{
				writer.WriteNullTerminated(Name);
				writer.WriteNullTerminated(Value);
			}

			return stream;
		}

		public void UnSerialize(MemoryStream stream)
		{
			using (var reader = new BinaryReader(stream, new UTF8Encoding(), true))
			{
				Name = reader.ReadNullTerminated();
				Value = reader.ReadNullTerminated();
			}
		}
	}

	public sealed class S2A_RULES : Message
	{
		public override byte Header { get => 0x45; }
		public IList<S2A_RULE_DATA> Rules { get; } = new List<S2A_RULE_DATA> { Capacity = 32767 };
		protected override StringBuilder ToStringBuilder()
		{
			var sb = base.ToStringBuilder();

			sb.AppendLine("\tRules :");
			foreach (var r in Rules)
				sb.AppendLine($"\t\t{r}");

			return sb;
		}

		protected override void SerializePayload()
		{
			writer.Write((short)Rules.Count);

			foreach (var player_data in Rules)
				player_data.Serialize().WriteTo(stream);
		}

		protected override void UnSerializePayload()
		{
			var nbRules = reader.ReadInt16();

			Rules.Clear();
			for (var index = 0; index < nbRules; index++)
			{
				var new_rule = new S2A_RULE_DATA();
				new_rule.UnSerialize(stream);
				Rules.Add(new_rule);
			}
		}
	}

	public sealed class S2C_CHALLENGE : Message
	{
		public override byte Header { get => 0x41; }
		public int Challenge { get; set; } = 0;
		protected override StringBuilder ToStringBuilder()
		{
			return base.ToStringBuilder()
				.AppendLine($"\tChallenge : {Challenge:X08}");
		}

		protected override void UnSerializePayload()
		{
			Challenge = reader.ReadInt32();
		}

		protected override void SerializePayload()
		{
			writer.Write(Challenge);
		}
	}

	public class A2S_PLAYER : A2S_SimpleCommand
	{
		public override byte Header { get => 0x55; }
	}

	public sealed class S2A_PLAYER_DATA : ISerializableMessage
	{
		public byte Index;
		public string PlayerName;
		public int Score;
		public float Duration;
		public override string ToString()
		{
			return $"{Index}\t{PlayerName}\t{Score}\t{Duration}";
		}

		public MemoryStream Serialize()
		{
			var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(), true))
			{
				writer.Write(Index);
				writer.WriteNullTerminated(PlayerName);
				writer.Write(Score);
				writer.Write(Duration);
			}

			return stream;
		}

		public void UnSerialize(MemoryStream stream)
		{
			using (var reader = new BinaryReader(stream, new UTF8Encoding(), true))
			{
				Index = reader.ReadByte();
				PlayerName = reader.ReadNullTerminated();
				Score = reader.ReadInt32();
				Duration = reader.ReadSingle();
			}
		}
	}

	public sealed class S2A_PLAYER : Message
	{
		public override byte Header { get => 0x44; }
		public IList<S2A_PLAYER_DATA> Players { get; } = new List<S2A_PLAYER_DATA> { Capacity = 255 };
		protected override StringBuilder ToStringBuilder()
		{
			var sb = base.ToStringBuilder();

			sb.AppendLine("\tPlayers :");
			foreach (var p in Players)
				sb.AppendLine($"\t\t{p}");

			return sb;
		}

		protected override void SerializePayload()
		{
			writer.Write((byte)Players.Count);
			foreach (var player_data in Players)
				player_data.Serialize().WriteTo(stream);
		}

		protected override void UnSerializePayload()
		{
			var nbPlayers = reader.ReadByte();
			Players.Clear();
			for (var index = 0; index < nbPlayers; index++)
			{
				var new_player = new S2A_PLAYER_DATA();
				new_player.UnSerialize(stream);
				Players.Add(new_player);
			}
		}
	}
}
