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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenRA.Support;

namespace OpenRA.QueryStats
{
	public class S2A_PLAYER_EX_DATA_FIELD_Factory : S2A_PLAYER_EX_DATA_FIELD_Factory<S2A_PLAYER_EX_DATA_FIELD_Factory> { }
	public class S2A_PLAYER_EX_DATA_FIELD_Factory<T> where T : S2A_PLAYER_EX_DATA_FIELD_Factory<T>, new()
	{
		static T Factory { get; set; } = null;
		static readonly object FactoryLock = new();
		public static T GetFactory()
		{
			if (Factory == null)
			{
				lock (FactoryLock)
				{
					Factory ??= new T();
				}
			}

			return Factory;
		}

		readonly A_S2A_PLAYER_EX_DATA_FIELD[] basePlayerExDataInfoTypes =
		{
			new S2A_PLAYER_EX_DATA_FIELD__Byte(),
			new S2A_PLAYER_EX_DATA_FIELD__Short(),
			new S2A_PLAYER_EX_DATA_FIELD__Int(),
			new S2A_PLAYER_EX_DATA_FIELD__String(),
			new S2A_PLAYER_EX_DATA_FIELD__Float32(),
		};

		readonly A_S2A_PLAYER_EX_DATA_FIELD[] basePlayerExDataInfo =
		{
			// new S2A_PLAYER_EX_DATA_FIELD__Byte(),
			new S2A_PLAYER_EX_DATA_FIELD__IsInTeam(),
			new S2A_PLAYER_EX_DATA_FIELD__TeamId(),
			new S2A_PLAYER_EX_DATA_FIELD__IsBot(),
			new S2A_PLAYER_EX_DATA_FIELD__IsAdmin(),
			new S2A_PLAYER_EX_DATA_FIELD__IsSpectating(),
			new S2A_PLAYER_EX_DATA_FIELD__IsAuthenticated(),

			// new S2A_PLAYER_EX_DATA_FIELD__Short(),
			new S2A_PLAYER_EX_DATA_FIELD__Ping(),

			// new S2A_PLAYER_EX_DATA_FIELD__Int(),
			new S2A_PLAYER_EX_DATA_FIELD__Score(),
			new S2A_PLAYER_EX_DATA_FIELD__ArmyValue(),
			new S2A_PLAYER_EX_DATA_FIELD__AssetsValue(),
			new S2A_PLAYER_EX_DATA_FIELD__BuildingsDead(),
			new S2A_PLAYER_EX_DATA_FIELD__BuildingsKilled(),
			new S2A_PLAYER_EX_DATA_FIELD__Earned(),
			new S2A_PLAYER_EX_DATA_FIELD__UnitsDead(),
			new S2A_PLAYER_EX_DATA_FIELD__UnitsKilled(),

			// new S2A_PLAYER_EX_DATA_FIELD__String(),
			new S2A_PLAYER_EX_DATA_FIELD__UUID(),
			new S2A_PLAYER_EX_DATA_FIELD__Name(),
			new S2A_PLAYER_EX_DATA_FIELD__FullName(),
			new S2A_PLAYER_EX_DATA_FIELD__IP(),
			new S2A_PLAYER_EX_DATA_FIELD__Faction(),

			// new S2A_PLAYER_EX_DATA_FIELD__Float32(),
			new S2A_PLAYER_EX_DATA_FIELD__ConnectionTime(),
			new S2A_PLAYER_EX_DATA_FIELD__APM(),
			new S2A_PLAYER_EX_DATA_FIELD__MapExplored(),
		};

		// public readonly List<A_S2A_PLAYER_EX_DATA_FIELD> CustomPlayerExDataInfo = new();
		public readonly IList<A_S2A_PLAYER_EX_DATA_FIELD> CustomPlayerExDataInfo = new List<A_S2A_PLAYER_EX_DATA_FIELD>()
		{
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbHarvester(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbCarryAll(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbFactory(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbHeavyFactory(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbBarrack(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbStarPort(),
			new S2A_PLAYER_EX_DATA_FIELD__ORA__NbCommandCenter(),
		};

		public A_S2A_PLAYER_EX_DATA_FIELD[] RegisteredPlayerExDataInfo { get => basePlayerExDataInfo.Union(CustomPlayerExDataInfo).ToArray(); }
		public A_S2A_PLAYER_EX_DATA_FIELD GetInfo(MemoryStream test_stream)
		{
			// First search for Fully defined Info Fields
			foreach (var info_ref in RegisteredPlayerExDataInfo)
			{
				if (info_ref.Identify(test_stream))
				{
					var new_msg = (A_S2A_PLAYER_EX_DATA_FIELD)Activator.CreateInstance(info_ref.GetType());
					new_msg.UnSerialize(test_stream);
					return new_msg;
				}
			}

			// Fall back to generic types Fields
			foreach (var info_ref in basePlayerExDataInfoTypes)
			{
				if (info_ref.Identify(test_stream))
				{
					var new_msg = (A_S2A_PLAYER_EX_DATA_FIELD)Activator.CreateInstance(info_ref.GetType());
					new_msg.UnSerialize(test_stream);
					return new_msg;
				}
			}

			throw new Exception("Info field not identified");
		}
	}

	public class A2S_PLAYER_EX : A2S_SimpleCommand
	{
		public override byte Header { get => 0x70; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Byte : A_S2A_PLAYER_EX_DATA_FIELD
	{
		public override string Name { get => "Unknown Byte Attribute"; }
		public override byte AttrType { get => 1; }
		public byte AttrValue;
		protected override void SerializeInfo(BinaryWriter writer) => writer.Write(AttrValue);
		protected override void UnSerializeInfo(BinaryReader reader) => AttrValue = reader.ReadByte();
		public override string ToString()
		{
			return AttrValue.ToString(CultureInfo.InvariantCulture);
		}

		public override string ToFormatedString() => base.ToFormatedString() + ToString();
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IsInTeam : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "IsInTeam"; }
		public override byte AttrId { get => 36; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__TeamId : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "TeamId"; }
		public override byte AttrId { get => 37; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IsBot : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "IsBot"; }
		public override byte AttrId { get => 41; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IsAdmin : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "IsAdmin"; }
		public override byte AttrId { get => 42; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IsSpectating : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "IsSpectating"; }
		public override byte AttrId { get => 43; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IsAuthenticated : S2A_PLAYER_EX_DATA_FIELD__Byte
	{
		public override string Name { get => "IsAuthenticated"; }
		public override byte AttrId { get => 44; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Short : A_S2A_PLAYER_EX_DATA_FIELD
	{
		public override string Name { get => "Unknown Short Attribute"; }
		public override byte AttrType { get => 2; }
		public short AttrValue;
		protected override void SerializeInfo(BinaryWriter writer) => writer.Write(AttrValue);
		protected override void UnSerializeInfo(BinaryReader reader) => AttrValue = reader.ReadInt16();
		public override string ToString() => AttrValue.ToString(CultureInfo.InvariantCulture);

		public override string ToFormatedString() => base.ToFormatedString() + ToString();
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Ping : S2A_PLAYER_EX_DATA_FIELD__Short
	{
		public override string Name { get => "Ping (ms)"; }
		public override byte AttrId { get => 35; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Int : A_S2A_PLAYER_EX_DATA_FIELD
	{
		public override string Name { get => "Unknown Int Attribute"; }
		public override byte AttrType { get => 3; }
		public int AttrValue;
		protected override void SerializeInfo(BinaryWriter writer) => writer.Write(AttrValue);
		protected override void UnSerializeInfo(BinaryReader reader) => AttrValue = reader.ReadInt32();
		public override string ToString() => AttrValue.ToString(CultureInfo.InvariantCulture);

		public override string ToFormatedString() => base.ToFormatedString() + ToString();
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Score : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "Score"; }
		public override byte AttrId { get => 38; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ArmyValue : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "ArmyValue"; }
		public override byte AttrId { get => 97; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__AssetsValue : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "AssetsValue"; }
		public override byte AttrId { get => 98; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__BuildingsDead : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "BuildingsDead"; }
		public override byte AttrId { get => 99; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__BuildingsKilled : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "BuildingsKilled"; }
		public override byte AttrId { get => 100; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Earned : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "Earned"; }
		public override byte AttrId { get => 101; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__UnitsDead : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "UnitsDead"; }
		public override byte AttrId { get => 102; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__UnitsKilled : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "UnitsKilled"; }
		public override byte AttrId { get => 103; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbHarvester : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbHarvester"; }
		public override byte AttrId { get => 32; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbCarryAll : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbCarryAll"; }
		public override byte AttrId { get => 33; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbFactory : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbFactory"; }
		public override byte AttrId { get => 34; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbHeavyFactory : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbHeavyFactory"; }
		public override byte AttrId { get => 35; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbBarrack : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbBarrack"; }
		public override byte AttrId { get => 36; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbStarPort : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbStarPort"; }
		public override byte AttrId { get => 37; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ORA__NbCommandCenter : S2A_PLAYER_EX_DATA_FIELD__Int
	{
		public override string Name { get => "NbCommandCenter"; }
		public override byte AttrId { get => 38; }
		public override bool AttrTypeCustom { get => true; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__String : A_S2A_PLAYER_EX_DATA_FIELD
	{
		public override string Name { get => "Unknown String Attribute"; }
		public override byte AttrType { get => 4; }
		public string AttrValue;
		protected override void SerializeInfo(BinaryWriter writer) => writer.WriteNullTerminated(AttrValue);
		protected override void UnSerializeInfo(BinaryReader reader) => AttrValue = reader.ReadNullTerminated();
		public override string ToString() => AttrValue;

		public override string ToFormatedString() => base.ToFormatedString() + ToString();
	}

	public class S2A_PLAYER_EX_DATA_FIELD__UUID : S2A_PLAYER_EX_DATA_FIELD__String
	{
		public override string Name { get => "PlayerUUID"; }
		public override byte AttrId { get => 32; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Name : S2A_PLAYER_EX_DATA_FIELD__String
	{
		public override string Name { get => "Name"; }
		public override byte AttrId { get => 33; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__FullName : S2A_PLAYER_EX_DATA_FIELD__String
	{
		public override string Name { get => "FullName"; }
		public override byte AttrId { get => 34; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__IP : S2A_PLAYER_EX_DATA_FIELD__String
	{
		public override string Name { get => "IP"; }
		public override byte AttrId { get => 40; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Faction : S2A_PLAYER_EX_DATA_FIELD__String
	{
		public override string Name { get => "Faction"; }
		public override byte AttrId { get => 45; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__Float32 : A_S2A_PLAYER_EX_DATA_FIELD
	{
		public override string Name { get => "Unknown Float Attribute"; }
		public override byte AttrType { get => 5; }
		public float AttrValue;
		protected override void SerializeInfo(BinaryWriter writer) => writer.Write(AttrValue);
		protected override void UnSerializeInfo(BinaryReader reader) => AttrValue = reader.ReadSingle();

		public override string ToString()
		{
			return AttrValue.ToString("0.#######", CultureInfo.InvariantCulture);
		}

		public override string ToFormatedString() => base.ToFormatedString() + ToString();
	}

	public class S2A_PLAYER_EX_DATA_FIELD__ConnectionTime : S2A_PLAYER_EX_DATA_FIELD__Float32
	{
		public override string Name { get => "ConnectionTime (s)"; }
		public override byte AttrId { get => 39; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__APM : S2A_PLAYER_EX_DATA_FIELD__Float32
	{
		public override string Name { get => "APM"; }
		public override byte AttrId { get => 96; }
	}

	public class S2A_PLAYER_EX_DATA_FIELD__MapExplored : S2A_PLAYER_EX_DATA_FIELD__Float32
	{
		public override string Name { get => "MapExplored (%)"; }
		public override byte AttrId { get => 104; }
	}

	public abstract class A_S2A_PLAYER_EX_DATA_FIELD : ISerializableMessage
	{
		public virtual string Name { get; protected set; } = "Unknown Attribute";
		public virtual byte AttrType { get; protected set; } = 0;
		public virtual bool AttrTypeCustom { get; protected set; } = false;
		public virtual bool AttrTypeExtended { get; protected set; } = false;
		public virtual byte AttrId { get; protected set; } = 0;
		public virtual byte ExtAttrId { get; protected set; } = 0;
		public override string ToString() => ToFormatedString();
		public virtual string ToFormatedString()
		{
			var sb = new StringBuilder();

			sb.Append($"[{AttrId}] {Name} ");

			if (AttrTypeCustom)
				sb.Append("[CUSTOM]");

			if (AttrTypeExtended)
				sb.Append("[EXT]");

			if (AttrTypeExtended || AttrTypeCustom)
				sb.Append(' ');

			sb.Append(": ");

			return sb.ToString();
		}

		public bool Identify(MemoryStream test_stream)
		{
			using (var reader = new BinaryReader(test_stream, new UTF8Encoding(), true))
			{
				var offset = 0;
				try
				{
					var rawAttrType = reader.ReadByte();
					offset--;
					var attrType = (byte)(rawAttrType & 0x3F);
					var attrTypeCustom = (rawAttrType & 0x40) != 0;
					var attrTypeExtended = (rawAttrType & 0x80) != 0;

					var attrId = reader.ReadByte();
					offset--;

					byte extAttrId = 0;
					if (attrTypeExtended)
					{
						extAttrId = reader.ReadByte();
						offset--;
					}

					return (attrType == AttrType)
						&& (attrId == AttrId)
						&& (attrTypeExtended == AttrTypeExtended)
						&& (attrTypeCustom == AttrTypeCustom)
						&& (extAttrId == ExtAttrId);
				}
				catch (EndOfStreamException)
				{
					return false;
				}
				finally
				{
					test_stream.Seek(offset, SeekOrigin.Current);
				}
			}
		}

		public bool IdentifyType(MemoryStream test_stream)
		{
			using (var reader = new BinaryReader(test_stream, new UTF8Encoding(), true))
			{
				var offset = 0;
				try
				{
					var rawAttrType = reader.ReadByte();
					offset--;
					var attrType = (byte)(rawAttrType & 0x3F);
					return attrType == AttrType;
				}
				catch (EndOfStreamException)
				{
					return false;
				}
				finally
				{
					test_stream.Seek(offset, SeekOrigin.Current);
				}
			}
		}

		protected abstract void SerializeInfo(BinaryWriter writer);
		protected abstract void UnSerializeInfo(BinaryReader reader);

		public MemoryStream Serialize()
		{
			var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(), true))
			{
				if (AttrType >= 0x3F || AttrType == 0)
					throw new Exception("Wrong AttrType");

				var attrType = (byte)(AttrType & 0x3F);

				if (AttrTypeCustom)
					attrType |= 0x40;

				if (AttrTypeExtended)
					attrType |= 0x80;
				else if (ExtAttrId > 0)
					throw new Exception("ExtAttrId seted but AttrTypeExtended isnt");

				writer.Write(attrType);

				writer.Write(AttrId);

				if (AttrTypeExtended)
					writer.Write(ExtAttrId);

				SerializeInfo(writer);
			}

			return stream;
		}

		public void UnSerialize(MemoryStream stream)
		{
			using (var reader = new BinaryReader(stream, new UTF8Encoding(), true))
			{
				var attrType = reader.ReadByte();
				AttrType = (byte)(attrType & 0x3F);
				AttrTypeCustom = (attrType & 0x40) != 0;
				AttrTypeExtended = (attrType & 0x80) != 0;

				AttrId = reader.ReadByte();

				if (AttrTypeExtended)
					ExtAttrId = reader.ReadByte();

				UnSerializeInfo(reader);
			}
		}
	}

	public sealed class S2A_PLAYER_EX_DATA : S2A_PLAYER_EX_DATA<S2A_PLAYER_EX_DATA_FIELD_Factory> { }

	public class S2A_PLAYER_EX_DATA<DATA_INFO_FACTORY> : ISerializableMessage
	where DATA_INFO_FACTORY : S2A_PLAYER_EX_DATA_FIELD_Factory<DATA_INFO_FACTORY>, new()
	{
		static readonly S2A_PLAYER_EX_DATA_FIELD_Factory<DATA_INFO_FACTORY> PlayerDataFieldFactory = S2A_PLAYER_EX_DATA_FIELD_Factory<DATA_INFO_FACTORY>.GetFactory();
		public int PlayerIndex;
		public IList<A_S2A_PLAYER_EX_DATA_FIELD> PlayerInfo { get; } = new List<A_S2A_PLAYER_EX_DATA_FIELD> { Capacity = 510 };

		public override string ToString()
		{
			var tmp = new StringBuilder();
			tmp.AppendLine($"PlayerIndex : {PlayerIndex}");
			foreach (var playerinfo in PlayerInfo)
				tmp.AppendLine("\t" + playerinfo.ToFormatedString().Replace(Environment.NewLine, Environment.NewLine + "\t"));

			return tmp.ToString();
		}

		public MemoryStream Serialize()
		{
			var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(), true))
			{
				lock (PlayerInfo)
				{
					writer.Write(PlayerIndex);
					writer.Write((short)PlayerInfo.Count);
					foreach (var playerinfo in PlayerInfo)
						playerinfo.Serialize().WriteTo(stream);
				}
			}

			return stream;
		}

		public void UnSerialize(MemoryStream stream)
		{
			using (var reader = new BinaryReader(stream, new UTF8Encoding(), true))
			{
				PlayerIndex = reader.ReadInt32();
				var playerInfoCount = reader.ReadInt16();
				lock (PlayerInfo)
				{
					PlayerInfo.Clear();
					for (var index = 0; index < playerInfoCount; index++)
						PlayerInfo.Add(PlayerDataFieldFactory.GetInfo(stream));
				}
			}
		}
	}

	public sealed class S2A_PLAYER_EX : Message
	{
		public override byte Header { get => 0x71; }
		public IList<S2A_PLAYER_EX_DATA> Players { get; } = new List<S2A_PLAYER_EX_DATA> { Capacity = 255 };
		protected override StringBuilder ToStringBuilder()
		{
			var sb = base.ToStringBuilder();

			sb.AppendLine("\tPlayers :");
			foreach (var p in Players)
				sb.AppendLine(p.ToString());

			return sb;
		}

		protected override void SerializePayload()
		{
			lock (Players)
			{
				writer.Write((byte)Players.Count);
				foreach (var player_data in Players)
					player_data.Serialize().WriteTo(stream);
			}
		}

		protected override void UnSerializePayload()
		{
			var nbPlayers = reader.ReadByte();
			lock (Players)
			{
				Players.Clear();
				for (var index = 0; index < nbPlayers; index++)
				{
					var new_player = new S2A_PLAYER_EX_DATA();
					new_player.UnSerialize(stream);
					Players.Add(new_player);
				}
			}
		}
	}
}
